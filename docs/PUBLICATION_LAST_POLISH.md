# Publication polish — November 24, 2026

> **Historical record.** This note documents an earlier revision; see the [documentation index](README.md) for the current reference.

This is a source-quality maintenance pass on the cross-platform, 30-document VirtualBank reference candidate, **not a claim of a validated production release**.

## Changes made

- All six Windows PowerShell launchers/maintenance scripts now reside in `scripts/windows/`; repository-root `.ps1` files were removed, references and the local startup invoker were updated. Start from any current directory where permitted; scripts resolve the repository root internally.
- CI validates PowerShell syntax for **every** Windows script rather than a hand-picked list.
- The EF Core CI gate (Customer, Credit and Documents contexts) now checks `has-pending-model-changes` **and generates idempotent migration SQL**, with no execution against an existing database.
- Windows and Unix source verification scripts run the corresponding EF SQL-generation check.
- `tools/publication_guard.py` now checks static security regressions: `svc-ingestion` audience, Sophie HR clearance, independent application document policy and its actual use, and Windows script organization. The snapshot guard supports both handwritten typed entities and standard EF-generated string entity mappings.
- Added `docs/EF_SNAPSHOT_VERIFICATION.md`, the safe, review-driven process for replacing hand-written snapshots with verified EF CLI snapshots without destroying data.

## Verified in this authoring environment

- `python tools/publication_guard.py` — PASS.
- `python tools/evaluate_rag.py --dry-run` — PASS for 42 labeled fixture cases; **not** live vector quality.
- JSON and YAML syntax for relevant configs; Bash syntax for Unix starters — PASS.
- ZIP integrity — must be checked when packaged.

## Not verified yet

- `dotnet restore`, build, xUnit or EF drift / EF migration SQL CLI calls (no .NET SDK/NuGet access in this authoring runtime).
- Windows PowerShell parser on actual Windows (a GitHub Actions job is defined).
- Fresh PostgreSQL migrations, Keycloak JWT claims, RLS, Semantic Kernel tool calling, OpenRouter/Ollama, document ingestion, pgvector retrieval, Bruno or Playwright.

## Do not publish the demo as validated until

1. Push to a **private** GitHub repository and require the CI jobs to pass on the exact commit.
2. On a clean disposable local environment run the Windows/Unix starter, then verify real Keycloak + database authorization, tool-call safety and no leaked document chunks.
3. Run labeled multilingual RAG evaluation, capture **real** screenshots, then update the README with observed results.
4. If EF model drift fails, follow `docs/EF_SNAPSHOT_VERIFICATION.md`. **Never delete a volume, edit migration history or bypass drift checks merely to turn CI green.**
