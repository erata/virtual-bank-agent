> **Historical milestone / patch note.** This file documents an earlier revision and may include obsolete defaults or expected test counts. For the CURRENT reference release, see [`README`](../README.md), [`architecture`](architecture.md), [`quality gate`](PUBLICATION_QUALITY_GATE.md) and [`validation status`](VALIDATION_STATUS.md).

# VirtualBank — semantic assistant, account balances, and authorization

## Why the screenshot was wrong

`mode offline` is NOT an LLM: it routed any request containing `customer` to `list_customers`, even
"show my customer total money". This is an intent/substitution bug, not a pgvector retrieval failure.
pgvector retrieves policy *documents*; structured balances must come from authorized Customer account use cases.

## This patch

- Adds the read-only CQRS `SummarizeCustomerBalancesHandler` and model-visible
  `summarize_customer_balances` tool, registered with existing Customer capability permissions.
- The handler first lists only authorized customers (application policy and PostgreSQL RLS), then
  invokes `ListCustomerAccountsHandler` for EACH authorized customer (again both policy and RLS).
- An inaccessible individual lookup causes the aggregate to fail CLOSED. Over 100 customers also
  fails closed instead of returning a misleading incomplete "total".
- Results are grouped by currency. Never mix EUR with USD. Account balances are *not* cash, net worth,
  or total bank holdings; those require separately modeled data.
- Customer names or IDs may be used as selectors in the real LLM. In Offline mode, a customer ID,
  an explicit plural/total request, or a uniquely matched name in the already authorized list is supported.
  Unsupported/unresolved requests ask for clarification. This is NOT arbitrary language understanding.
- `ToolSession` and `AssistantTableProjector` create trusted, authorized structured tables. The
  application overrides any model-written amounts with deterministic `AssistantBalanceFormatter` text.
  A model that only calls `list_customers` cannot produce a fictitious balance or customer table in response
  to a money question; `ASSISTANT_NO_VERIFIED_FINANCIAL_DATA` is returned.
- A customer list response explains that these are *accessible* customers, not customers the user
  owns and not the entire bank customer base.
- Credential words (`Kennwort`, `password`, `şifre`) are synonyms, but there is **no approved password tool**;
  the assistant refuses credential access instead of inventing an action.

## PowerShell, after replacing source files and keeping your existing .env

```powershell
# Check the code before deploying (real .NET SDK required):
dotnet restore .\VirtualBank.slnx
dotnet build .\VirtualBank.slnx -c Release
dotnet test .\VirtualBank.slnx -c Release

# Deploy only rebuilt API and frontend images; preserve existing DB volumes:
docker compose up -d --build --wait
```

### Flexible paraphrases require a real model

In `.env` change the runtime to Ollama (not the pgvector embedding model):

```dotenv
ASSISTANT_PROVIDER=Ollama
ASSISTANT_MODEL=qwen2.5:3b
ASSISTANT_ENDPOINT=http://ollama:11434/v1/
```

```powershell
docker compose --profile ai up -d ollama
docker compose --profile ai exec ollama ollama pull qwen2.5:3b
docker compose up -d --force-recreate api
```

Check the UI trace says `mode ollama` **and** the appropriate tool was called. A configured LLM can
still select the wrong tool; the application refuses unsupported amounts and continues enforcing RBAC,
ABAC and PostgreSQL RLS. No evidence of successful real Ollama/OpenRouter E2E tests is supplied here.

### Manual test prompts

| Persona | Question | Expected behavior |
|---|---|---|
| Anna | `müşterilerimi listele` | Authorized Stuttgart customer table, explicit scope note |
| Anna | `müsterilerimi göster` | Same customer table |
| Anna | `show my customer total money` | `summarize_customer_balances`; per-currency accessible-account balance table, **not** names only |
| Anna | `musterilerimin toplam bakiyeleri` | Same authorized amounts, not full bank balances |
| Anna | `C-1002'nin nakitini göster` | Account-balance summary for only C-1002; clearly NOT proof of cash holdings |
| Anna | `Jana'nın bakiyesi` | Resolve unique authorized name; otherwise request customer ID |
| Anna | `C-5001 balance` | Not found or not visible; no finance rows |
| Anna | `K-100 başvuruları` | Generic deterministic credit denial as before |
| Anna | `Kennwort ändern` | Credential action unsupported, no fabricated password tool |
| Anna | `Hangi kredi onay politikası geçerli?` | RAG search (after real ingestion); grounded citation or no-evidence response |

> This patch does NOT make `mode offline` a semantic AI. The real semantic component is the optional
> Semantic Kernel + tool-capable LLM; pgvector is for **documents**, not database balances.

## Verification boundary

Source was checked statically in this environment (no .NET SDK, no Docker daemon, no Ollama models).
Test source was added, but actual compilation and end-to-end model/pgvector behavior must be
verified on the user's development machine/CI. Never report tests as passing without running them.

### One-click PowerShell verification

Run `scripts/windows/VERIFY_ON_WINDOWS.ps1` in PowerShell from the project root, or
`scripts/windows/VERIFY_ON_WINDOWS.ps1 -SkipDocker` for .NET tests alone.
The script does **not** delete Docker volumes.
