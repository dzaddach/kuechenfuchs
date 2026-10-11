#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_name="kuechenfuchs-p4-ai-test-$$"
http_port="${HTTP_PORT:-54395}"
cookie_jar="$(mktemp)"
response_body="$(mktemp)"
response_headers="$(mktemp)"
export HTTP_PORT="$http_port"
export POSTGRES_DB="${POSTGRES_DB:-kuechenfuchs}"
export POSTGRES_USER="${POSTGRES_USER:-kuechenfuchs}"
export POSTGRES_PASSWORD="p4-ai-smoke-only"
export INITIAL_ADMIN_EMAIL="p4-ai-smoke@example.test"
export INITIAL_ADMIN_PASSWORD="P4-Smoke-$(openssl rand -hex 24)aA9!"
export INITIAL_HOUSEHOLD_NAME="P4 AI Recipe Test"
cd "$root_dir"

compose() {
  docker compose -f compose.yaml -f compose.p4-test.yaml --profile p4-test \
    --project-name "$project_name" "$@"
}

cleanup() {
  local exit_status=$?
  trap - EXIT
  compose down --volumes --remove-orphans || exit_status=$?
  rm -f "$cookie_jar" "$response_body" "$response_headers" || exit_status=$?
  exit "$exit_status"
}

fail() {
  echo "P4 AI recipe integration test failed: $*" >&2
  compose ps >&2 || true
  compose logs api fake-openai >&2 || true
  exit 1
}

assert_status() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  [[ "$actual" == "$expected" ]] || fail "$label expected HTTP $expected but got $actual: $(cat "$response_body")"
}

request() {
  curl --silent --show-error --max-time 8 --output "$response_body" --dump-header "$response_headers" \
    --write-out '%{http_code}' "$@"
}

ai_request() {
  local prompt="$1"
  local servings="${2:-3}"
  local payload
  payload="$(node -e 'process.stdout.write(JSON.stringify({prompt:process.argv[1],desiredServings:Number(process.argv[2]),availableIngredients:["Kartoffeln","Paprika"],preferences:["schnell"],avoidIngredients:[],externalProcessingConfirmed:true}))' "$prompt" "$servings")"
  request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
    --header "X-KF-CSRF: $csrf_token" --data "$payload" \
    "$base_url/api/v1/households/$household_id/ai/recipe-drafts"
}

trap cleanup EXIT
compose config --quiet
compose build
compose up --detach

base_url="http://127.0.0.1:${http_port}"
ready=false
for _ in {1..60}; do
  if curl --fail --silent --output /dev/null "$base_url/api/health/ready"; then
    ready=true
    break
  fi
  sleep 2
done
[[ "$ready" == true ]] || fail "API and fresh-database AI migration did not become ready."
compose --profile bootstrap run --rm bootstrap

status="$(request "$base_url/api/openapi/v1.json")"
assert_status 200 "$status" "Development OpenAPI document"
node -e 'const d=JSON.parse(process.argv[1]); if(!d.paths["/api/v1/households/{householdId}/ai/recipe-drafts"]?.post)process.exit(1)' "$(cat "$response_body")" \
  || fail "OpenAPI document did not describe the P4 AI draft endpoint."

status="$(request --cookie-jar "$cookie_jar" "$base_url/api/auth/csrf")"
assert_status 200 "$status" "CSRF token bootstrap"
csrf_token="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).requestToken)' "$(cat "$response_body")")"
status="$(request --cookie "$cookie_jar" --cookie-jar "$cookie_jar" \
  --header 'Content-Type: application/json' --header "X-KF-CSRF: $csrf_token" \
  --data "{\"email\":\"$INITIAL_ADMIN_EMAIL\",\"password\":\"$INITIAL_ADMIN_PASSWORD\"}" "$base_url/api/auth/login")"
assert_status 200 "$status" "Administrator login"
session="$(cat "$response_body")"
household_id="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).households[0].householdId)' "$session")"
status="$(request --cookie "$cookie_jar" --cookie-jar "$cookie_jar" "$base_url/api/auth/csrf")"
assert_status 200 "$status" "Authenticated CSRF token"
csrf_token="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).requestToken)' "$(cat "$response_body")")"

