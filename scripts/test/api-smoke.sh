#!/usr/bin/env bash
# Fast post-start smoke check of the running Compose stack (curl + jq). The full API contract
# lives in the Bruno collection (make test-api); this script only proves the stack is wired up.
set -euo pipefail
KC="${KEYCLOAK_URL:-http://localhost:8080}"
API="${API_URL:-http://localhost:8081}"
UI="${UI_URL:-http://localhost:4200}"
PASSWORD="VirtualBank-Dev-2026!"   # development-only persona password
REALM="${KEYCLOAK_REALM:-virtual-bank}"                      # demo tenant realm
CLI_CLIENT="${KEYCLOAK_CLI_CLIENT:-virtual-bank-cli}"  # development-only password-grant client
TOKEN_URL="$KC/realms/$REALM/protocol/openid-connect/token"

user_token() { curl -fsS -d grant_type=password -d client_id=$CLI_CLIENT --data-urlencode "username=$1" --data-urlencode "password=$PASSWORD" "$TOKEN_URL" | jq -er .access_token; }
status() { curl -s -o /dev/null -w '%{http_code}' ${2:+-H "Authorization: Bearer $2"} "$1"; }
expect() { local name="$1" want="$2" got="$3"; [[ "$got" == "$want" ]] && echo "PASS $name" || { echo "FAIL $name: expected $want, got $got"; exit 1; }; }

expect API_READY          200 "$(status "$API/health/ready")"
expect UI_SERVED          200 "$(status "$UI/")"

anna="$(user_token anna.berater)"
reporting="$(curl -fsS -d grant_type=client_credentials -d client_id=svc-reporting -d client_secret=svc-reporting-dev-only-secret "$TOKEN_URL" | jq -er .access_token)"

expect ANNA_OWN_BRANCH        200 "$(status "$API/api/customers/C-1002" "$anna")"
expect ANNA_OTHER_BRANCH      404 "$(status "$API/api/customers/C-5001" "$anna")"
expect ANNA_NO_CREDIT_ACCESS  403 "$(status "$API/api/credit/applications/K-100" "$anna")"
expect WRONG_AUDIENCE         401 "$(status "$API/api/customers/C-1002" "$reporting")"
expect NO_TOKEN               401 "$(status "$API/api/customers/C-1002")"
expect UI_PROXIES_API         404 "$(status "$UI/api/customers/C-5001" "$anna")"
# Assistant (offline runtime by default): customers answered, credit part denied with a deterministic notice.
notices="$(curl -fsS -H "Authorization: Bearer $anna" -H 'Content-Type: application/json' \
  -d '{"message":"show my customers and credit applications"}' "$API/api/assistant/chat" | jq -c .notices)"
expect ASSISTANT_PARTIAL_NOTICE '["CREDIT_READ_REQUIRED"]' "$notices"
echo "Smoke checks passed."
