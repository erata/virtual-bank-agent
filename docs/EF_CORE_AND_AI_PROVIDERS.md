# VirtualBank v4 — Real provider switching, EF Core Code First and multicurrency demos

> **Validation scope:** These changes were authored without a .NET SDK or Docker in the authoring environment. JSON/XML/YAML and repository structure can be checked here; only your .NET 10/Docker environment can compile, migrate and run real Keycloak/AI/pgvector E2E. Do not describe this release as fully verified before that run.

## 1. Chat provider vs embedding provider

A **chat model** (`openai/gpt-4o`) reasons over natural language and selects authorized tools. An **embedding model** (`openai/text-embedding-3-small`) encodes text to 1024-dimensional numeric vectors. GPT-4o does **not** output embeddings; a separate embedding-capable model is needed even when both use the **same OpenRouter account/API key**.

| Mode | Chat config | Embedding config | Key | Additional local model download? |
|---|---|---|---|---|
| OpenRouter (opt-in) | `OpenRouter` / `openai/gpt-4o` / `https://openrouter.ai/api/v1` | `OpenRouter` / `openai/text-embedding-3-small` / `https://openrouter.ai/api/v1/` | `OPENROUTER_API_KEY` | No |
| Direct OpenAI | `OpenAI` / `gpt-4o` / `https://api.openai.com/v1` | `OpenAI` / `text-embedding-3-small` / `https://api.openai.com/v1/` | `OPENAI_API_KEY` | No |
| **Local Ollama (default)** | `Ollama` / `qwen3:4b-instruct` / `http://ollama:11434/v1/` | `Ollama` / `bge-m3` / `http://ollama:11434/` | none | Yes, `--profile ai` + `ollama pull` |
| Offline (test-only) | `Offline` (keyword router) | independently configured | none for chat only | No (chat), RAG still needs embedding service |

All provider names/endpoints/keys are configured through `.env`; `compose.yaml` maps them to .NET `Assistant__*` and `Documents__*` options. Changing `ASSISTANT_PROVIDER` to `OpenRouter` does not auto-convert embeddings from `Ollama`, because they are intentionally separately configured. Both defaults are Ollama; OpenRouter and OpenAI are explicit alternatives.

**Private keys:** keep `.env` local, not checked in. Only synthetic fixtures may be sent to cloud providers. Do not print secrets in logs. Missing selected cloud key fails startup rather than silently switching to Offline.

**Embedding dimension contract:** Qwen3-Embedding 0.6B, BGE-M3, and `text-embedding-3-small` with `dimensions=1024` return 1024-dimensional vectors. They are NOT in the same embedding space. The stored `embedding_model` includes provider, model, and dimensions, so retrieval only compares vectors from the matching space. Switching embedding providers requires re-running ingestion with the new provider to replace all document chunks; previous document-level ACLs remain unchanged. Similarity threshold `0.45` is a configurable starting point, not a validated universal cutoff. Test relevance on TR/DE/EN golden queries. Existing ACL/RLS checks remain authoritative.

## 2. EF Core Code First schema

- `src/VirtualBank.Infrastructure/Persistence/EntityFramework/Entities.cs`: persistence entities; domain remains EF-free.
- `.../Contexts.cs`: 3 DbContexts and Fluent mapping for tables, relationships, `CHECK`s, unique/multi-column indexes, `numeric(14,2)` amounts, 3-character currency codes, `vector(1024)` and HNSW cosine index.
- `.../Migrations/*.cs`: initial entity-driven schema migrations and PostgreSQL-specific SQL for `FORCE ROW LEVEL SECURITY`, policies, helper functions, and least-privilege grants. Later **SQL-only security migrations** (no model change) add: `SecureDocumentsVectorUsage` (pgvector operator access for the reader role), `SecureCreditDecisions` / `SecureCreditDecidedVisibility` (rejection, decision notes), `SecureCreditCustomerRequests` (customer credit applications, numbering), `SecureCustomerSelfServiceWrites`, `SecureCustomerBanking` (bookings, no overdraft, cash desk, standing orders) `SecureCustomerInternalPayments` (credit to another customer of the bank) and `SecureOwnerPaymentPolicyScope` (limits the owner policies of that function to the function call, so FORCE RLS still hides every row from the table owner without context, I14). The migrator applies them automatically.
- `tools/VirtualBank.DbMigrator`: **one-shot** schema migrator using dedicated `*_migrator` logins and `SET ROLE` to NOLOGIN owner. API runtime credentials cannot perform DDL.
- `infra/postgres/init/00-roles-and-databases.sql`: only database/role provisioning and privileged pgvector `CREATE EXTENSION`. SQL table and seed sources moved to `infra/postgres/legacy/` for comparison; they are NOT executed.
- `infra/seed/fixtures.json` + `infra/seed/financial-fixtures.json`: canonical deterministic EF seed inputs; Keycloak synthetic subject mappings read from the canonical realm JSON.

