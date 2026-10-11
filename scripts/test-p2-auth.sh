#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_name="kuechenfuchs-p2-auth-test-$$"
http_port="${HTTP_PORT:-54392}"
cookie_jar="$(mktemp)"
response_body="$(mktemp)"
response_headers="$(mktemp)"
export HTTP_PORT="$http_port"
export POSTGRES_DB="${POSTGRES_DB:-kuechenfuchs}"
export POSTGRES_USER="${POSTGRES_USER:-kuechenfuchs}"
export POSTGRES_PASSWORD="p2-auth-smoke-only"
test_admin_email="p2-auth-smoke@example.test"
test_admin_password="P2-Smoke-$(openssl rand -hex 24)aA9!"
export INITIAL_ADMIN_EMAIL="$test_admin_email"
export INITIAL_ADMIN_PASSWORD="$test_admin_password"
export INITIAL_HOUSEHOLD_NAME="P2 Auth Smoke Test"
export AUTH_COOKIE_LIFETIME_SECONDS=10
cd "$root_dir"

compose() {
  docker compose --project-name "$project_name" "$@"
}

cleanup() {
  local exit_status=$?
  trap - EXIT
  compose down --volumes --remove-orphans || exit_status=$?
  rm -f "$cookie_jar" "$response_body" "$response_headers" || exit_status=$?
  exit "$exit_status"
}

fail() {
  echo "P2 auth integration test failed: $*" >&2
  compose ps >&2 || true
  compose logs api >&2 || true
  exit 1
}

assert_status() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  if [[ "$actual" != "$expected" ]]; then
    fail "$label expected HTTP $expected but got $actual: $(cat "$response_body")"
  fi
}

trap cleanup EXIT

echo "Validating and building the isolated Compose stack..."
compose config --quiet
compose build
compose up --detach

echo "Waiting for API migrations and database readiness..."
ready=false
for _ in {1..60}; do
  if curl --fail --silent --output /dev/null "http://127.0.0.1:${http_port}/api/health/ready"; then
    ready=true
    break
  fi
  sleep 2
done
[[ "$ready" == true ]] || fail "API did not become ready."

echo "Creating the first administrator in a disposable bootstrap container..."
compose --profile bootstrap run --rm bootstrap
bootstrap_container_id="$(compose ps --all --quiet bootstrap)"
[[ -z "$bootstrap_container_id" ]] || fail "The one-time bootstrap container was not removed."

api_container_id="$(compose ps --quiet api)"
api_environment="$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$api_container_id")"
if grep --quiet '^Bootstrap__' <<< "$api_environment"; then
  fail "Bootstrap configuration was retained in the long-running API container."
fi

base_url="http://127.0.0.1:${http_port}"
status="$(curl --silent --output "$response_body" --write-out '%{http_code}' "$base_url/api/auth/session")"
assert_status 401 "$status" "Anonymous session check"

csrf_response="$(curl --fail --silent --show-error --cookie-jar "$cookie_jar" "$base_url/api/auth/csrf")"
csrf_token="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).requestToken)' "$csrf_response")"
[[ -n "$csrf_token" ]] || fail "CSRF endpoint returned an empty request token."

status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --cookie "$cookie_jar" \
  --header 'Content-Type: application/json' \
  --data "{\"email\":\"${test_admin_email}\",\"password\":\"${test_admin_password}\"}" \
  "$base_url/api/auth/login")"
assert_status 400 "$status" "Login without CSRF header"

status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --dump-header "$response_headers" \
  --cookie "$cookie_jar" --cookie-jar "$cookie_jar" \
  --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: ${csrf_token}" \
  --data '{"email":"p2-auth-smoke@example.test","password":"wrong-password"}' \
  "$base_url/api/auth/login")"
assert_status 401 "$status" "Invalid credentials"

status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --dump-header "$response_headers" \
  --cookie "$cookie_jar" --cookie-jar "$cookie_jar" \
  --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: ${csrf_token}" \
  --data "{\"email\":\"${test_admin_email}\",\"password\":\"${test_admin_password}\"}" \
  "$base_url/api/auth/login")"
assert_status 200 "$status" "Valid login"
identity_cookie="$(grep --ignore-case --extended-regexp '^set-cookie:[[:space:]]*KF\.Identity=' "$response_headers" | tr -d '\r' || true)"
[[ -n "$identity_cookie" ]] || fail "Authentication cookie was not issued."
grep --ignore-case --extended-regexp --quiet '(^|;)[[:space:]]*httponly([;]|$)' <<< "$identity_cookie" \
  || fail "Authentication cookie is missing HttpOnly."
grep --ignore-case --extended-regexp --quiet '(^|;)[[:space:]]*samesite=strict([;]|$)' <<< "$identity_cookie" \
  || fail "Authentication cookie is missing SameSite=Strict."

