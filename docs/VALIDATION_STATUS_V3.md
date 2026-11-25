> **Historical milestone / patch note.** This file documents an earlier revision and may include obsolete defaults or expected test counts. For the CURRENT reference release, see [`README`](../README.md), [`architecture`](architecture.md), [`quality gate`](PUBLICATION_QUALITY_GATE.md) and [`validation status`](VALIDATION_STATUS.md).

# VirtualBank v3 verification status (2026-11-23)

## Implemented, source-reviewed (NOT CI verified)

- `bank-customer` Keycloak identity-bound access, Demo customers Deniz and Thomas, seed accounts and read-only payment records.
- Application-level read-policy checks plus FORCE RLS for customer/payment queries; the two identity bindings cannot be selected by message arguments.
- Self-service tools for IBAN, account/balances, upcoming payments, recent transactions and standing orders.
- Semantic Kernel with real local Ollama as **new default**; `Offline` remains an explicit rule-based testing provider.
- Role-based `GET /api/assistant/capabilities?lang=tr`; Angular suggestions derived from backend capabilities.
- Document-by-ID authorized excerpts, not raw PDF download.
- Non-destructive PostgreSQL upgrade script including `customers_read_own` on old volumes.

## NOT VERIFIED in this coding environment

- `dotnet restore/build/test` (SDK unavailable), real Keycloak import, real Docker compose startup.
- End-to-end Ollama Semantic Kernel function calling and pgvector multilingual retrieval.
- PostgreSQL 16 RLS script execution on a real running database.
- Existing web `.env` on user's Windows machine **must** be changed from Offline to Ollama.

## Acceptance commands in Windows PowerShell

```powershell
dotnet restore .\VirtualBank.slnx
dotnet build .\VirtualBank.slnx -c Release
dotnet test .\VirtualBank.slnx -c Release
docker compose up -d --build
.\scripts\windows\UPGRADE_EXISTING_WINDOWS.ps1 # only existing DB, run after postgres is up
docker compose up -d --force-recreate keycloak
docker compose exec ollama ollama pull qwen2.5:3b
docker compose exec ollama ollama pull bge-m3
docker compose up -d --build --force-recreate api frontend
docker compose ps
```

Then login as `deniz.customer` and `thomas.customer`, verify `GET /api/self/profile` distinguishes the two JWTs, run a 5-day payment query, and inspect the Assistant trace for `mode ollama` / the correct `get_my_...` tools. Search/ingest RAG separately before claiming it works. Avoid `docker compose down -v` unless you intentionally wish to destroy demo data.
