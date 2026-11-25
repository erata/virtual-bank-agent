# ADR-0007: EF Core Code First with PostgreSQL security in migrations

- Status: Implemented for DDL and deterministic demo seeding (runtime Npgsql repositories retained); real integration verification pending.
- Date: 2026-11-22
- Related: ADR-0005 (RLS context), ADR-0006 (Clean Architecture)

## Context

Historical context (M1.5.1): the schema was created by `infra/postgres/init/*.sql` and the services use ADO.NET with SQL
constants. This is defensible for an RLS-centric system: the security-relevant objects (roles, `FORCE ROW LEVEL
SECURITY`, policies, column grants, `set_config` helpers) are plain SQL in any approach, and the RLS suite runs
with psql only. Its weaknesses:

- No schema evolution: changing a table means editing an init script that only runs on an empty volume.
- No model ↔ schema consistency check besides tests in the historical pre-EF release.
- EF Core is the default .NET persistence stack; a reference project should show how it coexists with RLS.

## Decision

**Hybrid: EF Core Code First owns the schema evolution, native SQL inside the same migration chain owns the
security objects.** One source of truth per database; no parallel table definitions.

| Concern | Owner |
| --- | --- |
| Databases, cluster roles, `REVOKE CONNECT` | Bootstrap SQL (`00-roles-and-databases.sql`), cluster level, runs before any migration |
| Tables, columns, keys, FKs, CHECK constraints, indexes | EF Core migrations, one `DbContext` per service (`CustomerDbContext`, `CreditDbContext`), Fluent API in Infrastructure |
| RLS enable/force, policies, grants, helper functions | Dedicated migrations (`AddRowLevelSecurity`, …) using `migrationBuilder.Sql(...)`, reviewed like code |
| Demo data | `tools/VirtualBank.DbMigrator` seeds deterministic fixtures through EF, reading canonical Keycloak subjects |
| Runtime queries | Npgsql with explicitly scoped transactions and RLS context; an EF LINQ repository conversion is not claimed |

The old schema scripts `10-*-schema.sql` / `11-*-schema.sql` are **archived under `infra/postgres/legacy/` and not executed** in the same change that introduces
`InitialCreate`; the bootstrap keeps only database and role creation.

## Conditions (from review round 6)

1. **Least-privilege migrator identities.** `customer_owner` / `credit_owner` stay `NOLOGIN` (they own the schema).
   New LOGIN roles `customer_migrator` / `credit_migrator` are members of the respective owner role and run
   migrations with `SET ROLE <owner>`, so created objects are owned by the owner role. Migrators have no access to
   the other database. **APIs never run migrations**; the runtime roles `customer_api` / `credit_api` keep DML-only grants.
2. **RLS context and EF in one explicit transaction.** A request-scoped unit of work opens the connection, begins
   the transaction, sets `app.*` with `set_config(..., true)` and only then lets EF query or `SaveChanges` inside
   that transaction; commit or rollback ends the context. A `DbConnectionInterceptor` alone is not relied upon.
   Integration tests (Testcontainers, real PostgreSQL) cover: pooled connections do not leak context between
   requests, concurrent requests see only their own rows, `IgnoreQueryFilters()` does not bypass RLS (EF query
   filters are convenience, RLS is enforcement), and a command without context fails closed.
3. **Migrations are the only schema source.** The 45 database checks run against a database built by
   `dotnet ef migrations script --idempotent` (applied by the migrator), not against hand-written DDL. CI adds
   `dotnet ef migrations has-pending-model-changes`. Compose gets a one-shot `migrator` service; the APIs
   `depends_on` it with `service_completed_successfully`, so no API serves traffic between `InitialCreate` and the
   RLS migration.
4. **No false enforcement for planned modules.** Already implemented in M1.5.1: planned matrix cases are marked
   `status: planned` and must be denied with `NO_POLICY` by every running service.

## Mapping rules

- Model stays EF-free. `CustomerDbContext` and `CreditDbContext` with their configurations live in `Infrastructure/Persistence/{Customers,Credit}`.
- `CreditApplicationId` → value converter (`string`), `Money` → complex type / owned mapping to `amount` + `currency`,
  `Version` → concurrency token (`IsConcurrencyToken`), `CreditApplicationStatus` → string conversion matching the
  CHECK constraint values.
- Private setters and the private constructor are mapped through backing fields; `Rehydrate` stays the
  invariant check for non-EF construction and tests.
- Token validation is proven end to end (real Keycloak token → 200, tampered/foreign token → 401) instead of
  extending the JWKS parser checks further.
- Architecture tests are extended: `Microsoft.EntityFrameworkCore` allowed in Infrastructure only.

## Consequences

- Real schema evolution with reviewable migrations; one more service in Compose (migrator).
- RLS remains the enforcement layer; EF does not replace it.
- The historical SQL/ADO.NET version (M1.5.1) served as an earlier baseline; M1.6 is a separate commit series so a
  regression can be attributed either to the integration or to the new persistence.
- Current limitation: migration classes and frozen model snapshots exist, but require real EF CLI build/drift and database validation on a machine with NuGet access before being treated as a verified migration baseline.


## 2026-11-23 implementation notes

See `docs/EF_CORE_AND_AI_PROVIDERS.md`. EF entities and migration classes now own tables/FKs/checks/indexes. Separate security migrations own native PostgreSQL `FORCE RLS`, policies and grants. A one-shot dedicated DbMigrator is wired into Compose before API startup. Runtime ADO.NET repositories remain unchanged pending a separate tested EF data-access transition. Old SQL DDL is archived under `infra/postgres/legacy/` and not executed.
