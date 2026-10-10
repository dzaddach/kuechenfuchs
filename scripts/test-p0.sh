#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_name="kuechenfuchs-p0-test-$$"
http_port="${HTTP_PORT:-8080}"
export HTTP_PORT="$http_port"
export POSTGRES_PASSWORD="p0-smoke-test-only"
cd "$root_dir"

compose() {
  docker compose --project-name "$project_name" "$@"
}

cleanup() {
  compose down --volumes --remove-orphans
}

echo "Validating Docker Compose configuration..."
compose config --quiet
trap cleanup EXIT

echo "Building API and Angular/Caddy images..."
compose build

echo "Starting isolated P0 smoke-test stack..."
compose up --detach

echo "Waiting for the application to become ready..."
ready=false
for _ in {1..45}; do
  if curl --fail --silent --output /dev/null "http://127.0.0.1:${http_port}/api/health/ready"; then
    ready=true
    break
  fi
  sleep 2
done

if [[ "$ready" != true ]]; then
  compose ps
  compose logs
  echo "The API did not become ready within 90 seconds." >&2
  exit 1
fi

echo "Waiting for all service healthchecks..."
all_healthy=false
for _ in {1..30}; do
  all_healthy=true
  for service in db api caddy; do
    container_id="$(compose ps --quiet "$service")"
    health_status="$(docker inspect --format '{{.State.Health.Status}}' "$container_id")"
    if [[ "$health_status" != healthy ]]; then
      all_healthy=false
      break
    fi
  done

  if [[ "$all_healthy" == true ]]; then
    break
  fi
  sleep 2
done

if [[ "$all_healthy" != true ]]; then
  compose ps
  compose logs
  for service in db api caddy; do
    container_id="$(compose ps --quiet "$service")"
    docker inspect --format "{{.Name}} health={{.State.Health.Status}} details={{json .State.Health.Log}}" "$container_id"
  done
  echo "One or more P0 services did not become healthy within 60 seconds." >&2
  exit 1
fi

for service_port in "db 5432" "api 8080"; do
  read -r service port <<< "$service_port"
  if host_port="$(compose port "$service" "$port" 2>/dev/null)" && [[ -n "$host_port" ]]; then
    echo "Service $service unexpectedly publishes $host_port to the host." >&2
    exit 1
  fi
done

proxy_port="$(compose port caddy 80)"
if [[ "$proxy_port" != "127.0.0.1:${http_port}" ]]; then
  echo "Expected Caddy to bind only 127.0.0.1:${http_port}, got: $proxy_port" >&2
  exit 1
fi

base_url="http://127.0.0.1:${http_port}"
frontend="$(curl --fail --silent --show-error "${base_url}/")"
grep --quiet --fixed-strings '<title>Küchenfuchs</title>' <<< "$frontend"
grep --quiet --fixed-strings '<app-root' <<< "$frontend"
grep --quiet --fixed-strings 'lang="de"' <<< "$frontend"

bundle_path="$(grep -oE 'src="[^"]+\.js"' <<< "$frontend" | head -1 | cut -d '"' -f 2)"
if [[ -z "$bundle_path" ]]; then
  echo "Angular JavaScript bundle was not referenced by the served index page." >&2
  exit 1
fi

bundle="$(curl --fail --silent --show-error "${base_url}/${bundle_path}")"
grep --quiet --fixed-strings 'Die lokale Entwicklungsumgebung ist bereit.' <<< "$bundle"

live_status="$(curl --fail --silent --show-error "${base_url}/api/health/live")"
grep --quiet --fixed-strings '"status":"ok"' <<< "$live_status"

ready_status="$(curl --fail --silent --show-error "${base_url}/api/health/ready")"
grep --quiet --fixed-strings '"database":"connected"' <<< "$ready_status"

echo "P0 smoke tests passed: frontend, API, PostgreSQL readiness, service health, and host-port isolation."
