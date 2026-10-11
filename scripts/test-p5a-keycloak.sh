#!/usr/bin/env bash
set -euo pipefail

# P5a.1: isolierter Keycloak-Teststack mit Wegwerf-Volumes und zufälligen Secrets.
# Keine öffentlichen Ports außer 127.0.0.1, keine produktiven Daten, keine ChatGPT-Aufrufe.
root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_name="kuechenfuchs-p5a-keycloak-test-$$"
export HTTP_PORT="${HTTP_PORT:-54396}"
export KEYCLOAK_PORT="${KEYCLOAK_PORT:-54397}"
export POSTGRES_DB="${POSTGRES_DB:-kuechenfuchs}"
export POSTGRES_USER="${POSTGRES_USER:-kuechenfuchs}"
export POSTGRES_PASSWORD="p5a-$(openssl rand -hex 16)"
export KEYCLOAK_DB_PASSWORD="$(openssl rand -hex 24)"
export KEYCLOAK_ADMIN_USERNAME="p5a-test-admin"
export KEYCLOAK_ADMIN_PASSWORD="$(openssl rand -hex 24)"
export KEYCLOAK_LINK_CLIENT_SECRET="$(openssl rand -hex 32)"
export KEYCLOAK_CHATGPT_ACTIONS_CLIENT_SECRET="$(openssl rand -hex 32)"
export KEYCLOAK_CLOCK_SKEW_SECONDS=0
export INITIAL_ADMIN_EMAIL="p5a-keycloak-smoke@example.test"
export INITIAL_ADMIN_PASSWORD="P5a-Smoke-$(openssl rand -hex 24)aA9!"
export INITIAL_HOUSEHOLD_NAME="P5a Keycloak Test"
export P5A_COMPOSE_ARGS="[\"-f\",\"compose.yaml\",\"-f\",\"compose.keycloak.yaml\",\"--project-name\",\"${project_name}\"]"
cd "$root_dir"

compose() {
  docker compose -f compose.yaml -f compose.keycloak.yaml --project-name "$project_name" "$@"
}

cleanup() {
  local exit_status=$?
  trap - EXIT
  compose down --volumes --remove-orphans || exit_status=$?
  exit "$exit_status"
}

fail() {
  echo "P5a Keycloak integration test failed: $*" >&2
  compose ps >&2 || true
  compose logs api keycloak >&2 || true
  exit 1
}

wait_for() {
  local label="$1"
  local attempts="$2"
  shift 2
  for _ in $(seq 1 "$attempts"); do
    if "$@" >/dev/null 2>&1; then
      return 0
    fi
    sleep 5
  done
  fail "$label did not become ready."
}

keycloak_healthy() {
  [[ "$(docker inspect --format '{{.State.Health.Status}}' "$(compose ps --quiet keycloak)")" == "healthy" ]]
}

trap cleanup EXIT

echo "Validating and building the isolated Compose stack with Keycloak..."
compose config --quiet
compose build
compose up --detach

echo "Waiting for API, PostgreSQL and Keycloak..."
wait_for "API" 60 curl --fail --silent --output /dev/null "http://127.0.0.1:${HTTP_PORT}/api/health/ready"
wait_for "Keycloak" 120 keycloak_healthy
wait_for "Keycloak realm" 60 curl --fail --silent --output /dev/null \
  "http://127.0.0.1:${KEYCLOAK_PORT}/realms/kuechenfuchs/.well-known/openid-configuration"

echo "Checking network exposure..."
for service in db keycloak-db api; do
  published="$(docker inspect --format '{{json .NetworkSettings.Ports}}' "$(compose ps --quiet "$service")")"
  if grep --quiet 'HostPort' <<< "$published"; then
    fail "Service $service must not publish a host port."
  fi
done
keycloak_binding="$(compose port keycloak 8080)"
[[ "$keycloak_binding" == "127.0.0.1:${KEYCLOAK_PORT}" ]] || fail "Keycloak must be bound to loopback only, got $keycloak_binding."
api_networks="$(docker inspect --format '{{range $name, $_ := .NetworkSettings.Networks}}{{$name}} {{end}}' "$(compose ps --quiet api)")"
if grep --quiet 'keycloak-backend' <<< "$api_networks"; then
  fail "API must not reach the Keycloak database network."
fi

echo "Creating the first Küchenfuchs administrator in a disposable bootstrap container..."
compose --profile bootstrap run --rm bootstrap

node scripts/test-p5a-keycloak.mjs || fail "End-to-end checks failed."