**Migration sequencing:** first create Customer and Credit relational schema via EF; insert synthetic fixtures via EF while the database is not exposed; apply security migrations with `FORCE RLS` and grants; migrate Documents, including its RLS policy. `api` waits for a successful `db-migrate` job; API never sees an unprotected schema. A legacy SQL-created schema is *detected and rejected*, never silently overwritten or stamped as migrated.

**Future migrations:** run `dotnet ef migrations add ... --context CustomerDbContext --project src/VirtualBank.Infrastructure --startup-project tools/VirtualBank.DbMigrator` using the committed `IDesignTimeDbContextFactory` (which never opens a database or invokes Web/Keycloak/OpenRouter); the snapshots are frozen, independent model representations; validate them with real `dotnet ef migrations has-pending-model-changes` before accepting new changes. Review RLS policies in every table migration. EF cannot express PostgreSQL RLS or cluster roles as ordinary relationships, so native SQL remains indispensable in migrations.

**Important implementation limit:** existing runtime Customer/Credit/Document repositories still use optimized Npgsql SQL under transaction-local RLS; the change here replaces **DDL and seeding** with EF Core Code First, not every runtime SQL read with LINQ. This avoids accidentally bypassing the tested RLS context.

## 3. Fresh installation on Windows PowerShell

```powershell
Copy-Item .env.example .env
notepad .env
# The local starter requires no provider API key. Run the opt-in installer below.
dotnet restore .\VirtualBank.slnx
dotnet build .\VirtualBank.slnx -c Release
dotnet test .\VirtualBank.slnx -c Release
.\scripts\windows\start-local.ps1
docker compose ps
docker compose logs db-migrate --tail=100
```

Use `http://localhost:4200`. `db-migrate` is expected to show **Exited (0)** after successfully completing; it is a one-shot job, not a long-running server. Keycloak and API must be healthy.

### Existing pre-v4 PostgreSQL volume: NO automatic deletion

If you installed an older SQL-schema VirtualBank before v4, `db-migrate` fails closed with `Legacy SQL schema detected`. This is intentional: blindly running `InitialCreate` on populated tables or marking an incompatible schema as migrated could corrupt data or security policy.

For a **synthetic demo only**, `scripts/windows/RESET_TO_EFCORE_WINDOWS.ps1 -ConfirmReset` first exports a `pg_dumpall` SQL backup to `backups/` and then deletes only this Compose project's PostgreSQL named volume. It never deletes Ollama or other projects' volumes. **Old data is not automatically restored.** Review the script, the backup file and the exact volume name before confirming. For a real database use a separately reviewed data migration, not this reset script. `scripts/windows/UPGRADE_EXISTING_WINDOWS.ps1` intentionally refuses unsafe upgrades.

## 4. Multicurrency seed and tests

- 12 customers; 32 accounts including `EUR`, `USD`, `TRY`; Deniz, Thomas, Meryem, Lucas, Elif and Emilia are individual Keycloak bank-customer personas.
- Multi-currency query results are **grouped by currency**, never added together without a trusted FX rate/source. They report *account balances*, not cash holdings or net worth.
- Payments, transactions and standing orders also demonstrate different currencies and relative dates.
- Advisor access remains branch-limited and bank customer self-service bound to the verified Keycloak `sub`, with Application authorization and PostgreSQL RLS.

Test prompts: **“Jana'nın EUR, USD ve TRY hesaplarını ayrı göster”**, **“Deniz'in toplam bakiyesi hangi para birimlerinde?”**, **“Meine Daueraufträge anzeigen”**, **“Son 30 gündeki USD ödemelerim neler?”**, **“C-5003 bilgilerine Anna erişebilir mi?”**. Do not mistake a ChatGPT-generated total for a database-backed tool result.

## 5. Real RAG test (local Ollama default; cloud alternative)

The local starter script pulls `bge-m3` and runs `scripts/windows/INDEX_DOCUMENTS_WINDOWS.ps1` with an actual `svc-ingestion` JWT. Alternatively use `scripts/ai-index.sh` in Bash/WSL. When switched to OpenRouter, the document embeddings use the same OpenRouter key as chat; the plain `docker compose up` command does not start or download Ollama. Check `documents_db.docs.documents` and `docs.chunks` row counts, then ask a policy question with a real authorized persona. No documents = no RAG answer; low relevance must not be invented into a policy citation.

**Security:** Authorization limits the blast radius of prompt injection; it does not prevent it.
