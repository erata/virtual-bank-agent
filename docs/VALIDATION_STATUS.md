# Validation status — public reference source revision

## Current status (2026-11-25, Windows 11, .NET 10 SDK, Docker Desktop, OpenRouter)

- **Build and tests:** `dotnet test VirtualBank.slnx` — Model 30, Presentation 6, Application 241, Infrastructure 121,
  Specifications 110, Architecture 20: all green. Angular: 68 unit tests, 10 architecture rules, production build green.
  `tools/publication_guard.py` passes.
- **Exercised end-to-end against the running stack (API + browser):** permission-derived menus in EN/DE/TR; document
  list/viewer per format; ACL administration; credit approve/reject with confirmation cards (RLS re-check under the
  `credit_approval` role); customer transfers (immediate booking, internal credit to another customer, no overdraft),
  transfers between own accounts, standing orders, planned payment cancel, credit applications, branch cash desk; the
  same operations prepared by the assistant and confirmed in the chat; cross-user confirmation attempts return 404; the
  internal-credit function refuses a credit without a matching debit.
- **Fresh install:** a new Compose project with empty volumes migrates (5 migrations per database), seeds valid IBANs,
  indexes 30 documents and passes the banking smoke tests above.
- **RAG gate (OpenRouter `text-embedding-3-small`):** Hit@5 85.7 %, MRR@5 0.767, 0 unauthorized/forbidden hits — PASS
  (gate 70 %). After making the JSON fixtures DOC-114 (tr) and DOC-115 (de) single-language, three cross-lingual cases
  (EN/DE → Turkish HR grades, TR → German runbook) no longer hit; previously 91.4 %.
- **Not executed here:** the Playwright e2e suite (`npm run e2e`), Bruno collection, ARM64 images, load/fault injection.

## Earlier status (2026-11-24, executed on a Windows 11 developer machine with .NET 10 SDK and Docker Desktop)

This section supersedes the "not executed here" notes below; those are kept as the history of the original source drop.

- **Build and tests:** `dotnet test VirtualBank.slnx` — 6 test projects, all green. Before this revision two test projects
  (Application, Architecture) did not compile (xUnit analyzer errors, a missing `using`), which hid 14 failing tests:
  - Real bug: `NaturalLanguageIntent.Normalize` relied on `string.Normalize(FormD)`, a no-op under
    `InvariantGlobalization=true`, so TR/DE intents with diacritics ("müşterilerimi göster") were not recognised in
    production images either. Replaced by an explicit fold table; German compounds ("Kreditrichtlinie") now count as
    document requests.
  - Real bug: `ASSISTANT_PROVIDER=Offline` failed at startup because the unused endpoint was validated.
  - Notice duplication: an explicit credit-tool denial no longer also gets the generic `CREDIT_READ_REQUIRED` hint;
    a denied action answers "not available to you" instead of "no records retrieved".
  - Stale expectations fixed: 8 PDF fixtures (was 3), `lena.admin` may use the assistant per the matrix
    (`ADMIN_ASSISTANT_ALLOW`), four customer personas added to the authorization matrix.
- **EF Core:** snapshots regenerated with the EF Core 10.0.4 CLI; `has-pending-model-changes` clean for all contexts.
- **Docker:** `docker compose build --no-cache` and `scripts/windows/start-local.ps1` complete; migrator exits 0,
  API/Keycloak/Angular healthy, 30 documents / 74 chunks indexed with local `bge-m3`.
- **Local model:** default chat model is now `qwen3:4b-instruct`. The `qwen3:4b` tag resolves to the always-thinking
  2507 variant, which spent the whole token budget on hidden reasoning and returned empty answers.
- **Performance (CPU-only, i7-13700H laptop, Ollama in Docker/WSL):** ~16 prompt tok/s, 3–6 output tok/s; one
  tool-calling answer took ~198 s end to end. Timeouts were raised (API 240 s, nginx 300 s) as a stop-gap, SDK retries
  are disabled for Ollama, and every turn logs `AssistantTurnTiming` (total/model/tool ms, tool count) plus
  `AssistantTokenUsage`. "What can I do?" questions are answered deterministically from the caller's visible tools.
  `start-local.ps1 -NativeOllama` is available to compare native Windows Ollama (not yet measured on this machine).


