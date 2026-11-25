> **Historical milestone / patch note.** This file documents an earlier revision and may include obsolete defaults or expected test counts. For the CURRENT reference release, see [`README`](../README.md), [`architecture`](architecture.md), [`quality gate`](PUBLICATION_QUALITY_GATE.md) and [`validation status`](VALIDATION_STATUS.md).

# Code review log — M1 API layer

Two review rounds on 2026-11-21. Status words follow the README: *verified* means executed here.

## Round 1 (external review, archive "m1-reviewed")

Added Customer and Credit read APIs, shared JWT/RLS plumbing, compose services, a Bruno collection,
a curl smoke script and a static source checker. Nothing was compiled or executed in that environment.
Its observation that the earlier test counts were unverified was correct for that environment; the
counts were re-executed in round 2 and still hold (89 unit cases, 38 database checks).

## Round 2 findings and fixes

| # | Finding | Severity | Fix |
| --- | --- | --- | --- |
| 1 | The APIs never called the authorization policies. Security relied on RLS alone, so a broken or missing RLS policy would have leaked rows; invariant I2 (independent API enforcement) was not met. | High | Every row returned under RLS is re-authorized with `VirtualBank.Authorization` policies. Rows that RLS returns but the policy denies are dropped and logged as `PolicyRlsMismatch`. Covered by handler tests with a deliberately unfiltered store. |
| 2 | `SecuritySetup.cs` would not compile: `IServiceCollection`, `IConfiguration`, `HttpContext` were used without `using` directives (implicit usings of `Microsoft.NET.Sdk` do not cover ASP.NET namespaces). | High | Replaced by `JwtAuthentication`, `ServiceCollectionExtensions`, `HeliosPrincipal` (since round 4: `PrincipalContextFilter`) with explicit usings. All projects compile with warnings as errors (against package stubs, see below). |
| 3 | The backchannel handler rewrote every `localhost:8080` request to `keycloak:8080`. Running an API on the host (not in Docker) would break JWKS retrieval. | Medium | `BackchannelRewriteHandler` is configured explicitly (`Keycloak:PublicBaseUrl` → `Keycloak:InternalBaseUrl`) and only in Compose; matching is on scheme, host and port. Unit-tested. |
| 4 | Token algorithms were not restricted. | Medium | `ValidAlgorithms = RS256`; `IncludeErrorDetails = false`. |
| 5 | Account balances were masked although `customer.read-sensitive` is defined as contact data, IBAN and date of birth. Advisors could not use the accounts endpoint. | Medium | Balances are returned to anyone allowed to read the customer; sensitive fields follow the catalog definition and `CustomerSensitiveReadPolicy`. |
| 6 | `GET /customers/{id}/accounts` answered `200 []` for hidden customers, unlike the detail endpoint. | Low | Answers 404 unless the customer is readable; the accounts query is not even executed. |
| 7 | No input validation on IDs. | Low | `C-dddd` / `K-ddd` patterns; malformed IDs answer 404 without a database call. |
| 8 | `POST /credit/applications/{id}/notes` (M1 scope) was missing. | Medium | Implemented: permission check, policy on the loaded application, 1–4000 characters, insert re-checked by RLS `WITH CHECK`, `201 Created`. |
| 9 | Inactive or unresolvable principals were rejected with ad-hoc `Results.Forbid()` in every handler. | Low | One `PrincipalContextFilter` per route group; problem-details responses; deterministic `noticeCode` for credit denials. |
| 10 | No readiness endpoint or container health checks for the APIs; `make up` could return before the APIs worked. | Medium | `/health/ready` checks the database; Compose health checks (bash probe, because `/bin/sh` in Debian images has no `/dev/tcp`); `make up` uses `--build --wait`. |
| 11 | Compose hard-coded the issuer port 8080 although `KEYCLOAK_PORT` is configurable. | Low | Issuer and public base URL derive from `KEYCLOAK_PORT`. |
| 12 | The service image ran as root and re-restored packages on every source change. | Low | `USER $APP_UID`; restore layer before source copy. |
| 13 | Bruno collection needed manually pasted tokens, asserted status codes only and had no wrong-audience test. | Medium | Self-contained collection: `01-auth` obtains tokens, 22 security/contract requests with body assertions, a genuine wrong-audience test (service token without `customer-api` audience) and a check that the approval endpoint is not exposed. |
| 14 | `static-review.py` asserted exact source strings. It gave false confidence and would fail on harmless refactors. | Low | Removed; replaced by real compilation, handler tests, SQL execution and Bruno runs. |
| 15 | `test-static` was missing from `.PHONY`; the API test target was not in CI. | Low | Makefile rewritten; CI runs smoke and Bruno after `docker compose up --build --wait`. |

