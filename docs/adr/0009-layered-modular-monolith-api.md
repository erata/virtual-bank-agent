# ADR-0009: One layered API — Model, Application, Infrastructure, Presentation, Web

> **Updated by [ADR-0014](0014-human-confirmed-changes-and-core-banking-operations.md) (2026-11-25):** the "no approval route before M6" rule became "decisions are only prepared by route; one confirm route executes".

- Status: Accepted
- Date: 2026-11-22
- Supersedes: ADR-0006 (Clean Architecture for Credit, Vertical Slice for Customer, one API per context)
- Amends: ADR-0002 for the Customer and Credit APIs (they now share one process)

## Context

M1.5 had two APIs with different internal styles: Credit as four Clean-Architecture projects, Customer as a
vertical slice, plus three shared building-block libraries. Technically sound, but the author's professional
projects use one consistent layout — four or five layer projects per application, CQRS handlers, business areas
as folders inside each layer — and the repository should show that layout end to end, so every part can be
explained from experience.

## Decision

**One API host, five layer projects, business modules as folders inside each layer.**

```text
src/
  VirtualBank.Model/            entities, value objects, rules          Customers/, Credit/, Common/
  VirtualBank.Application/      use cases (CQRS), ports, authorization  Customers/, Credit/, Authorization/, Common/
  VirtualBank.Infrastructure/   PostgreSQL (RLS), Keycloak/JWT, audit   Persistence/{Customers,Credit}, Identity/, Auditing/
  VirtualBank.Presentation/     endpoints, contracts, result → HTTP     Customers/, Credit/, Http/
  VirtualBank.Web/              host: Program.cs, composition root, appsettings
src/Frontend/virtual-bank-ui/   Angular UI (ADR-0008)
```

Dependencies point inwards only: `Web → Presentation → Application → Model` and `Infrastructure → Application`.
Web is the only project that references both Presentation and Infrastructure (composition root).

- **Model** has no references. **Application** references Model only — no ASP.NET, no driver, no DI framework;
  the composition root in Web registers its services. Ports (`ICustomerRepository`, `ICreditApplicationRepository`,
  `IAuthorizationAudit`, `IPrincipalResolver`) are interfaces in Application, implemented only in Infrastructure.
- **CQRS without MediatR**: six handlers under `Queries/` and `Commands/`, each with its `…Query`/`…Command`
  message, returning a transport-neutral `UseCaseResult<T>`. Presentation translates it to HTTP in one place.
- The **authorization engine** lives in `Application/Authorization` (mechanisms only); each module ships its own
  policies (`Customers/Authorization/CustomerPolicies`, `Credit/Authorization/CreditPolicies`).
- **Module boundaries** replace service boundaries for Customers vs. Credit: no type and no source file of one module
  references the other (architecture tests on compiled signatures and on usings).

**What stays exactly as before:** two databases, two least-privilege DB logins (`customer_api` for `customer_db`,
`credit_api` for `credit_db`; the Customers repositories hold only the customer pool), FORCE RLS with the
transaction-local context, re-authorization of every row, deterministic notices, no approval route before M6.

**Identity:** one resource-server client and audience, `virtual-bank-api`, replaces `customer-api`/`credit-api`.
The demo company is now **Virtual Bank** (realm `virtual-bank`).

## Consequences

- One service to run, debug and deploy; nginx proxies all of `/api/` to it.
- **Weaker process isolation (accepted):** a compromised API process holds both DB logins. RLS still limits it to the
  rows matching whatever context it sets (see threat model), and each module's queries still run under its own login.
  The previous "each service registers only its own policies" argument becomes "each module owns its policies; the
  matrix proves an action without a policy is denied".
- **Updated by ADR-0011:** the Assistant module now executes inside the one API host and calls Application use cases in-process. Ingestion is a machine-authorized API endpoint, not an independent worker process in the current Compose stack.
- Tests mirror the layers: `tests/unit/VirtualBank.{Model,Application,Infrastructure,Presentation}.UnitTests`,
  the authorization matrix, architecture tests (layer rules, module boundaries, route allowlist), DB/RLS suite,
  Bruno, browser tests.

## Alternatives considered

- **Keep two APIs with the same five-layer template each:** doubles the projects for two small modules; the security
  evidence (separate DB logins, RLS) does not depend on separate processes.
- **Keep ADR-0006:** shows two styles side by side, but does not match the layout the author works with daily.
