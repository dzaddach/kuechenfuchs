#!/usr/bin/env bash
set -euo pipefail

# P5b: ChatGPT-Rezeptaktionen mit simulierten ChatGPT-Anfragen. Isolierter Stack mit Keycloak unter /auth
# derselben Origin und dem lokalen Fake-KI-Anbieter aus P4. Läuft bewusst in Development: In Production
# erlaubt die API als KI-Endpunkt ausschließlich https://api.openai.com. Die Production-Aspekte (HTTPS-Edge,
# __Host-Cookies, Proxy-Vertrauen) prüft scripts/test-p5a2-chatgpt-oauth.sh. Synthetische Daten, Wegwerf-Volumes.
root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_name="kuechenfuchs-p5b-recipes-test-$$"
export HTTP_PORT="${HTTP_PORT:-54402}"
export KEYCLOAK_ADMIN_PORT="${KEYCLOAK_ADMIN_PORT:-54403}"
export KUECHENFUCHS_PUBLIC_URL="http://localhost:${HTTP_PORT}"
export POSTGRES_DB="${POSTGRES_DB:-kuechenfuchs}"
export POSTGRES_USER="${POSTGRES_USER:-kuechenfuchs}"
export POSTGRES_PASSWORD="p5b-$(openssl rand -hex 16)"
export KEYCLOAK_DB_PASSWORD="$(openssl rand -hex 24)"
export KEYCLOAK_ADMIN_USERNAME="p5b-test-admin"
export KEYCLOAK_ADMIN_PASSWORD="$(openssl rand -hex 24)"
export KEYCLOAK_LINK_CLIENT_SECRET="$(openssl rand -hex 32)"
export KEYCLOAK_CHATGPT_ACTIONS_CLIENT_SECRET="$(openssl rand -hex 32)"
export KEYCLOAK_API_ADMIN_CLIENT_SECRET="$(openssl rand -hex 32)"
export KEYCLOAK_CLOCK_SKEW_SECONDS=0
export CHATGPT_BEARER_REQUESTS_PER_MINUTE=300
export INITIAL_ADMIN_EMAIL="p5b-recipes-smoke@example.test"
export INITIAL_ADMIN_PASSWORD="P5b-Smoke-$(openssl rand -hex 24)aA9!"
export INITIAL_HOUSEHOLD_NAME="P5b Rezepte Test"
export P5A_COMPOSE_ARGS="[\"-f\",\"compose.yaml\",\"-f\",\"compose.keycloak.yaml\",\"-f\",\"compose.p4-test.yaml\",\"--profile\",\"p4-test\",\"--project-name\",\"${project_name}\"]"
cd "$root_dir"

compose() {
  docker compose -f compose.yaml -f compose.keycloak.yaml -f compose.p4-test.yaml --profile p4-test \
    --project-name "$project_name" "$@"
}

cleanup() {
  local exit_status=$?
  trap - EXIT
  compose down --volumes --remove-orphans || exit_status=$?
  exit "$exit_status"
}

fail() {
  echo "P5b ChatGPT recipe integration test failed: $*" >&2
  compose ps >&2 || true
  compose logs api keycloak fake-openai >&2 || true
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

echo "Validating and building the isolated Compose stack (Keycloak + fake AI provider)..."
compose config --quiet
compose build
compose up --detach

echo "Waiting for API and Keycloak..."
wait_for "API" 60 curl --fail --silent --output /dev/null "http://127.0.0.1:${HTTP_PORT}/api/health/ready"
wait_for "Keycloak" 120 keycloak_healthy
wait_for "Keycloak realm" 60 curl --fail --silent --output /dev/null \
  "http://127.0.0.1:${HTTP_PORT}/auth/realms/kuechenfuchs/.well-known/openid-configuration"

echo "Creating the first Küchenfuchs administrator in a disposable bootstrap container..."
compose --profile bootstrap run --rm bootstrap

node scripts/test-p5b-chatgpt-recipes.mjs || fail "End-to-end checks failed."