**Date:** 2026-11-24. **Scope:** static fixes, publication materials, and test/verification tooling added to VirtualBank v4.

## Executed here (no .NET SDK, Docker, PostgreSQL, provider credentials)

- `python tools/publication_guard.py`: validation of snapshots, Git ignore safeguards, seed references, sample-doc files, and 12 bilingual article drafts.
- `python tools/evaluate_rag.py --dry-run`: validation of labeled multilingual retrieval cases against known document IDs.
- JSON/YAML, Python syntax and sample-document sanity checks; diagram generation using Graphviz DOT.
- Rendered and visually reviewed the synthetic PDF and DOCX fixtures (two-page layouts).

## Not executed here — require an actual developer machine or CI

- `dotnet restore`, real compilation, xUnit, and architecture tests with actual packages.
- `dotnet ef migrations has-pending-model-changes`, fresh EF migration and migration re-run.
- PostgreSQL RLS, pgvector query, schema owner/role isolation and seeded ACLs.
- Keycloak realm import, browser login, client claims and machine ingestion tokens.
- OpenRouter/OpenAI/Ollama model-generated tool calls and real embedding responses.
- Bruno, Angular production build and Playwright against the real API.
- Cross-language Hit@5 and forbidden-document checks from real retriever results.

**Important:** The frozen snapshots in this source release were created by copying the current Fluent mapping into independent historical files, not by running the EF CLI generator in this authoring environment. The real EF model-drift check in CI must pass before calling them verified. If EF reports pending differences, regenerate snapshots/migrations with `dotnet ef` and inspect the generated DDL.

`docs/PUBLICATION_QUALITY_GATE.md` contains the exact steps to obtain the missing evidence. A failed command is an actionable issue, never an acceptable “green” label.

## Local-first provider packaging amendment

- Switched starter defaults in `.env.example`, `appsettings.json`, Compose fallback settings and Assistant/RAG options to Ollama (`qwen3:4b`, `bge-m3`).
- Kept both cloud alternatives and the test-only Offline mode. Added `scripts/windows/start-local.ps1`; Compose still uses `profiles: ["ai"]` without an API dependency on Ollama.
- This amendment was statically inspected only. No claims of a successful .NET build, Ollama download, Semantic Kernel call, RAG ingestion or EF migration are made.

## Cross-platform README and corpus expansion (2026-11-24)

- Added `scripts/unix/start-local.sh` for Linux/macOS with native Bash and existing Docker Compose, plus `scripts/unix/verify-publication.sh`; verified Bash syntax, **not live Unix or Apple Silicon execution**.
- Clarified prerequisites, local/cloud provider toggles, offline mode, security boundaries, port conflicts, EF migration and safe shutdown in `README.md` and `docs/LOCAL_SETUP.md`.
- Added 15 synthetic documents (DOC-116..130, 30 total) including five searchable 3-page PDFs, 2023/2024 archived versus 2026 current document pairs, and public SEPA/customer FAQs.
- Updated `bank-customer` role to grant `document.read` with Keycloak clearance `public` to six customer personas; security matrix, unit tests, Keycloak claim checks and PostgreSQL RLS tests now include positive/negative customer document cases.
- Expanded `tests/evaluation/rag_golden.json` to **42** equal-language cases; enhanced the evaluator with an independent seed-ACL oracle, MRR@5 and version Top-1 reporting.
- Executed source JSON/YAML, Bash/ Python syntax, publication-guard, evaluation `--dry-run` and actual PDF/DOCX render/extraction checks in the authoring environment. These are **not substitutes** for `dotnet build`, EF Core drift, real Ollama embeddings, Keycloak, PostgreSQL or running browser tests.
- A fresh Keycloak realm import/volume setup is required for users whose existing realm DB has the old customer clearance/permissions; do not assume editing JSON dynamically changes a persistent realm.

## Final GitHub housekeeping pass

- Consolidated Windows maintenance and validation scripts under `scripts/windows/`; adjusted call sites, canonical setup documentation and CI PowerShell syntax scanning.
- `publication_guard.py` checks Keycloak ingestion audience, Sophie HR clearance and independent Application-level document authorization; **these are static checks, not live tests**.
- Read-only EF CLI drift and idempotent SQL generation checks are configured for CI and both OS preflight scripts; their **execution remains outstanding**. See `docs/EF_SNAPSHOT_VERIFICATION.md`.