foreign_household_id="$(node -p 'require("node:crypto").randomUUID()')"
status="$(request --header 'Content-Type: application/json' --data '{}' \
  "$base_url/api/v1/households/$household_id/ai/recipe-drafts")"
assert_status 401 "$status" "Anonymous AI request"
status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: $csrf_token" --data '{}' \
  "$base_url/api/v1/households/$foreign_household_id/ai/recipe-drafts")"
assert_status 404 "$status" "Foreign household AI request"

status="$(ai_request "Ich möchte Kartoffeln kochen.")"
assert_status 200 "$status" "Valid structured AI draft"
node -e 'const r=JSON.parse(process.argv[1]); if(r.recipe.name!=="Kartoffelpfanne"||r.recipe.baseServings!==3||r.recipe.ingredients[0].foodId!==null||!r.warnings.length)process.exit(1)' "$(cat "$response_body")" \
  || fail "The validated AI draft or its uncertainty warnings were not returned."
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$household_id/recipes")"
assert_status 200 "$status" "Recipe list after unsaved AI draft"
node -e 'if(JSON.parse(process.argv[1]).length!==0)process.exit(1)' "$(cat "$response_body")" \
  || fail "AI generation stored a recipe without user confirmation."

status="$(ai_request "FAKE: invalid json")"
assert_status 502 "$status" "Malformed structured AI JSON"
status="$(ai_request "FAKE: unknown unit")"
assert_status 502 "$status" "Unknown AI unit"
status="$(ai_request "FAKE: invalid amount")"
assert_status 502 "$status" "Implausible AI quantity"
status="$(ai_request "FAKE: invalid servings")"
assert_status 502 "$status" "Implausible AI servings"
status="$(ai_request "FAKE: provider rate limit")"
assert_status 429 "$status" "OpenAI provider rate limit"
status="$(ai_request "FAKE: timeout")"
assert_status 502 "$status" "OpenAI provider timeout"

status="$(ai_request "Kartoffeln für ein schnelles Abendessen.")"
assert_status 200 "$status" "Second valid AI draft"
draft="$(cat "$response_body")"
node -e 'const r=JSON.parse(process.argv[1]);r.recipe.name="Von mir geprüftes Rezept";process.stdout.write(JSON.stringify(r.recipe))' "$draft" >"$response_body"
status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: $csrf_token" --data "$(cat "$response_body")" \
  "$base_url/api/v1/households/$household_id/recipes")"
assert_status 201 "$status" "Explicit user-approved recipe persistence"
node -e 'if(JSON.parse(process.argv[1]).name!=="Von mir geprüftes Rezept")process.exit(1)' "$(cat "$response_body")" \
  || fail "The explicitly edited recipe was not stored by the existing recipe API."

status="$(ai_request "Monthly cost cap check")"
assert_status 429 "$status" "Monthly AI cost budget"
node -e 'if(JSON.parse(process.argv[1]).title!=="Monthly AI budget reached")process.exit(1)' "$(cat "$response_body")" \
  || fail "Cost cap rejection did not explain that the monthly budget was reached."
status="$(ai_request "Monthly cost cap remains enforced")"
assert_status 429 "$status" "Repeated monthly AI cost budget"

status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --data '{}' "$base_url/api/v1/households/$household_id/ai/recipe-drafts")"
assert_status 400 "$status" "AI request without CSRF token"
status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header 'X-KF-CSRF: invalid-token' --data '{}' \
  "$base_url/api/v1/households/$household_id/ai/recipe-drafts")"
assert_status 400 "$status" "AI request with invalid CSRF token"

status="$(ai_request "Rate limit check")"
assert_status 429 "$status" "Per-household AI request rate limit"
node -e 'if(JSON.parse(process.argv[1]).title!=="Request rate limit reached")process.exit(1)' "$(cat "$response_body")" \
  || fail "Rate-limit rejection did not use the expected problem response."

echo "P4 AI recipe integration checks passed; all provider traffic used the local fake service."
