#!/usr/bin/env bash
# Verifies that the running Keycloak issues the claims the authorization core expects
# (subject IDs, realm roles, full-path groups, attributes, audiences). Requires curl and jq.
#
#   KEYCLOAK_URL=http://localhost:8080 scripts/test/keycloak-claims.sh
set -euo pipefail
KC="${KEYCLOAK_URL:-http://localhost:8080}"
REALM="${KEYCLOAK_REALM:-virtual-bank}"                      # demo tenant realm
CLI_CLIENT="${KEYCLOAK_CLI_CLIENT:-virtual-bank-cli}"  # development-only password-grant client
TOKEN_URL="$KC/realms/$REALM/protocol/openid-connect/token"
PASSWORD="VirtualBank-Dev-2026!"   # development-only persona password

decode() { cut -d. -f2 | tr '_-' '/+' | awk '{ l=length($0)%4; if (l==2) $0=$0"=="; else if (l==3) $0=$0"="; print }' | base64 -d 2>/dev/null; }
user_claims() {
  curl -fsS -d grant_type=password -d client_id=$CLI_CLIENT -d "username=$1" --data-urlencode "password=$PASSWORD" "$TOKEN_URL" \
    | jq -r .access_token | decode
}
service_claims() {
  curl -fsS -d grant_type=client_credentials -d "client_id=$1" -d "client_secret=$2" "$TOKEN_URL" | jq -r .access_token | decode
}
check() { # name, json, jq filter that must evaluate to true
  if [[ "$(jq -r "$3" <<<"$2")" == "true" ]]; then echo "PASS $1"; else echo "FAIL $1"; jq . <<<"$2"; exit 1; fi
}

anna="$(user_claims anna.berater)"
check ANNA_SUBJECT_IS_STABLE "$anna" '.sub == "00000000-0000-4000-8000-000000000004"'
check ANNA_ROLE "$anna" '.realm_access.roles | index("customer-advisor") != null'
check ANNA_FULL_PATH_GROUP "$anna" '.groups == ["/Customer/Stuttgart"]'
check ANNA_NOT_A_MACHINE "$anna" '.principal_type == null'
check ANNA_BRANCH_ATTRIBUTE "$anna" '.branch == "STR" and .employment_status == "active"'
check ANNA_AUDIENCE "$anna" '(.aud | if type == "array" then . else [.] end) | index("virtual-bank-api") != null'
check ANNA_LIFETIME_5_MIN "$anna" '(.exp - .iat) == 300'

max="$(user_claims max.approver)"
check MAX_APPROVAL_LIMIT "$max" '.approval_limit == "50000"'

mehmet="$(user_claims mehmet.kredit)"
check MEHMET_NO_BRANCH "$mehmet" '.branch == null and .clearance == "confidential"'

svc="$(service_claims svc-reporting svc-reporting-dev-only-secret)"
check SERVICE_ACCOUNT_SUBJECT "$svc" '.sub == "00000000-0000-4000-8000-000000000009" and .preferred_username == "service-account-svc-reporting"'
check SERVICE_ACCOUNT_MACHINE_TYPE "$svc" '.principal_type == "machine"'
check SERVICE_ACCOUNT_AUTHORIZED_PARTY "$svc" '.azp == "svc-reporting"'
check SERVICE_ACCOUNT_ACTIVE "$svc" '.employment_status == "active" and (.realm_access.roles | index("svc-reporting") != null)'
ingest="$(service_claims svc-ingestion svc-ingestion-dev-only-secret)"
check INGESTION_MACHINE_TYPE "$ingest" '.principal_type == "machine" and .azp == "svc-ingestion"'
check INGESTION_API_AUDIENCE "$ingest" '(.aud | if type == "array" then . else [.] end) | index("virtual-bank-api") != null'
check SERVICE_ACCOUNT_NO_BUSINESS_AUDIENCE "$svc" '(.aud // []) | if type == "array" then (index("virtual-bank-api") == null) else (. != "virtual-bank-api") end'

for username in deniz.customer thomas.customer meryem.customer lucas.customer elif.customer emilia.customer; do
  customer="$(user_claims "$username")"
  check "${username}_IS_BANK_CUSTOMER" "$customer" '.realm_access.roles | index("bank-customer") != null'
  check "${username}_PUBLIC_CLEARANCE" "$customer" '.clearance == "public"' 
done

if curl -fsS -d grant_type=password -d client_id=virtual-bank-ui -d username=anna.berater --data-urlencode "password=$PASSWORD" "$TOKEN_URL" >/dev/null 2>&1; then
  echo "FAIL UI_CLIENT_REJECTS_PASSWORD_GRANT"; exit 1
fi
echo "PASS UI_CLIENT_REJECTS_PASSWORD_GRANT"
echo "All Keycloak claim checks passed."
