> **Historical milestone / patch note.** This file documents an earlier revision and may include obsolete defaults or expected test counts. For the CURRENT reference release, see [`README`](../README.md), [`architecture`](architecture.md), [`quality gate`](PUBLICATION_QUALITY_GATE.md) and [`validation status`](VALIDATION_STATUS.md).

# NL / RAG / Assistant Tables patch — 2026-11-23

Baseline: `virtual-bank-agent-m2-m3-semantic-kernel-security-updated.zip` with `Microsoft.SemanticKernel` 1.71.0.

## Why the user saw a failure

`ASSISTANT_PROVIDER=Offline` routes requests through a deterministic keyword matcher, **not pgvector**. The original list renderer concatenated raw `id=...` fields into one long paragraph. Both are appropriate fixes to the tool/UX layer; rebuilding embeddings alone would not address the typo or presentation.

## New implementation

- `Application/Assistant/NaturalLanguageIntent.cs`: diacritic/case normalization and bounded TR/DE/EN read-intent detection. Never decides authorization.
- `Infrastructure/Assistant/Offline/OfflineAssistantRuntime.cs`: Turkish keyboard variants, localized summaries and quoted document excerpts. No fabricated document answers when no results.
- `Application/Assistant/Tools/AssistantDataTable.cs` and `ToolSession`: data-only, whitelisted, authorized read-use-case tables (`list_customers`, `list_credit_applications`, `list_customer_accounts`). Sensitive masked fields are not projected. Up to 100 rows displayed per result.
- `Presentation/Assistant/AssistantContracts.cs`, Angular domain/gateway/page/styles: typed `tables`, defensive parsing and actual HTML tables (without `innerHTML`), improved readable layout and explicit offline indication.
- `Application/Documents/SecureDocumentSearch.cs`, `DocumentSearchQualityOptions.cs` and infrastructure config: configurable minimum cosine similarity (default 0.45; **must be tuned against labelled queries**).
- `Application/Assistant/Commands/AskAssistant/AskAssistantHandler.cs`: fail-closed response when a policy/document question has no authorized citations; no ungrounded bank-record narrative if no read tool returned data. Deterministic codes `ASSISTANT_NO_DOCUMENT_EVIDENCE` and `ASSISTANT_NO_VERIFIED_DATA`.
- Regression tests: ASCII/Unicode Turkish variants, table projections, PDF/document grounding checks, credit write restriction, browser table rendering, and score filtering.

## Practical smoke test (Windows PowerShell)

Keep the existing `.env` file and existing named Docker volumes, if you have a running environment. Do **not** execute `docker compose down -v`.

```powershell
dotnet restore .\VirtualBank.slnx
dotnet build .\VirtualBank.slnx -c Release
dotnet test .\VirtualBank.slnx -c Release
docker compose up -d --build --wait
```

Angular UI: http://localhost:4200, Anna demo user:

- `müşterilerimi göster`
- `musterilerimi goster`
- `müsterilerimi göster`
- `basvurulari goster` (advisor: `CREDIT_READ_REQUIRED`)
- `Zeig mir meine Kunden` (German summary + table)

All first three should produce the **same three authorized customer IDs** and an actual table with four columns. The exact authorization results depend on the seeded realm, token and database state.

For **real semantic RAG**:

```powershell
docker compose --profile ai up -d ollama
docker compose --profile ai exec ollama ollama pull bge-m3
# Then run the repo's ingestion script via WSL/Git Bash, or call /api/documents/ingest with a valid svc-ingestion token.
```

For **real conversational tool calling**, also pull `qwen2.5:3b`, set `ASSISTANT_PROVIDER=Ollama`, `ASSISTANT_MODEL=qwen2.5:3b`, `ASSISTANT_ENDPOINT=http://ollama:11434/v1/` in `.env`, then `docker compose up -d --force-recreate api`.

## Honest validation scope

Authoring runtime does **not** have .NET SDK, Docker, PostgreSQL, Ollama nor external package-registry access. The source was inspected and static checks were run (ZIP integrity, XML, standard JSON, YAML, TypeScript parser). An actual `dotnet build`, .NET xUnit run, Angular compiler/build, live Keycloak, database and model integration **were not run here**. The user's machine can perform these checks. A similarity threshold and prompt guard **do not guarantee no hallucination**; evaluate negative examples and citation-to-answer support with a labelled multilingual dataset before calling the agent production-safe.
