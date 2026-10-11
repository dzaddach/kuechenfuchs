#!/usr/bin/env bash
set -euo pipefail

# P5a.2: produktionsnaher, isolierter Teststack (API in Production, Keycloak unter /auth, simulierter
# Heimserver-Caddy mit HTTPS über lokale Test-CA). Zufällige Secrets, synthetische Daten, Wegwerf-Volumes.
# Keine öffentlichen Ports außer 127.0.0.1, kein echter ChatGPT-Zugang, keine Änderung anderer Stacks.
root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_name="kuechenfuchs-p5a2-oauth-test-$$"
work_dir="$(mktemp -d)"
export HTTP_PORT="${HTTP_PORT:-54398}"
export KEYCLOAK_ADMIN_PORT="${KEYCLOAK_ADMIN_PORT:-54399}"
export EDGE_HTTPS_PORT="${EDGE_HTTPS_PORT:-54443}"
export KUECHENFUCHS_PUBLIC_URL="https://localhost:${EDGE_HTTPS_PORT}"
export POSTGRES_DB="${POSTGRES_DB:-kuechenfuchs}"
export POSTGRES_USER="${POSTGRES_USER:-kuechenfuchs}"
export POSTGRES_PASSWORD="p5a2-$(openssl rand -hex 16)"
export KEYCLOAK_DB_PASSWORD="$(openssl rand -hex 24)"
export KEYCLOAK_ADMIN_USERNAME="p5a2-test-admin"
export KEYCLOAK_ADMIN_PASSWORD="$(openssl rand -hex 24)"
export KEYCLOAK_LINK_CLIENT_SECRET="$(openssl rand -hex 32)"
export KEYCLOAK_CHATGPT_ACTIONS_CLIENT_SECRET="$(openssl rand -hex 32)"
export KEYCLOAK_API_ADMIN_CLIENT_SECRET="$(openssl rand -hex 32)"
export KEYCLOAK_CLOCK_SKEW_SECONDS=0
export CHATGPT_BEARER_REQUESTS_PER_MINUTE=40
export INITIAL_ADMIN_EMAIL="p5a2-oauth-smoke@example.test"
export INITIAL_ADMIN_PASSWORD="P5a2-Smoke-$(openssl rand -hex 24)aA9!"
export INITIAL_HOUSEHOLD_NAME="P5a2 OAuth Test"
export P5A_PROJECT="$project_name"
export P5A_COMPOSE_FILES="-f compose.yaml -f compose.keycloak.yaml -f compose.homeserver.yaml -f compose.p5a2-test.yaml"
export P5A_COMPOSE_ARGS="[\"-f\",\"compose.yaml\",\"-f\",\"compose.keycloak.yaml\",\"-f\",\"compose.homeserver.yaml\",\"-f\",\"compose.p5a2-test.yaml\",\"--project-name\",\"${project_name}\"]"
cd "$root_dir"

compose() {
  # shellcheck disable=SC2086
  docker compose $P5A_COMPOSE_FILES --project-name "$project_name" "$@"
}

cleanup() {
  local exit_status=$?
  trap - EXIT
  compose down --volumes --remove-orphans || exit_status=$?
  rm -rf "$work_dir" || exit_status=$?
  exit "$exit_status"
}

fail() {
  echo "P5a.2 ChatGPT OAuth integration test failed: $*" >&2
  compose ps >&2 || true
  compose logs api keycloak edge >&2 || true
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

echo "Validating and building the production-like Compose stack..."
compose config --quiet
compose build
compose up --detach

echo "Waiting for migration, API, Keycloak and HTTPS edge..."
[[ "$(docker inspect --format '{{.State.ExitCode}}' "$(compose ps --all --quiet migrate)")" == "0" ]] || fail "Migration step failed."
wait_for "Keycloak" 120 keycloak_healthy
wait_for "Edge CA" 30 compose cp edge:/data/caddy/pki/authorities/local/root.crt "$work_dir/root.crt"
export NODE_EXTRA_CA_CERTS="$work_dir/root.crt"
wait_for "API via HTTPS" 60 curl --fail --silent --cacert "$work_dir/root.crt" --output /dev/null "${KUECHENFUCHS_PUBLIC_URL}/api/health/ready"
wait_for "Keycloak realm via HTTPS" 60 curl --fail --silent --cacert "$work_dir/root.crt" --output /dev/null \
  "${KUECHENFUCHS_PUBLIC_URL}/auth/realms/kuechenfuchs/.well-known/openid-configuration"

echo "Checking network exposure..."
for service in db keycloak-db api; do
  published="$(docker inspect --format '{{json .NetworkSettings.Ports}}' "$(compose ps --quiet "$service")")"
  if grep --quiet 'HostPort' <<< "$published"; then
    fail "Service $service must not publish a host port."
  fi
done
[[ "$(compose port keycloak 8080)" == "127.0.0.1:${KEYCLOAK_ADMIN_PORT}" ]] || fail "Keycloak admin port must be loopback-only."
[[ "$(compose port caddy 80)" == "127.0.0.1:${HTTP_PORT}" ]] || fail "Küchenfuchs web port must be loopback-only."
[[ "$(compose port edge "${EDGE_HTTPS_PORT}")" == "127.0.0.1:${EDGE_HTTPS_PORT}" ]] || fail "Test edge must be loopback-only."
api_environment="$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$(compose ps --quiet api)")"
grep --quiet '^ASPNETCORE_ENVIRONMENT=Production$' <<< "$api_environment" || fail "API must run in Production."

echo "Creating the first Küchenfuchs administrator in a disposable bootstrap container..."
compose --profile bootstrap run --rm bootstrap

node scripts/test-p5a2-chatgpt-oauth.mjs || fail "End-to-end checks failed."
