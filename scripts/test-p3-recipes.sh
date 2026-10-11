#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_name="kuechenfuchs-p3-recipes-test-$$"
http_port="${HTTP_PORT:-54393}"
cookie_jar="$(mktemp)"
response_body="$(mktemp)"
response_headers="$(mktemp)"
concurrent_body_a="$(mktemp)"
concurrent_body_b="$(mktemp)"
concurrent_headers_a="$(mktemp)"
concurrent_headers_b="$(mktemp)"
concurrent_status_a="$(mktemp)"
concurrent_status_b="$(mktemp)"
export HTTP_PORT="$http_port"
export POSTGRES_DB="${POSTGRES_DB:-kuechenfuchs}"
export POSTGRES_USER="${POSTGRES_USER:-kuechenfuchs}"
export POSTGRES_PASSWORD="p3-recipes-smoke-only"
export INITIAL_ADMIN_EMAIL="p3-recipes-smoke@example.test"
export INITIAL_ADMIN_PASSWORD="P3-Smoke-$(openssl rand -hex 24)aA9!"
export INITIAL_HOUSEHOLD_NAME="P3 Recipe Smoke Test"
cd "$root_dir"

compose() {
  docker compose --project-name "$project_name" "$@"
}

cleanup() {
  local exit_status=$?
  trap - EXIT
  compose down --volumes --remove-orphans || exit_status=$?
  rm -f "$cookie_jar" "$response_body" "$response_headers" "$concurrent_body_a" "$concurrent_body_b" \
    "$concurrent_headers_a" "$concurrent_headers_b" "$concurrent_status_a" "$concurrent_status_b" || exit_status=$?
  exit "$exit_status"
}

fail() {
  echo "P3 recipe integration test failed: $*" >&2
  compose ps >&2 || true
  compose logs api >&2 || true
  exit 1
}

assert_status() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  [[ "$actual" == "$expected" ]] || fail "$label expected HTTP $expected but got $actual: $(cat "$response_body")"
}

request() {
  curl --silent --show-error --output "$response_body" --dump-header "$response_headers" \
    --write-out '%{http_code}' "$@"
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
[[ "$ready" == true ]] || fail "API and fresh-database migrations did not become ready."
compose --profile bootstrap run --rm bootstrap

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

status="$(request "$base_url/api/v1/households/$household_id/recipes")"
assert_status 401 "$status" "Anonymous recipe list"

status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --data '{}' "$base_url/api/v1/households/$household_id/recipes")"
assert_status 400 "$status" "Recipe creation without CSRF token"
status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header 'X-KF-CSRF: invalid-token' --data '{}' "$base_url/api/v1/households/$household_id/recipes")"
assert_status 400 "$status" "Recipe creation with invalid CSRF token"

other_household_id="$(node -p 'require("node:crypto").randomUUID()')"
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$other_household_id/recipes")"
assert_status 404 "$status" "Cross-household recipe list"

status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$household_id/units")"
assert_status 200 "$status" "Unit catalog"
node -e 'const codes=JSON.parse(process.argv[1]).map(x=>x.code); if(!codes.includes("g")||!codes.includes("ml"))process.exit(1)' "$(cat "$response_body")" \
  || fail "Expected stable mass and volume unit codes."
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$household_id/foods?q=Nudeln")"
assert_status 200 "$status" "Food catalog search"
food_id="$(node -e 'const foods=JSON.parse(process.argv[1]); if(foods.length!==1||foods[0].name!=="Nudeln")process.exit(1); process.stdout.write(foods[0].id)' "$(cat "$response_body")")"
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$household_id/foods/$food_id")"
assert_status 200 "$status" "Food detail"
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$other_household_id/foods")"
assert_status 404 "$status" "Cross-household food catalog"

recipe_json="$(node -e 'process.stdout.write(JSON.stringify({name:"Zitronenpasta",description:"Frisch und schnell",baseServings:2,preparationMinutes:20,category:"Abendessen",tags:["vegetarisch"],ingredients:[{name:"Nudeln",amount:250,unitCode:"g",foodId:"e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc001"},{name:"Zitrone",amount:null,unitCode:"piece",foodId:null}],steps:[{description:"Nudeln kochen."},{description:"Mit Zitrone servieren."}]}))')"
status="$(request --cookie "$cookie_jar" --cookie-jar "$cookie_jar" \
  --header 'Content-Type: application/json' --header "X-KF-CSRF: $csrf_token" \
  --data "$recipe_json" "$base_url/api/v1/households/$household_id/recipes")"
assert_status 201 "$status" "Recipe creation"
recipe_id="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).id)' "$(cat "$response_body")")"
etag="$(grep -i '^ETag:' "$response_headers" | tr -d '\r' | cut -d' ' -f2-)"
[[ -n "$etag" ]] || fail "Recipe creation did not return an ETag."