Kept from round 1: separate API processes, transaction-local RLS context, `127.0.0.1` port bindings,
credit approval **not** exposed over HTTP until the M6 human-confirmation workflow exists.

## What was executed in round 2

| Check | Result |
| --- | --- |
| RLS suite on PostgreSQL 16 (34 policy + 4 isolation checks) | 38/38 pass |
| Authorization core and matrix tests | 89/89 pass |
| API handler tests (new) | 28/28 pass |
| Store SQL executed under persona RLS context on PostgreSQL 16 (lists, detail, accounts, note insert incl. RLS rejections) | 10/10 pass |
| Compilation of all projects, warnings as errors | pass |
| Bruno collection with Bruno CLI 4.3.0 against a contract mock | 27/27 requests, 34/34 tests pass; a deliberate cross-branch leak in the mock fails 3 tests |
| `docker compose config`, workflow YAML, shell syntax | valid |
| Mutation: API handler ignores the policy decision | detected by 4 handler tests |

Limits of this evidence: NuGet, Docker Hub and the Keycloak distribution are unreachable here.
Unit tests ran against a minimal stand-in of the xUnit API; the APIs were compiled against
compile-only stubs of `JwtBearer`, `IdentityModel` and `NpgsqlDataSource.Create`. Package API
compatibility, the real HTTP pipeline, Keycloak import and the Bruno run against the real stack are
verified only once the CI workflow runs.

## Round 3 (external review of the round-2 archive)

The review found no new defects in the API code and confirmed the round-2 fixes. Its open points:

| Point | Decision |
| --- | --- |
| `credit_api` held `UPDATE` on applications although no HTTP endpoint changes them | **Fixed.** New role `credit_approval` (NOLOGIN until the M6 approval executor) is the only role with `UPDATE`; `credit_api` reads applications and inserts notes only. New test `CREDIT_API_ROLE_CANNOT_UPDATE`; approval scenarios run under `credit_approval`. 39/39 database checks pass. |
| `/health/ready` checked only the database | **Fixed.** Readiness also fetches the Keycloak discovery document over the same backchannel path as token validation (3 s timeout) and answers 503 naming only the failing dependency. |
| `make down` deleted the database volume | **Fixed.** `make down` keeps data; `make reset` deletes the volume. |
| Assignment projection has no freshness guarantee | Already a documented M1 limitation (README, threat model, ADR-0005); synchronization is M4 scope. |
| Audit goes to logs only | Accepted for M1; a persistent, access-restricted audit store is planned with the Operations API (M6, `audit.read`). |
| Real NuGet build, Keycloak import, live API and Bruno runs not yet executed | Agreed: this is the M1 exit criterion. It requires running the CI workflow or `make up && make test-stack` on a machine with registry access. |

Re-executed after the round-3 changes: database 39/39, unit 89/89, handlers 28/28, store SQL 10/10,
compilation of all projects without warnings.

## Round 4 — product rename (no behavior change)

Product renamed to **VirtualBank** (repository `virtual-bank-agent`, namespaces and projects
`VirtualBank.*`, solution `VirtualBank.slnx`). Class and method names no longer carry a product
prefix (`PrincipalContextFilter`, `RequirePrincipalContext()`, `GetPrincipalContext()`,
`AddResourceServerSecurity()`, `AddKeycloakJwtAuthentication()`, `MapHealthEndpoints()`). Audit log
category `VirtualBank.Audit`; test override variable `VIRTUAL_BANK_REPO_ROOT`.
Unchanged on purpose: the demo company Helios Finanz AG and its environment — Keycloak realm `helios`,
client IDs, persona e-mails and password, database roles.

Re-executed after the rename: database 39/39, unit 89/89, handlers 33/33, store SQL 10/10, compilation of
all projects without warnings, seed check, Compose configuration, Bruno collection against the contract mock 27/27.

## Round 5 — naming, ownership and architecture (M1.5)

