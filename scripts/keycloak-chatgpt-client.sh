#!/usr/bin/env bash
set -euo pipefail

# Pflege des Keycloak-Clients "chatgpt-actions" im laufenden Küchenfuchs-Stack (Heimserver oder lokal).
# Trägt ausschließlich ausdrücklich angegebene, exakte ChatGPT-Callback-URIs ein (keine Wildcards).
#
#   scripts/keycloak-chatgpt-client.sh show
#   scripts/keycloak-chatgpt-client.sh set-redirect-uris <uri> [<uri> ...]
#   scripts/keycloak-chatgpt-client.sh pkce required|optional
#   scripts/keycloak-chatgpt-client.sh rotate-secret
#
# Umgebung: KEYCLOAK_ADMIN_USERNAME, KEYCLOAK_ADMIN_PASSWORD (sonst interaktive Abfrage),
# KF_COMPOSE_FILES (Standard: Heimserver-Kombination), optional KF_COMPOSE_PROJECT.
# KF_ALLOW_TEST_REDIRECT_URIS=1 erlaubt https://localhost:<port>/... nur für automatisierte Tests.

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root_dir"
read -r -a compose_files <<< "${KF_COMPOSE_FILES:--f compose.yaml -f compose.keycloak.yaml -f compose.homeserver.yaml}"
project_args=()
[[ -n "${KF_COMPOSE_PROJECT:-}" ]] && project_args=(--project-name "$KF_COMPOSE_PROJECT")
realm="kuechenfuchs"
client_id="chatgpt-actions"
config_file="/tmp/kcadm-chatgpt-$$.config"

usage() {
  sed -n '4,13p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
  exit 2
}

compose() {
  docker compose "${compose_files[@]}" "${project_args[@]}" "$@"
}

kcadm() {
  compose exec -T keycloak /opt/keycloak/bin/kcadm.sh "$@" --config "$config_file"
}

cleanup() {
  compose exec -T keycloak rm -f "$config_file" >/dev/null 2>&1 || true
}

login() {
  local username="${KEYCLOAK_ADMIN_USERNAME:-}"
  local password="${KEYCLOAK_ADMIN_PASSWORD:-}"
  if [[ -z "$username" ]]; then read -r -p "Keycloak-Admin-Benutzer: " username; fi
  if [[ -z "$password" ]]; then read -r -s -p "Keycloak-Admin-Passwort: " password; echo; fi
  trap cleanup EXIT
  kcadm config credentials --server http://localhost:8080/auth --realm master \
    --user "$username" --password "$password" >/dev/null
}

client_uuid() {
  local id
  id="$(kcadm get clients -r "$realm" -q "clientId=$client_id" --fields id --format csv --noquotes | tr -d '\r')"
  [[ "$id" =~ ^[0-9a-f-]{36}$ ]] || { echo "Client $client_id nicht gefunden." >&2; exit 1; }
  printf '%s' "$id"
}

valid_redirect_uri() {
  local uri="$1"
  # Von OpenAI dokumentierte Callback-Formen mit konkreter GPT-ID (Stand 11.10.2026).
  [[ "$uri" =~ ^https://(chat\.openai\.com|chatgpt\.com)/aip/g-[A-Za-z0-9]+/oauth/callback$ ]] && return 0
  if [[ "${KF_ALLOW_TEST_REDIRECT_URIS:-}" == "1" ]]; then
    [[ "$uri" =~ ^https://localhost:[0-9]{2,5}/[A-Za-z0-9/_-]+$ ]] && return 0
  fi
  return 1
}

command="${1:-}"
[[ -n "$command" ]] || usage
shift

case "$command" in
  show)
    login
    kcadm get "clients/$(client_uuid)" -r "$realm" \
      --fields clientId,enabled,publicClient,clientAuthenticatorType,standardFlowEnabled,implicitFlowEnabled,directAccessGrantsEnabled,redirectUris,attributes
    ;;
  set-redirect-uris)
    [[ $# -ge 1 ]] || usage
    json="["
    for uri in "$@"; do
      if ! valid_redirect_uri "$uri"; then
        echo "Abgelehnt (keine exakte ChatGPT-Callback-URI): $uri" >&2
        exit 1
      fi
      json+="\"$uri\","
    done
    json="${json%,}]"
    login
    kcadm update "clients/$(client_uuid)" -r "$realm" -s "redirectUris=$json"
    echo "Redirect-URIs für $client_id gesetzt: $*"
    ;;
  pkce)
    case "${1:-}" in
      required) method="S256" ;;
      optional) method="" ;;
      *) usage ;;
    esac
    login
    kcadm update "clients/$(client_uuid)" -r "$realm" -s "attributes.\"pkce.code.challenge.method\"=$method"
    echo "PKCE für $client_id: ${1}"
    ;;
  rotate-secret)
    login
    secret="$(openssl rand -base64 48 | tr -d '\n/+=')"
    kcadm update "clients/$(client_uuid)" -r "$realm" -s "secret=$secret"
    echo "Neues Client-Secret (nur jetzt sichtbar; im GPT-Editor eintragen, nirgends sonst speichern):"
    echo "$secret"
    ;;
  *)
    usage
    ;;
esac