status="$(request --cookie "$cookie_jar" --request PUT \
  "$base_url/api/v1/households/$household_id/recipes/$recipe_id/favorite")"
assert_status 400 "$status" "Favorite write without CSRF token"
status="$(request --cookie "$cookie_jar" --header 'X-KF-CSRF: invalid-token' --request PUT \
  "$base_url/api/v1/households/$household_id/recipes/$recipe_id/favorite")"
assert_status 400 "$status" "Favorite write with invalid CSRF token"
status="$(request --cookie "$cookie_jar" --header "X-KF-CSRF: $csrf_token" --request PUT \
  "$base_url/api/v1/households/$household_id/recipes/$recipe_id/favorite")"
assert_status 204 "$status" "Favorite creation"
status="$(request --cookie "$cookie_jar" --header "X-KF-CSRF: $csrf_token" --request PUT \
  "$base_url/api/v1/households/$household_id/recipes/$recipe_id/favorite")"
assert_status 204 "$status" "Idempotent favorite creation"
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$household_id/recipe-favorites")"
assert_status 200 "$status" "Favorite list"
node -e 'const ids=JSON.parse(process.argv[1]); if(ids.length!==1||ids[0]!==process.argv[2])process.exit(1)' "$(cat "$response_body")" "$recipe_id" \
  || fail "Favorite was not listed exactly once."
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$other_household_id/recipe-favorites")"
assert_status 404 "$status" "Cross-household favorite list"

status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header "If-Match: $etag" --request PUT --data "$recipe_json" \
  "$base_url/api/v1/households/$household_id/recipes/$recipe_id")"
assert_status 400 "$status" "Recipe update without CSRF token"
status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header 'X-KF-CSRF: invalid-token' --header "If-Match: $etag" --request PUT \
  --data "$recipe_json" "$base_url/api/v1/households/$household_id/recipes/$recipe_id")"
assert_status 400 "$status" "Recipe update with invalid CSRF token"
status="$(request --cookie "$cookie_jar" --request DELETE \
  "$base_url/api/v1/households/$household_id/recipes/$recipe_id")"
assert_status 400 "$status" "Recipe archive without CSRF token"
status="$(request --cookie "$cookie_jar" --header 'X-KF-CSRF: invalid-token' \
  --request DELETE "$base_url/api/v1/households/$household_id/recipes/$recipe_id")"
assert_status 400 "$status" "Recipe archive with invalid CSRF token"

status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$household_id/recipes/$recipe_id")"
assert_status 200 "$status" "Recipe detail"
node -e 'const r=JSON.parse(process.argv[1]); if(r.ingredients[0].amount!==250||r.ingredients[1].amount!==null||r.steps.length!==2)process.exit(1)' "$(cat "$response_body")" \
  || fail "Recipe amounts, null quantity, or steps did not persist."
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$household_id/recipes?q=Nudeln")"
assert_status 200 "$status" "Recipe search by ingredient"
node -e 'if(JSON.parse(process.argv[1]).length!==1)process.exit(1)' "$(cat "$response_body")" \
  || fail "Ingredient search did not find the recipe."

zero_amount_json="$(node -e 'const r=JSON.parse(process.argv[1]);r.ingredients[0].amount=0;process.stdout.write(JSON.stringify(r))' "$recipe_json")"
status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: $csrf_token" --data "$zero_amount_json" \
  "$base_url/api/v1/households/$household_id/recipes")"
assert_status 400 "$status" "Zero ingredient amount"
invalid_servings_json="$(node -e 'const r=JSON.parse(process.argv[1]);r.baseServings=0;process.stdout.write(JSON.stringify(r))' "$recipe_json")"
status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: $csrf_token" --data "$invalid_servings_json" \
  "$base_url/api/v1/households/$household_id/recipes")"
assert_status 400 "$status" "Non-positive recipe servings"
precision_amount_json="$(node -e 'const r=JSON.parse(process.argv[1]);r.ingredients[0].amount=1.0001;process.stdout.write(JSON.stringify(r))' "$recipe_json")"
status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: $csrf_token" --data "$precision_amount_json" \
  "$base_url/api/v1/households/$household_id/recipes")"
assert_status 400 "$status" "Ingredient amount precision"

invalid_unit_json="$(node -e 'const r=JSON.parse(process.argv[1]);r.ingredients[0].unitCode="unknown";process.stdout.write(JSON.stringify(r))' "$recipe_json")"
status="$(request --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: $csrf_token" --data "$invalid_unit_json" \
  "$base_url/api/v1/households/$household_id/recipes")"