| Point | Decision |
| --- | --- |
| "Helios" in code and configuration | Kept only as the fictional demo company and Keycloak realm `helios`. Client IDs renamed to `virtual-bank-ui` and `virtual-bank-cli`; realm and CLI client are configurable (`Keycloak:*` options, `KEYCLOAK_REALM` / `KEYCLOAK_CLI_CLIENT` in scripts, `realm` / `cliClientId` in Bruno). |
| One database owner role for both databases | **Fixed.** `customer_owner` owns `customer_db`, `credit_owner` owns `credit_db`. New check `CREDIT_OWNER_SUBJECT_TO_FORCED_RLS`. |
| Credit API not Clean Architecture | **Changed now, not after M6.** `Credit.Domain` / `.Application` (CQRS, no MediatR) / `.Infrastructure` / `.Api`. Approval rules live in the domain; the approval endpoint stays closed until M6 (architecture test). |
| Customer API | Stays a vertical slice; owns `CustomerPolicies`. |
| Shared authorization library contained business policies | **Fixed.** Core keeps mechanisms (`CapabilityPolicies`, generic `NoticeCatalog`, `IAuthorizationAudit` port). Each API registers only its own policies; cross-context requests answer `NO_POLICY` (tested in both services). New `VirtualBank.Data` holds the RLS session. |
| Dependency rules were not checked | **Added** `tests/architecture` (14 tests, declared and compiled references). Mutation-checked. |

Re-executed after round 5: database 40/40, store SQL 10/10, Bruno collection against the contract mock
27 requests / 34 tests, .NET tests 171/171 (core 38, ApiSecurity 9, Customer 17, Credit 41, matrix 52,
architecture 14), compilation of every project with warnings as errors, Compose configuration.
Still pending: the real CI run (NuGet, containers, Keycloak import, live Bruno).

## Round 6 — M1.5 review and persistence decision (M1.5.1)

| Point | Decision |
| --- | --- |
| Capability rules (HR, operations, documents, audit, platform) still in the shared core | **Fixed.** Core ships only `PermissionPolicy`; an architecture test forbids concrete rule sets there. The modules do not exist yet, so their 19 matrix cases are `status: planned`: checked against design policies and **proven denied with `NO_POLICY`** by every running service (no false enforcement). |
| `Rehydrate` accepted Approved without `approvedAt`, version ≤ 0, zero amount | **Fixed** in the domain and as CHECK constraints (`APPROVED_REQUIRES_TIMESTAMP`, `VERSION_MUST_BE_POSITIVE`, `CREDIT_AMOUNT_MUST_BE_POSITIVE`). `Money` may be zero; a credit application amount may not. |
| `CreditApplicationId` was a validator, not a value object | **Fixed.** Sealed record (no invalid default), `TryParse`/`Parse`, used through domain, port and repository. |
| Service accounts detected by user-name prefix | **Fixed.** Hardcoded `principal_type=machine` claim on client-credentials-only clients; resolver uses only the claim. Prefix without claim, unknown type or missing `azp` → inactive. Realm test: the claim exists only on clients that cannot log in humans. |
| Customer not per-feature | **Fixed.** `Features/{ListCustomers,GetCustomer,ListCustomerAccounts}` + `Shared/`, `CustomerModule` composition root. |
| Approval-route test searched source text | **Replaced** by route tests on the registered endpoints of the real composition roots (exact allowlist, auth + principal filter on every business route, only health anonymous). Mutation: a hidden `PUT …/decision` route via `MapMethods` fails the test; the old text search would have missed it. |
| Weak readiness | **Fixed.** Issuer must match, `jwks_uri` must lie under the issuer (or its internal backchannel twin), and an RSA RS256 signing key must exist. Compose realm name now configurable (`KEYCLOAK_REALM`). |
| EF Core Code First | **Accepted as M1.6** (ADR-0007) with the four conditions: migrator identities (owners are NOLOGIN), one explicit transaction for RLS context + EF, migrations as the single schema source (old scripts removed, DB suite against the migrated DB), planned-case handling (done). Not implemented here: no NuGet access in the authoring environment. |

Re-executed after round 6: database 43/43, store SQL 10/10, .NET tests 218/218 (core 45, ApiSecurity 17,
Customer 17, Credit 49, matrix 72, architecture 18), compilation with warnings as errors, seed check,
Compose configuration, Bruno against the contract mock 27/34. Still pending: the real CI run.

## Round 7 — M1.5.1 review (M1.5.2)

