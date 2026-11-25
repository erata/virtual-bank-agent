#!/usr/bin/env bash
# Runs the PostgreSQL RLS regression suite.
#
#   scripts/test/db-tests.sh              against an initialized server (PGHOST, PGPORT, PGUSER, PGPASSWORD = superuser)
#   scripts/test/db-tests.sh --ephemeral  creates a throwaway local cluster with initdb, applies infra/postgres/init, runs, deletes it
#
# The ephemeral mode needs PostgreSQL 16 server binaries (initdb, pg_ctl) on PATH or in /usr/lib/postgresql/16/bin.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
INIT_DIR="$ROOT/infra/postgres/init"
TESTS="$ROOT/tests/database/rls-tests.sql"

if [[ "${1:-}" == "--ephemeral" ]]; then
  echo "EF Core migrations require pgvector and dotnet: use docker compose up -d --build --wait postgres db-migrate, then run db-tests.sh." >&2
  exit 2
fi
: "${PGHOST:?PGHOST required}" "${PGUSER:?PGUSER required}"
export PGDATABASE="${PGDATABASE:-postgres}"
TCP_HOST="$PGHOST"

echo "== RLS policy tests"
psql -v ON_ERROR_STOP=1 -f "$TESTS" 2>&1 | grep -E "PASS|FAIL|ERROR|passed" | sed 's/^psql:[^:]*:[0-9]*: //'

echo "== Database isolation tests"
expect_connect_denied() {
  local role="$1" password="$2" database="$3" name="$4"
  if PGPASSWORD="$password" psql -h "$TCP_HOST" -p "$PGPORT" -U "$role" -d "$database" -c "select 1" >/dev/null 2>&1; then
    echo "FAIL $name : $role could connect to $database"; exit 1
  fi
  echo "NOTICE:  PASS $name"
}
expect_connect_allowed() {
  local role="$1" password="$2" database="$3" name="$4"
  if ! PGPASSWORD="$password" psql -h "$TCP_HOST" -p "$PGPORT" -U "$role" -d "$database" -c "select 1" >/dev/null 2>&1; then
    echo "FAIL $name : $role could not connect to $database"; exit 1
  fi
  echo "NOTICE:  PASS $name"
}
expect_connect_allowed customer_api customer-api-dev-only customer_db CUSTOMER_ROLE_CONNECTS_OWN_DB
expect_connect_allowed credit_api   credit-api-dev-only   credit_db   CREDIT_ROLE_CONNECTS_OWN_DB
expect_connect_denied  credit_api   credit-api-dev-only   customer_db WRONG_ROLE_CANNOT_CONNECT_CUSTOMER_DB
expect_connect_denied  customer_api customer-api-dev-only credit_db   WRONG_ROLE_CANNOT_CONNECT_CREDIT_DB

echo "All database tests passed."