session="$(curl --fail --silent --show-error --cookie "$cookie_jar" "$base_url/api/auth/session")"
household_id="$(node -e 'const s=JSON.parse(process.argv[1]); if(s.households.length!==1||s.households[0].role!=="Owner")process.exit(1); process.stdout.write(s.households[0].householdId)' "$session")"
[[ -n "$household_id" ]] || fail "Bootstrap user does not own exactly one household."

status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --request POST --cookie "$cookie_jar" \
  --header 'Content-Type: application/json' \
  --data '{"email":"member@example.test"}' \
  "$base_url/api/v1/households/${household_id}/members")"
assert_status 400 "$status" "Household-member write without CSRF header"

other_household_id="$(node -p 'require("node:crypto").randomUUID()')"
compose exec --no-TTY db psql --set ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --command "INSERT INTO \"Households\" (\"Id\", \"Name\", \"CreatedAt\") VALUES ('$other_household_id', 'Unrelated household', now());" >/dev/null
status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --cookie "$cookie_jar" "$base_url/api/v1/households/${other_household_id}")"
assert_status 404 "$status" "Cross-household access"

csrf_response="$(curl --fail --silent --show-error --cookie "$cookie_jar" --cookie-jar "$cookie_jar" "$base_url/api/auth/csrf")"
csrf_token="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).requestToken)' "$csrf_response")"
status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --request POST --cookie "$cookie_jar" --cookie-jar "$cookie_jar" \
  "$base_url/api/auth/logout")"
assert_status 400 "$status" "Logout without CSRF header"

status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --request POST --cookie "$cookie_jar" --cookie-jar "$cookie_jar" \
  --header "X-KF-CSRF: ${csrf_token}" \
  "$base_url/api/auth/logout")"
assert_status 204 "$status" "Valid logout"

status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --cookie "$cookie_jar" "$base_url/api/auth/session")"
assert_status 401 "$status" "Session after logout"

csrf_response="$(curl --fail --silent --show-error --cookie-jar "$cookie_jar" "$base_url/api/auth/csrf")"
csrf_token="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).requestToken)' "$csrf_response")"
status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --cookie "$cookie_jar" --cookie-jar "$cookie_jar" \
  --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: ${csrf_token}" \
  --data "{\"email\":\"${INITIAL_ADMIN_EMAIL}\",\"password\":\"${INITIAL_ADMIN_PASSWORD}\"}" \
  "$base_url/api/auth/login")"
assert_status 200 "$status" "Login before expiry test"

echo "Removing bootstrap values from the process environment and recreating the API..."
unset INITIAL_ADMIN_EMAIL INITIAL_ADMIN_PASSWORD INITIAL_HOUSEHOLD_NAME
compose up --detach --force-recreate api
ready=false
for _ in {1..30}; do
  if curl --fail --silent --output /dev/null "$base_url/api/health/ready"; then
    ready=true
    break
  fi
  sleep 2
done
[[ "$ready" == true ]] || fail "API did not become ready after bootstrap configuration was removed."

api_container_id="$(compose ps --quiet api)"
api_environment="$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$api_container_id")"
if grep --quiet '^Bootstrap__' <<< "$api_environment"; then
  fail "Bootstrap configuration was present after recreating the API."
fi

csrf_response="$(curl --fail --silent --show-error --cookie-jar "$cookie_jar" "$base_url/api/auth/csrf")"
csrf_token="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).requestToken)' "$csrf_response")"
status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --cookie "$cookie_jar" --cookie-jar "$cookie_jar" \
  --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: ${csrf_token}" \
  --data '{"email":"p2-auth-smoke@example.test","password":"wrong-password"}' \
  "$base_url/api/auth/login")"
assert_status 401 "$status" "Invalid login after bootstrap configuration removal"

status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --cookie "$cookie_jar" --cookie-jar "$cookie_jar" \
  --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: ${csrf_token}" \
  --data "{\"email\":\"${test_admin_email}\",\"password\":\"${test_admin_password}\"}" \
  "$base_url/api/auth/login")"
assert_status 200 "$status" "Administrator login after bootstrap configuration removal"
session="$(curl --fail --silent --show-error --cookie "$cookie_jar" "$base_url/api/auth/session")"
preserved_household_id="$(node -e 'const s=JSON.parse(process.argv[1]); if(s.households.length!==1||s.households[0].role!=="Owner")process.exit(1); process.stdout.write(s.households[0].householdId)' "$session")"
[[ "$preserved_household_id" == "$household_id" ]] || fail "Household was not preserved after API recreation."

sleep 11
status="$(curl --silent --output "$response_body" --write-out '%{http_code}' \
  --cookie "$cookie_jar" "$base_url/api/auth/session")"
assert_status 401 "$status" "Expired session"

echo "P2 auth integration tests passed: disposable bootstrap, secret-free API recreation, CSRF, valid/invalid login, cookie flags, household isolation, logout and expiry."