| Point | Decision |
| --- | --- |
| `principal_type=machine` accepted with any `azp` | **Fixed.** `MachineIdentityOptions` from `Authorization:MachineClients` (DI/configuration); the claim counts only for an allowlisted client. Tests: trusted clients accepted; UI, CLI, agent, unknown and wrong-case clients fail closed; missing `azp` fails closed; empty allowlist ⇒ no machines; allowlist equals the realm's machine clients in every service. Mutation (any `azp` accepted) fails 6 tests. |
| Blank `approvedBy` accepted | **Fixed** in `Rehydrate`, in `Approve` and as CHECK constraints (`BLANK_APPROVER_REJECTED`). |
| Undefined status enum accepted | **Fixed** with `Enum.IsDefined`; DB counterpart `UNKNOWN_STATUS_REJECTED` (existing `status IN (…)`). |
| JWKS readiness | Kept as is. Further proof via real-token 200/401 tests in CI and M1.6, not more parsing. |
| ADR-0007 test requirements | Added: readiness fails while FORCE RLS is off; ownership of migrated objects verified. |

Re-executed: database 45/45, store SQL 10/10, .NET tests 231/231 (core 54, ApiSecurity 17, Customer 17,
Credit 53, matrix 72, architecture 18), Bruno against the contract mock 27/34. Still pending: the real CI run.

## Round 8 — owner decision: one layered API, Virtual Bank

Not a review finding but a deliberate change by the project owner, recorded in [ADR-0009](adr/0009-layered-modular-monolith-api.md):

- Backend restructured into `VirtualBank.Model`, `.Application`, `.Infrastructure`, `.Presentation`, `.Web`; Customers and
  Credit are modules (folders) in every layer. One API host, one audience `virtual-bank-api`; two databases and two
  least-privilege DB logins unchanged.
- Customer queries moved from HTTP-returning slice handlers to transport-neutral use cases (`UseCaseResult<T>`), like Credit.
- `IPrincipalResolver` is now an Application port implemented in Infrastructure; Presentation no longer knows the resolver.
- Architecture tests rewritten: layer rules, module-boundary rules (compiled signatures and usings), ports implemented
  only in Infrastructure, route allowlist for the single host. Mutation-checked (Customers → Credit reference;
  Presentation → Infrastructure reference).
- Demo company renamed to Virtual Bank (realm `virtual-bank`, persona password `VirtualBank-Dev-2026!`).

Re-executed: .NET tests 231/231 (Model 30, Application 63, Infrastructure 44, Presentation 4, matrix 72, architecture 18),
database 45/45, repository SQL 10/10, real Keycloak 26.4.7 import + 15 claim checks, Bruno 27/34 with real Keycloak tokens
against an API stand-in, UI 42 unit + 8 layer rules + 11 browser tests through nginx and the real Keycloak login.

## Round 9 — owner decision: M2 assistant (tool calling) from the earlier RBAC-Agent / RAG-Lab

Recorded in [ADR-0011](adr/0011-assistant-module-and-tool-authorization.md). The lab's Semantic Kernel + OpenRouter
tool calling is integrated as an `Assistant` module of the layered API:

- `assistant.use` (advisor, analyst, approver; humans only) as a capability policy with 9 new matrix cases.
- Seven tools over the existing use cases; `ToolPolicyGate` with call budget, permission, risk tier, write budget
  and argument validation; approval listed but never executed (M6); partial success with deterministic notices.
- `IAssistantRuntime` with a Semantic Kernel adapter (OpenRouter `gpt-4o-mini`, temperature 0, loop filter) and a
  deterministic offline runtime (default, EN/DE/TR keywords).
- `POST /api/assistant/chat`, Angular assistant page, Bruno folder `04-assistant`, browser tests.
- Architecture: peer modules may not reference the assistant; the assistant reaches modules only through
  Application use cases (no repository ports).

Re-executed: .NET tests 308/308 (Model 30, Application 106, Infrastructure 65, Presentation 6, matrix 81,
architecture 20), gate mutation checks (3 of 3 detected), database 45/45, repository SQL 10/10, real Keycloak import
+ 15 claim checks, UI 47 unit + 9 layer rules + 13 browser tests through nginx and the real Keycloak login (API
stand-in). Not executed: Semantic Kernel package restore and `SemanticKernelRuntimeTests`, OpenRouter calls,
the assistant Bruno requests against the real API — all CI.
