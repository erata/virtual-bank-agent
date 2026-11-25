# GitHub / Job Application Release Gate

This page is the evidence-oriented release checklist for VirtualBank. **Source code existing is not proof that a runtime scenario passed.** We deliberately do not use a green CI badge or “production-ready” label until the current commit's workflow and live demonstrations pass.

## Current source corrections in this release

| Concern | Remediation in source | Verification still required |
| --- | --- | --- |
| EF Core snapshots delegated to the live model | Three snapshot classes now contain frozen mappings rather than `DbContext.ConfigureModel()` calls | `dotnet ef migrations has-pending-model-changes` and `dotnet ef migrations script --idempotent` for each DbContext; see [snapshot verification playbook](EF_SNAPSHOT_VERIFICATION.md) |
| Inconsistent README / architecture description | Root README and `docs/architecture.md` rewritten for v4; older milestone notes labelled historical | Editorial review against real UI and code |
| No public-release guard for secrets/backups | `.gitignore`, `.dockerignore`, and stdlib `tools/publication_guard.py` | CI execution plus a pre-push scan on a real git repo |
| No reproducible multilingual retrieval score | 42 labeled multilingual retrieval/ACL scenarios with independent manifest oracle and `tools/evaluate_rag.py` added; local/Unix cross-platform starters and new 30-document corpus shipped | Actual PostgreSQL + embedding provider + 30 indexed docs, Hit@5/MRR@5 and ZERO unauthorized/forbidden hits |
| Limited illustrations / publication materials | 4 editable DOT diagrams + SVG renders in `docs/assets`; technical articles are published separately (not part of this repository) | Editorial review and user-produced real screenshots |
| Live integration uncertain | Explicit Windows check script, updated CI model-drift check and release matrix | Successful actual .NET/Compose/Keycloak/Bruno/Playwright runs |

## Mandatory launch checklist

1. **Source and security:** verify `.env` is ignored and has never been committed; check `backups/`, `.env.local`, access tokens and console recordings. Run `python tools/publication_guard.py`.
2. **Code:** run `dotnet restore VirtualBank.slnx`, `dotnet build VirtualBank.slnx -c Release`, and `dotnet test VirtualBank.slnx -c Release`. Do not publish if they fail.
3. **Migrations:** install `dotnet-ef` 10.0.4, run `dotnet ef migrations has-pending-model-changes` and generate idempotent migration SQL for each context; test fresh migration twice, verify indexes, constraints and `FORCE ROW LEVEL SECURITY` on the real database.
4. **Identity and authorization:** in a **disposable** Compose environment run `docker compose up -d --build --wait`, then `scripts/test/keycloak-claims.sh` and `scripts/test/db-tests.sh`; test two distinct `bank-customer` subjects. No administrative role should bypass business-data policy.
5. **HTTP and browser:** run `scripts/test/api-smoke.sh`, Bruno and Playwright against the **real API**, not a stand-in.
6. **Agent:** configure a real OpenRouter key, request three paraphrased balance questions in TR/EN/DE, inspect `toolCalls` and deny side effects (`approve_credit_application`). Never trust `mode openrouter` alone.
7. **RAG:** ingest documents with a machine token, confirm `docs.documents`/`docs.chunks` populated, then run `python tools/evaluate_rag.py --min-hit-rate 0.7`. No forbidden document should appear; expect real cross-lingual Hit@K metrics.
8. **Documentation:** add actual UI screenshots, update observed results, ensure no live token, API key or privileged banking content appears. Clearly mark synthetic fixtures and the local-only trust model.

## Windows PowerShell quick path

```powershell
# No Makefile or WSL required for basic local source verification.
.\scripts\windows\VERIFY_PUBLICATION_WINDOWS.ps1
# Real stack after configuring .env; do not reset existing volumes without backup.
docker compose up -d --build --wait
docker compose ps
docker compose logs --tail=100 db-migrate
```

Optional cross-lingual RAG evaluation (after successful ingestion):

```powershell
.\scripts\windows\INDEX_DOCUMENTS_WINDOWS.ps1
python .\tools\evaluate_rag.py --dry-run
python .\tools\evaluate_rag.py --api http://localhost:8081 --keycloak http://localhost:8080 --min-hit-rate 0.70
```

If an old SQL-created DB volume exists, the EF migrator intentionally refuses to adopt it automatically. Use a separately reviewed migration or backup and an explicit disposable-data reset. **Never run `docker compose down -v` on irreplaceable data.**

## What not to claim on GitHub or in a CV

- Do not say “CI 100% green” without a real workflow run for this commit.
- Do not claim “fully secure against prompt injection”: authorization constrains blast radius, not model behavior.
- Do not claim validated TR/DE/EN search quality until labeled retrieval scores are measured.
- Do not claim all queries use EF LINQ: runtime business repositories still use Npgsql with trusted RLS context.
- Do not call the example bank a production bank or expose local demo credentials to the internet.

## Suggested public-release milestones

**0.5 — Working engineering prototype:** CI build and tests green; disposable local Compose completes; technical limitations visible.

**0.8 — Demonstrable security-aware agent:** actual GPT-4o tool calling and real per-user Keycloak/RLS demos, plus indexed document search.

**1.0 — Publication reference:** reproducible screenshots, measured RAG evaluation, robust migration drift checks, versioned docs, clean repository scan and CV/demo narrative.

The labels are proposed release targets, **not current achievement claims**.
