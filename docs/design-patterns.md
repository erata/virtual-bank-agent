# Design patterns and principles — where to find them

A map for reviewers and interviewers: every entry points at real code, not at an example written for the doc.

| Pattern / principle | Where | Why it is used there |
| --- | --- | --- |
| **Clean / Onion Architecture (layered)** | `src/VirtualBank.{Model,Application,Infrastructure,Presentation,Web}` | Business rules framework-free, one consistent layout ([ADR-0009](adr/0009-layered-modular-monolith-api.md)) |
| **Modular monolith** | `Customers/` and `Credit/` folders in every layer, guarded by `ModuleBoundaryTests` | Module separation without a second process; each module keeps its own DB login |
| **CQRS (without MediatR)** | `Application/*/Queries/*`, `Application/Credit/Commands/*` | Separate read and write paths; handlers resolved directly from DI |
| **Ports and Adapters** | ports in `Application/*/Abstractions` and `IPrincipalResolver` → adapters in `Infrastructure` | Use cases testable with in-memory repositories that ignore RLS |
| **Aggregate root / rich domain model** | `Model/Credit/CreditApplication.cs` | The only transition to *Approved*; invariants checked on rehydration |
| **Value objects** | `Money`, `CreditNoteText` | Validation once, at the boundary; no primitive obsession for amounts |
| **Strongly typed ID** | `CreditApplicationId` (sealed record, `TryParse`/`Parse`, no invalid default) | An application id cannot be confused with a customer id or an unvalidated string |
| **Aggregate invariants mirrored in the schema** | `CreditApplication.Rehydrate` ↔ CHECK constraints on `credit.credit_applications` | Domain and database accept exactly the same states |
| **Strategy** | `IResourcePolicy` implementations, selected by `ResourceAuthorizationService` per (resource type, action) | New resources get new policies without touching the engine (Open/Closed) |
| **Result object** | `UseCaseResult<T>` + `UseCaseHttpResults.ToHttp` | Application layer stays transport-neutral; HTTP decided in one place |
| **Composition root** | `Web/CompositionRoot.cs` (+ `Infrastructure/DependencyInjection.cs`) | Only place that knows concrete types; lifetimes documented in ADR-0006/0009 |
| **Options pattern** | `KeycloakOptions` (`Keycloak:*`) | Realm, issuer and URLs configurable; nothing hardcoded |
| **Unit of Work (transaction-scoped)** | `Infrastructure/Persistence/AuthorizedDbSession` | RLS context is transaction-local; commit or roll back as one unit |
| **Decorator / delegating handler** | `BackchannelRewriteHandler` | Rewrites Keycloak backchannel URLs without changing JWT handling |
| **Endpoint filter (pipeline)** | `PrincipalContextFilter` | Principal resolved once per request, before any handler |
| **Fail closed / deny by default** | `ResourceAuthorizationService` (`NO_POLICY`), missing approval limit, inactive principals | Security property, tested in every context |
| **Dependency Inversion (SOLID D)** | Application defines `IAuthorizationAudit`, `ICreditApplicationRepository`; outer layers implement | Inner layers never depend on frameworks — enforced by architecture tests |
| **Single Responsibility (SOLID S)** | `AuthorizedReader` (load + re-authorize), handlers (one use case each) | The RLS re-check rule exists once |
| **Interface Segregation (SOLID I)** | Small ports (`IAuthorizationAudit` has two methods) | Test doubles stay trivial |
| **Executable specification** | `tests/specifications/authorization-matrix.yaml` | Expected outcomes live outside the code they test |
| **Architecture tests** | `tests/architecture/VirtualBank.ArchitectureTests` | Dependency rules are verified, not just drawn |
| **Confirmation card (prepare / confirm)** | `PendingActions.cs`, `ActionConfirmation`, `IConfirmableAction` handlers, Angular `ActionCard` | Every change — from a menu or the assistant — is prepared without side effects and executed only when the same user confirms a single-use, expiring card |
| **Route allowlist from routing metadata** | `RouteRuleTests` (reads `EndpointDataSource` of the real composition roots) | A new route fails the build until added deliberately; credit decisions may only be *prepared* by route and executed through the single `/api/actions/{id}/confirm` route |
| **Specification with lifecycle** | `status: planned` cases in the authorization matrix | Design intent is tested without pretending it is enforced |
| **Ports and adapters in the UI** | `application/ports/*` (abstract classes as DI tokens) ↔ `infrastructure/*` adapters, wired in `app.config.ts` | Pages are tested with fake gateways; the OIDC library can be swapped |
| **Result type instead of exceptions (UI)** | `domain/result.ts`, `infrastructure/http/call.ts`, `ProblemView` | Every page renders 403/404/401/validation the same way |
| **Same-origin reverse proxy** | `src/Frontend/virtual-bank-ui/nginx/default.conf.template` | No CORS, token scoped to `/api/`, one origin under one CSP |
| **Externalized configuration** | `/config.json` rendered from environment variables, validated by `parseRuntimeConfig` | One build for every environment |

