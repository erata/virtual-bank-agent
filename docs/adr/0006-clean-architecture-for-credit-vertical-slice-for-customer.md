# ADR-0006: Clean Architecture for Credit, Vertical Slice for Customer, policies per bounded context

> **Updated by [ADR-0014](0014-human-confirmed-changes-and-core-banking-operations.md) (2026-11-25):** the "M6" human-confirmed approval exists. Approval and rejection are *prepared* by route and executed only through the single confirm route.

- Status: Superseded by [ADR-0009](0009-layered-modular-monolith-api.md) (one layered API with Customers and Credit modules)
- Date: 2026-11-22
- Supersedes: the "Credit API in vertical-slice style" part of the M1 code structure

## Context

M1 implemented both APIs as vertical slices on top of a shared authorization library that also
contained the customer and credit resource types, policies and notice codes. Review round 5 raised:

1. The Credit context has real business rules (state machine, four-eyes, approval limit, notes) and
   gets more in M6 (human-confirmed approval). Those rules lived in an authorization policy and in SQL,
   not in a domain model.
2. A "shared" library that knows every business context couples all services: each API registered every
   policy of every other context, and changing a credit rule rebuilt the customer service.
3. Architecture claims ("the domain has no framework dependency") were not checked by anything.

The specification (§1.3) asks for Clean/Hexagonal Architecture where rules are complex, and Vertical
Slice for small CRUD-style endpoints — not four projects for every endpoint.

## Decision

**Credit → Clean Architecture now (M1.5), not after M6.**

| Project | Contains | May depend on |
| --- | --- | --- |
| `VirtualBank.Credit.Domain` | `CreditApplication` aggregate (approval rules, the only transition to *Approved*), `Money`, `CreditNoteText`, `CreditApplicationId` | nothing (BCL only) |
| `VirtualBank.Credit.Application` | CQRS use cases (`Queries/…`, `Commands/…`), port `ICreditApplicationRepository`, credit policies and notice rules, transport-neutral `UseCaseResult<T>` | Domain, authorization core |
| `VirtualBank.Credit.Infrastructure` | `PostgresCreditApplicationRepository` (SQL under RLS via `System.Data.Common`) | Application, `VirtualBank.Data` |
| `VirtualBank.Credit.Api` | Endpoints, HTTP contracts, `UseCaseResult` → HTTP mapping, composition root | all of the above, `ApiSecurity` |

- CQRS **without MediatR**: one handler class per query/command, resolved directly from DI. There is no
  pipeline yet that would justify a mediator, and explicit handlers keep the call graph navigable.
  Reads and writes share one database; CQRS here separates the *models and code paths*, not the stores.
- The approval rules exist in the domain (`CheckApproval`, `Approve`) and are reused by `CreditApprovePolicy`,
  so the rule has one business definition. **No HTTP endpoint reaches approval** until the M6 workflow
  (pending action, explicit confirmation, re-validation, idempotency); an architecture test enforces this.
- The application layer has no `HttpContext`, `TypedResults`, `IResult` or Npgsql. HTTP status codes are
  decided in exactly one place (`UseCaseHttpResults`).

**Customer → stays Vertical Slice.** Store → handler → response in one folder is the right size for
branch/assignment-filtered reads. The customer policies moved into the slice
(`Customers/CustomerPolicies.cs`).

**Shared building blocks hold mechanisms only.**

- `VirtualBank.Authorization`: principal resolution, permission catalog, policy engine, `PermissionPolicy`
  (mechanism), generic `NoticeCatalog`, `IAuthorizationAudit` port. No customer or credit types, and since M1.5.1
  no concrete capability rules either (see "Amendment").
- `VirtualBank.Data`: `AuthorizedDbSession` (transaction-local RLS context), provider-neutral.
- `VirtualBank.ApiSecurity`: JWT, principal filter, readiness, audit logger, `DbDataSource` registration.
- `AddResourceServerSecurity(configuration, audience, policies)` takes the policies of the calling service;
  **each API registers only its own context's policies**. A credit policy asked of the Customer API answers
  `NO_POLICY` (deny), and vice versa — both are tested.

**Agent and RAG (M2/M3)** will follow the hexagonal shape from the start (ports for tools, retriever, model).

## Dependency rules (enforced by `tests/architecture`)

- Credit.Domain: no project or package references; no ASP.NET, EF Core, Npgsql, `Microsoft.Extensions.*`.
- Credit.Application: only Credit.Domain and the authorization core.
- Credit.Infrastructure: no ASP.NET, no Npgsql (the driver is chosen in the composition root), no Api.
- Authorization core and Data: framework-free class libraries, no bounded-context references.
- Bounded contexts never reference each other. The shared core contains no business type.
- Ports live in `Application/Abstractions`, are interfaces, and each has an infrastructure adapter.
- Handlers live under `Queries/` or `Commands/` and handle a `…Query` or `…Command` message.

Rules are checked on the declared references (`.csproj`) **and** the compiled assembly references.

## DI lifetimes

| Service | Lifetime | Why |
| --- | --- | --- |
| Policies, `ResourceAuthorizationService`, `NoticeCatalog`, `PermissionCatalog`, `PrincipalResolver` | Singleton | Immutable after startup |
| `DbDataSource`, repository/store | Singleton | Thread-safe pool; every call opens its own authorized transaction |
| Use-case handlers, `AuthorizedReader` | Scoped | Free to take per-request collaborators later without becoming captive dependencies |

## Consequences

- More projects in the Credit context (4 instead of 1), deliberately not repeated for Customer.
- The authorization matrix (system-wide specification) moved to `tests/VirtualBank.Specifications.Tests`,
  which composes the policies of all contexts; the `Specifications` support library is context-agnostic.
- Notice codes are owned by the context that defines the action (`CreditPolicies.NoticeRules`).
- Approving through the domain while the database UPDATE policy re-checks the same rules is intentional
  defense in depth (application rule, RLS `applications_approve`), not duplication of business logic in code.

## Amendment (M1.5.1, review round 6)

- HR, operations, audit, document and platform capability rules left the core. Their modules do not exist yet;
  the matrix keeps them as `status: planned` cases checked against design policies in the specification tests,
  and every running service must deny them with `NO_POLICY`.
- Customer is organized per feature (`Customers/Features/{ListCustomers,GetCustomer,ListCustomerAccounts}`,
  `Customers/Shared`), with `CustomerModule` as composition root.
- The "no approval endpoint" rule is checked on the registered routes (`EndpointDataSource`, exact allowlist),
  not on source text. Both APIs expose `Add…Service` / `Map…Service` used by `Program.cs` and the route tests.

