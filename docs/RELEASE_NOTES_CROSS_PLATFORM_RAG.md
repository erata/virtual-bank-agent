# VirtualBank v4 — Cross-platform GitHub starter & extended RAG fixtures

> **Historical record.** This note documents an earlier revision; see the [documentation index](README.md) for the current reference.

Release preparation: **2026-11-24**. This note records **source changes**, not certification that every integration check passed.

## Changes

- Windows launcher `scripts/windows/start-local.ps1` retained and hardened with an Ollama-readiness check.
- New Linux/macOS Bash launcher `scripts/unix/start-local.sh` and source verification `scripts/unix/verify-publication.sh` added. They use the same Compose/EF migrator/schema and don't require `make` or cloud keys for the local starter.
- Added CI jobs to parse/check Bash on Ubuntu/macOS and PowerShell on Windows without downloading AI models. Live Docker/browser/LLM E2E still requires the separate real integration suite.
- README rebuilt as a reference-project landing page with project motivation, capabilities, architecture, security, OS setup, provider switching, users, troubleshooting, benchmarks, roadmap and 12 bilingual articles.
- RAG fixture corpus extended from **15 to 30** documents, with more realistic searchable multi-page PDFs, version-confusable 2023/2024 versus 2026 policies, public SEPA/FAQ, HR and operations documents.
- Added reproducible optional synthetic corpus generator `tools/generate_extended_corpus.py` (ReportLab and python-docx needed **only** to regenerate fixtures, not to run VirtualBank).
- Retrieval evaluation extended from 14 to **42 labeled questions** (14 TR, 14 DE, 14 EN) and an **independent seed-ACL oracle** for every returned document. Prints Hit@5, MRR@5, known-ID coverage, temporal Top-1 and unauthorized/forbidden hits.
- `bank-customer` now has `document.read` with explicitly `public` clearance (0) in the six demo Keycloak identities. Read-only document search is available for customer fees and FAQ, not internal/Credit/HR policy. Application, Keycloak claims, policy matrix and PostgreSQL RLS checks updated together.
- Assistant capability suggestions for bank customers now advertise public fees/FAQ instead of confidential credit policies.

## Upgrade instructions from earlier local demo

1. **Keep your real `.env`** and existing port overrides; read the updated `.env.example` for new defaults. Never commit keys.
2. The code changes to `policies/permissions.json` take effect when the API uses the updated catalog. However, **customer clearance is also a Keycloak token claim**; you need to import/update the new realm for your demo environment. Recreating the demo Keycloak service can reload the mounted realm JSON when it uses ephemeral Keycloak storage; **a persistent external Keycloak database requires a controlled realm update**. Verify the six customer users' `clearance=public` claims.
3. No EF schema migration is required merely to add corpus documents, but the 15 new docs must be ingested. Re-run `scripts/windows/INDEX_DOCUMENTS_WINDOWS.ps1` on Windows or `scripts/ai-index.sh` on Linux/macOS after starting the correct embedding provider.
4. If you change from `bge-m3` to a cloud embedding model, **re-ingest** the corpus with the new provider/model. Same 1024 dimensions do not make old vectors valid.
5. In a genuinely clean demo DB (Code First v4), ingestion should result in 30 documents and more than zero chunks. Verify directly in the real PostgreSQL service; do not take this source-level statement as proof.
6. Run C# tests, real Keycloak claims and RLS checks, live 42-question retrieval evaluation, real LLM tool calling and browser/Bruno tests before calling the GitHub release verified.

**Do not use** `docker compose down -v` without a verified backup. It removes named volumes and any persisted demo data. `docker compose --profile ai down` keeps volumes.

## Known limitations

This source revision has no executed evidence of a full real `.NET build`, EF pending-model-drift check, Keycloak import, pgvector ingestion, Apple Silicon ARM64 Docker image support or Ollama/OpenRouter tool calling. Syntactic/static checks and PDF/DOCX rendering succeeded; the live tests remain required. The original project limitations (HITL, audit retention, durable authorization-checked conversation history and complete folder ACL inheritance) remain unchanged.
