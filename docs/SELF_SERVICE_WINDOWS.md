> **Historical milestone / patch note.** This file documents an earlier revision and may include obsolete defaults or expected test counts. For the CURRENT reference release, see [`README`](../README.md), [`architecture`](architecture.md), [`quality gate`](PUBLICATION_QUALITY_GATE.md) and [`validation status`](VALIDATION_STATUS.md).

# Windows PowerShell — VirtualBank v3 upgrade and verification

## Two different meanings of "offline"

`ASSISTANT_PROVIDER=Offline` **never calls an LLM**; it is a narrow keyword router only. The v3 default is `Ollama` (a REAL local LLM using Semantic Kernel). Ollama does not require the Internet for inference **after** the model has been downloaded. OpenRouter is a cloud alternative requiring an API key and network connectivity.

## Existing Docker project (data-preserving path)

**Back up real data first.** These example records are synthetic. Do **not** execute `docker compose down -v` just to upgrade.

```powershell
# Use the v3 source but retain your existing .env and API/PG port values.
# The new SQL scripts are mounted into the postgres container by Compose.
docker compose up -d --no-deps postgres
.\scripts\windows\UPGRADE_EXISTING_WINDOWS.ps1
# Keycloak realm import normally happens at first start, so recreate the DEV identity container:
docker compose up -d --force-recreate keycloak
# IMPORTANT: Existing .env files override the new default.
# Edit .env: ASSISTANT_PROVIDER=Ollama, ASSISTANT_MODEL=qwen2.5:3b,
# ASSISTANT_ENDPOINT=http://ollama:11434/v1/
docker compose up -d ollama
docker compose exec ollama ollama pull qwen2.5:3b
docker compose exec ollama ollama pull bge-m3
docker compose up -d --build --force-recreate api frontend
```

If `scripts/windows/UPGRADE_EXISTING_WINDOWS.ps1` is blocked by PowerShell policy, invoke `powershell -ExecutionPolicy Bypass -File .\scripts\windows\UPGRADE_EXISTING_WINDOWS.ps1` from the project root. A pre-existing Keycloak realm in an external database may need explicit user/role migration; this project's dev Keycloak container has no persistent volume configured.

## Fresh setup

Copy `.env.example` to `.env`, preserve host ports required by your machine, then:

```powershell
docker compose up -d --build
# Pull the model BEFORE trying the assistant; the model is not baked into the image.
docker compose exec ollama ollama pull qwen2.5:3b
docker compose exec ollama ollama pull bge-m3
docker compose up -d --force-recreate api frontend
```

## Tests

```powershell
dotnet build .\VirtualBank.slnx -c Release
dotnet test .\VirtualBank.slnx -c Release
# Run the new, real FORCE RLS tests in a disposable test database:
Get-Content -Raw .\tests\database\self-service-rls.sql | docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U postgres -d postgres
# Check backend: adjust API_PORT to your .env
Invoke-RestMethod http://localhost:8081/health/ready
docker compose ps
```

## Live personas (all DEVELOPMENT-ONLY)

| User | Password | Expected behavior |
|---|---|---|
| `deniz.customer` | `VirtualBank-Dev-2026!` | Own IBAN, two balances, own payment history and scheduled payments; **never Thomas** |
| `thomas.customer` | `VirtualBank-Dev-2026!` | Own C-5001 profile and payments; **never Deniz** |
| `anna.berater` | `VirtualBank-Dev-2026!` | Authorized Stuttgart customer records only; **no customer self-service tools** |
| `mehmet.kredit` | `VirtualBank-Dev-2026!` | Assigned credit applications and authorized documents |
| `lena.admin` | `VirtualBank-Dev-2026!` | Capabilities / own permission explanation, **not bank customer data** |

Natural language prompts: `IBAN'ım nedir?`, `Bakiyem kaç?`, `Önümüzdeki 5 günün ödemeleri`, `Son 30 günde yaptığım işlemler`, `Otomatik ödeme talimatlarım`, `Welche Daueraufträge habe ich?`, `Welche Berechtigungen habe ich?`, `DOC-101 dosyasından alıntı göster` (only if document.read is granted).

Check the Assistant trace for `mode ollama` and the correct `get_my_...` tool. The new `GET /api/assistant/capabilities?lang=tr` endpoint returns only authorized prompts and tools. `200 OK` on the API health endpoint does not prove that Ollama has pulled a model or that RAG documents have been indexed. Run a real assistant tool-call and document search before claiming full E2E success.
