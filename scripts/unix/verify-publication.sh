#!/usr/bin/env bash
# Cross-platform source/EF publication gate; requires .NET 10 SDK, Python 3 and dotnet-ef 10.0.4.
set -euo pipefail
ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$ROOT"
for cmd in dotnet python3; do
  command -v "$cmd" >/dev/null 2>&1 || { echo "Missing $cmd" >&2; exit 1; }
done
python3 tools/publication_guard.py
python3 tools/evaluate_rag.py --dry-run
dotnet restore VirtualBank.slnx
dotnet run --project tools/VirtualBank.SeedGenerator -- --check
dotnet build VirtualBank.slnx -c Release --no-restore
dotnet test VirtualBank.slnx -c Release --no-build
if ! dotnet ef --version >/dev/null 2>&1; then
  echo 'Missing dotnet-ef. Install: dotnet tool install --global dotnet-ef --version 10.0.4' >&2; exit 1
fi
for context in CustomerDbContext CreditDbContext DocumentsDbContext; do
  dotnet ef migrations has-pending-model-changes --context "$context" \
     --project src/VirtualBank.Infrastructure --startup-project src/VirtualBank.Infrastructure \
     --configuration Release --no-build
  tmp_sql=$(mktemp)
  trap 'rm -f "$tmp_sql"' EXIT
  dotnet ef migrations script --idempotent --context "$context" \
     --project src/VirtualBank.Infrastructure --startup-project src/VirtualBank.Infrastructure \
     --configuration Release --no-build --output "$tmp_sql"
  test -s "$tmp_sql"
  rm -f "$tmp_sql"
done
printf '\nPASS source + EF model drift gate; run separate REAL DB/Keycloak/Tool Calling/RAG integration tests.\n'
