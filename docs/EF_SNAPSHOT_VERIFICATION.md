# EF Core migration snapshots — verify before a public release

The three `*DbContextModelSnapshot.cs` files were originally hand-written and triggered `PendingModelChangesWarning` at runtime (they lacked EF conventions such as NOT NULL for non-nullable reference types and FK indexes). They have since been **regenerated with the real EF Core 10.0.4 CLI**; the scaffolded diff only contained nullability and FK-index entries that the `Initial*Schema` migrations already create, so no new migration was needed and `has-pending-model-changes` is clean for all three contexts. Compiling or passing static `publication_guard.py` alone is still not proof of a production-verified migration.

## Read-only verification on a machine with .NET 10 and NuGet access

From the repository root, run:

```powershell
dotnet restore .\VirtualBank.slnx
dotnet build .\VirtualBank.slnx -c Release
dotnet tool install --global dotnet-ef --version 10.0.4
.\scripts\windows\VERIFY_PUBLICATION_WINDOWS.ps1
```

On Linux or macOS:

```bash
dotnet tool install --global dotnet-ef --version 10.0.4
bash scripts/unix/verify-publication.sh
```

The release gate runs **all three** `dotnet ef migrations has-pending-model-changes` checks and generates **idempotent SQL scripts** with EF tooling. No database is modified by these checks. CI runs the same checks in `.github/workflows/tests.yml`.

### If a drift check fails

1. **Do not mark an old migration as applied, change the history table, or delete volumes.** Record the context and exact EF exception.
2. In a new branch, use the real EF tool to scaffold a carefully named migration for the affected context (e.g. `dotnet ef migrations add AuditCustomerSchema --context CustomerDbContext --project src/VirtualBank.Infrastructure --startup-project src/VirtualBank.Infrastructure`).
3. Inspect its `Up` and `Down` thoroughly. Even with generated snapshots, unexpected `CreateTable`, `DropTable`, type/default changes or permission differences are **blockers**, not instructions to run it.
4. Compare the generated EF snapshot to the original model and database schema. Make the existing historical snapshot match the actual baseline **without rewriting the data model or bypassing RLS**. Do not publish speculative, unreviewed DDL.
5. Run a fresh disposable PostgreSQL/pgvector instance, apply all migrations twice, verify foreign keys, indexes, unique/check constraints, restricted DB roles and `FORCE ROW LEVEL SECURITY`, then rerun drift checks.
6. Commit only the reviewed, reproducible migration files and their generated snapshots.

Changing migration history to obtain a green CI result without confirming real SQL schema compatibility is unsafe.

## Separate security regression evidence

`tools/publication_guard.py` also checks that the Keycloak `svc-ingestion` client has the `virtual-bank-api` audience, Sophie has strictly confidential clearance, and Application `DocumentReadPolicy` exists and is invoked after RLS retrieval. **These are static assertions**; real Keycloak claims and PostgreSQL tests are still required.