assert_status 400 "$status" "Unknown unit"

updated_json="$(node -e 'const r=JSON.parse(process.argv[1]);r.description="Updated by request A";process.stdout.write(JSON.stringify(r))' "$recipe_json")"
other_update_json="$(node -e 'const r=JSON.parse(process.argv[1]);r.description="Updated by request B";process.stdout.write(JSON.stringify(r))' "$recipe_json")"
curl --silent --show-error --output "$concurrent_body_a" --dump-header "$concurrent_headers_a" \
  --write-out '%{http_code}' --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: $csrf_token" --header "If-Match: $etag" --request PUT \
  --data "$updated_json" "$base_url/api/v1/households/$household_id/recipes/$recipe_id" >"$concurrent_status_a" &
request_a_pid=$!
curl --silent --show-error --output "$concurrent_body_b" --dump-header "$concurrent_headers_b" \
  --write-out '%{http_code}' --cookie "$cookie_jar" --header 'Content-Type: application/json' \
  --header "X-KF-CSRF: $csrf_token" --header "If-Match: $etag" --request PUT \
  --data "$other_update_json" "$base_url/api/v1/households/$household_id/recipes/$recipe_id" >"$concurrent_status_b" &
request_b_pid=$!
wait "$request_a_pid"
wait "$request_b_pid"
status_a="$(cat "$concurrent_status_a")"
status_b="$(cat "$concurrent_status_b")"
if [[ "$status_a" == 200 && "$status_b" == 412 ]]; then
  updated_etag="$(grep -i '^ETag:' "$concurrent_headers_a" | tr -d '\r' | cut -d' ' -f2-)"
elif [[ "$status_b" == 200 && "$status_a" == 412 ]]; then
  updated_etag="$(grep -i '^ETag:' "$concurrent_headers_b" | tr -d '\r' | cut -d' ' -f2-)"
else
  fail "Concurrent updates expected one HTTP 200 and one HTTP 412 but got $status_a and $status_b."
fi

compose up --detach --force-recreate api
ready=false
for _ in {1..30}; do
  if curl --fail --silent --output /dev/null "$base_url/api/health/ready"; then
    ready=true
    break
  fi
  sleep 2
done
[[ "$ready" == true ]] || fail "API did not restart against the persisted database."
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$household_id/recipes/$recipe_id")"
assert_status 200 "$status" "Recipe persistence after API restart"
node -e 'if(!["Updated by request A","Updated by request B"].includes(JSON.parse(process.argv[1]).description))process.exit(1)' "$(cat "$response_body")" \
  || fail "The updated recipe did not persist across restart."
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$household_id/recipe-favorites")"
assert_status 200 "$status" "Favorite persistence after API restart"
node -e 'const ids=JSON.parse(process.argv[1]); if(ids.length!==1||ids[0]!==process.argv[2])process.exit(1)' "$(cat "$response_body")" "$recipe_id" \
  || fail "Household favorite did not persist across API restart."

status="$(request --cookie "$cookie_jar" --request DELETE \
  "$base_url/api/v1/households/$household_id/recipes/$recipe_id/favorite")"
assert_status 400 "$status" "Favorite removal without CSRF token"
status="$(request --cookie "$cookie_jar" --header 'X-KF-CSRF: invalid-token' --request DELETE \
  "$base_url/api/v1/households/$household_id/recipes/$recipe_id/favorite")"
assert_status 400 "$status" "Favorite removal with invalid CSRF token"
status="$(request --cookie "$cookie_jar" --header "X-KF-CSRF: $csrf_token" --request DELETE \
  "$base_url/api/v1/households/$household_id/recipes/$recipe_id/favorite")"
assert_status 204 "$status" "Favorite removal"
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$household_id/recipe-favorites")"
assert_status 200 "$status" "Empty favorite list after removal"
node -e 'if(JSON.parse(process.argv[1]).length!==0)process.exit(1)' "$(cat "$response_body")" \
  || fail "Favorite remained after removal."

status="$(request --cookie "$cookie_jar" --header "X-KF-CSRF: $csrf_token" \
  --header "If-Match: $updated_etag" --request DELETE \
  "$base_url/api/v1/households/$household_id/recipes/$recipe_id")"
assert_status 204 "$status" "Recipe archive"
status="$(request --cookie "$cookie_jar" "$base_url/api/v1/households/$household_id/recipes?includeArchived=true")"
assert_status 200 "$status" "Archived recipe list"
node -e 'if(JSON.parse(process.argv[1]).length!==1||!JSON.parse(process.argv[1])[0].isArchived)process.exit(1)' "$(cat "$response_body")" \
  || fail "Archived recipe was not represented as archived."

echo "P3 recipe integration checks passed."
