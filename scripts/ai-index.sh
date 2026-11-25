#!/usr/bin/env bash
set -euo pipefail
# Ingest the repository-owned document manifest with a REAL Keycloak client-credentials token.
# This script never passes a principal/role in the request body.
cd "$(dirname "$0")/.."
if [[ -f .env ]]; then set -a; source .env; set +a; fi
KC_URL="${KEYCLOAK_PUBLIC_URL:-http://localhost:${KEYCLOAK_PORT:-8080}}"
REALM="${KEYCLOAK_REALM:-virtual-bank}"
SERVICE_ID="${KEYCLOAK_INGESTION_CLIENT:-svc-ingestion}"
SERVICE_SECRET="${KEYCLOAK_INGESTION_SECRET:-svc-ingestion-dev-only-secret}"
API_URL="${VIRTUALBANK_API_URL:-http://localhost:${API_PORT:-8081}}"
token=$(curl --fail --silent --show-error --retry 3 \
  --data-urlencode 'grant_type=client_credentials' \
  --data-urlencode "client_id=$SERVICE_ID" \
  --data-urlencode "client_secret=$SERVICE_SECRET" \
  "$KC_URL/realms/$REALM/protocol/openid-connect/token" | jq -er '.access_token')
curl --fail --silent --show-error -X POST \
  -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
  "$API_URL/api/documents/ingest"
printf '\n'
