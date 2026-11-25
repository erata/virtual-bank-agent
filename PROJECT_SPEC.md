> **DESIGN SPECIFICATION / HISTORICAL ROADMAP — NOT AN IMPLEMENTATION STATUS REPORT.** Some earlier sections specify a standalone Agent Service and Microsoft Agent Framework; ADR-0009 and ADR-0011 supersede that architecture. Current v4 source decisions: one layered modular-monolith API, in-process Semantic Kernel Assistant, OpenRouter defaults, EF Core schema migrations, PostgreSQL RLS. Refer to `README.md`, `docs/architecture.md` and `docs/VALIDATION_STATUS.md` for what exists and what has actually been verified.

<!--
  PROJECT_SPEC.md — binding implementation specification, version 1.0.8
  Change log:
    1.0.0  Consolidated specification (personas incl. tom.auditor, credit_assignments projection,
           permission-aware RLS, reason/notice codes, invariants I1–I27, embedding dimension 384).
    1.0.1  Permission-aware RLS context and `customer.read.assigned` (ADR-0005).
    1.0.2  Historical release: named BoundedAccess (renamed to VirtualBank in 1.0.8); the
           fictional demo originally used a Helios realm. Preserve the decision in the ADR history.
    1.0.3  Neutral client IDs `virtual-bank-ui` / `virtual-bank-cli`; per-service DB owners
           `customer_owner` / `credit_owner`; realm and client IDs configurable. Milestone M1.5: Credit in
           Clean Architecture (CQRS without MediatR), Customer stays Vertical Slice, policies per bounded
           context, architecture tests (ADR-0006).
    1.0.4  M1.5.1 hardening (domain invariants = CHECK constraints, strongly typed id, machine identity from a
           signed claim, issuer/JWKS readiness, Customer feature folders, route-metadata tests, planned matrix
           cases). Milestone M1.6: EF Core Code First with RLS in migrations (ADR-0007).
    1.0.5  M1.5.2: machine claim honored only for allowlisted clients (Authorization:MachineClients);
           blank approver and undefined status rejected in domain and schema.
    1.0.6  M1 web UI: Angular 22 (later moved to src/Frontend/virtual-bank-ui), layered (domain, application ports/use cases,
           infrastructure adapters, presentation), PKCE, same-origin nginx proxy, strict CSP (ADR-0008).
    1.0.7  One layered API (Model, Application, Infrastructure, Presentation, Web) with Customers and Credit as
           modules (ADR-0009, supersedes ADR-0006); audience virtual-bank-api; demo company Virtual Bank
           (realm virtual-bank); UI moved to src/Frontend.
    1.0.8  Project/repository and all C# namespaces renamed to VirtualBank / virtual-bank-agent;
           demo realm stays virtual-bank. Baseline remains M1 with AI agent and RAG scheduled for M2/M3.
           The single business API and module layout from ADR-0009 are binding.
-->

# VIRTUALBANK — PERMISSION-AWARE ENTERPRISE AI AGENT

## PROJECT_SPEC.md — FINAL MASTER IMPLEMENTATION SPECIFICATION

### Version 1.0.8 — Binding Architecture and Implementation Contract

**Project:** VirtualBank\
**Repository:** `virtual-bank-agent`\
**Type:** Personal Engineering Portfolio Project\
**Domain:** Fictional German Financial Services\
**Primary Stack:** .NET 10, Angular, Keycloak, PostgreSQL, pgvector\
**Architecture:** Permission-Aware Enterprise AI Agent\
**Execution:** Docker Compose\
**Engineering Language:** English\
**Supported RAG Languages:** English, German, Turkish and mixed-language queries

### Current implementation contract (overrides superseded service descriptions below)

- **Name:** `VirtualBank` is the product and fictional demonstration organization. Source assemblies,
  solution, namespaces, repository and Angular package use the VirtualBank / `virtual-bank-*` identity.
- **M1 business backend:** one ASP.NET Core resource server in five assemblies:
  `VirtualBank.Model`, `.Application`, `.Infrastructure`, `.Presentation`, `.Web`.
  Customers and Credit are modules within the same host and share the single JWT audience
  `virtual-bank-api`, but have separate least-privilege PostgreSQL runtime users and databases.
  No separate Customer/Credit resource servers exist in this version. See ADR-0009.
- **M2 assistant (implemented, ADR-0011):** an `Assistant` module inside the same API. Seven tools are adapters over
  the existing use cases; `ToolPolicyGate` (budget, known tool, permission, risk tier, write budget, arguments)
  runs before every call; notices come from application code. Runtime port with two adapters: Semantic Kernel +
  OpenRouter (`gpt-4o-mini`) and a deterministic offline runtime (default). This supersedes the earlier plan of a
  separate agent process on Microsoft Agent Framework for M2; a separate process with token exchange remains M5.
- **M3 retrieval:** multilingual embeddings, document ACLs, ingestion, secure RAG and pgvector similarity
  search remain **planned**, not implemented. The pgvector-capable image is not a working vector index.
- **Milestone state:** M1 business API and UI source exist; real .NET / Docker / Keycloak / database
  end-to-end compatibility is not yet independently verified. EF Core Code First is planned for M1.6.
- Historical descriptions of two Customer/Credit API processes, separate JWT audiences, or split
  Clean Architecture projects are **superseded** by this contract and ADR-0009.


---

# 0. MISSION AND IMPLEMENTATION PRINCIPLES

Act as a Principal Software Architect, Senior .NET Engineer, AI Agent Engineer, Identity and Access Management Engineer, Application Security Engineer, PostgreSQL Engineer, DevOps Engineer and Test Automation Architect.

Your responsibility is to design, implement, test and document a complete, executable, GitHub-ready reference application named:

**VirtualBank**

The project simulates a secure AI assistant for a fictional German financial-services organization named **Virtual Bank**.

It is a personal engineering reference implementation, not a claim of production experience at an actual bank.

The completed repository must demonstrate senior-level engineering competence through working code, architectural decisions, independently enforced security boundaries, realistic business scenarios and automated verification.

## 0.1 Core architectural principle

**The LLM never decides authorization.**

A language model may:

- Interpret user requests.
- Select candidate tools.
- Request tool execution.
- Summarize authorized information.
- Generate natural-language answers.

However, deterministic application and infrastructure components must determine:

- Whether a tool is available.
- Whether a tool may execute.
- Whether an API request is authorized.
- Whether a database record is accessible.
- Whether a document or chunk is retrievable.
- Whether an operation requires human confirmation.
- Whether a cached response remains authorized.
- Whether sensitive resource existence may be disclosed.

Authorization must be enforced independently of the model.

## 0.2 Primary product story

The same natural-language question may produce different results depending on the authenticated principal.

For example, an agent may need multiple tools to answer a request.

Some tools may be authorized, while others are denied.

The agent must:

1. Execute only authorized operations.
2. Reject unauthorized operations.
3. Preserve authorized partial results.
4. Prevent unauthorized information from entering model context.
5. Return deterministic permission notices when disclosure policy permits.
6. Avoid inventing unavailable information.
7. Record safe audit information.

## 0.3 Engineering integrity

Never claim that functionality is implemented merely because files exist.

Distinguish:

- Implemented and verified
- Implemented but not executed
- Partially implemented
- Planned
- Optional

Never fabricate build output, test results or API responses.

Never disable authorization to make a test pass.

Do not create placeholder implementations that falsely appear complete.

Work incrementally and maintain a runnable repository after each completed milestone.

---

# 1. ENGINEERING STANDARDS

## 1.1 English-only code and documentation

All technical engineering content must be written in English.

This includes:

- Class names
- Interface names
- Method names
- Property names
- Variables
- Namespaces
- Source-code comments
- XML documentation
- Exception messages
- Log messages
- Audit event names
- Test names
- Test descriptions
- OpenAPI descriptions
- README
- ADRs
- Architecture documentation
- Git commit messages
- CI workflow names

Exceptions are permitted for:

- Multilingual business documents
- User-facing localized messages
- Frontend localization resources
- Natural-language test queries
- Multilingual RAG fixtures
- User-generated content

Canonical permission identifiers and authorization reason codes must remain language-independent.

## 1.2 Code quality

Apply appropriate engineering principles:

- SOLID
- Separation of concerns
- Dependency injection
- Asynchronous I/O
- CancellationToken propagation
- Structured logging
- Input validation
- Typed configuration
- Centralized error handling
- Least privilege
- Fail-closed authorization
- Idempotency
- Concurrency protection
- Secure defaults

Avoid unnecessary abstractions and boilerplate.

The goal is maintainable software, not maximizing class or project counts.

## 1.3 Architecture styles

Use Clean/Hexagonal Architecture for:

- Authorization core
- Agent execution and policy boundaries
- ACL normalization and resolution
- Complex business rules

Use Vertical Slice Architecture for smaller CRUD-oriented API endpoints.

Do not create four-layer Clean Architecture projects for every trivial endpoint.

Applied (ADR-0009, superseding ADR-0006): **one API in five layer projects** — `VirtualBank.Model`,
`.Application` (CQRS use cases, ports, authorization engine), `.Infrastructure`, `.Presentation`, `.Web` (host and
composition root). Business modules (`Customers`, `Credit`, later HR, Operations, Documents) are folders inside each
layer and never reference each other; each module owns its policies and its database login. The Agent (M2) and the
retrieval pipeline (M3) follow the same layering. Layer dependencies and module boundaries are enforced by
`tests/architecture`. Model and Application reference no ASP.NET, EF Core or database driver types.

## 1.4 Binding specification

This document is the binding implementation specification.

If implementation discoveries require changes, record them in an Architecture Decision Record.

Do not silently change authorization semantics or business rules.

---

# 2. TECHNOLOGY STACK

Use stable, mutually compatible versions available at implementation time.

Verify actual library APIs, licenses and container compatibility.

## Backend

- .NET 10
- ASP.NET Core Web API
- C#
- Entity Framework Core where appropriate
- Npgsql
- PostgreSQL
- pgvector
- Semantic Kernel (M2 assistant runtime, ADR-0011); Microsoft Agent Framework as possible successor
- Microsoft.Extensions.AI where appropriate
- OpenTelemetry

## Frontend

- Angular
- TypeScript
- Angular Router
- HttpClient
- OpenID Connect Authorization Code Flow with PKCE
- A maintained compatible OIDC client library

## Identity

- Keycloak
- OAuth 2.0
- OpenID Connect
- JWT bearer authentication
- Confidential clients
- Service accounts
- Client credentials
- Standard Token Exchange V2

## RAG

- Multilingual embedding provider
- `intfloat/multilingual-e5-small`
- Hugging Face Text Embeddings Inference, subject to compatibility verification
- SentenceTransformers-based fallback
- PostgreSQL/pgvector
- PDF, DOCX, Markdown, TXT and JSON parsers
- Authorization-aware semantic retrieval
- PostgreSQL lexical search
- Exact metadata lookup
- Optional hybrid retrieval and reranking

## Testing

- xUnit
- Built-in xUnit assertions
- NSubstitute or another compatible mocking library
- Testcontainers for .NET
- ASP.NET Core WebApplicationFactory
- Bruno
- Bruno CLI
- Playwright
- Coverage tooling
- GitHub Actions

All mandatory core functionality must work without paid API subscriptions.

---

# 3. BUSINESS DOMAIN

Use a fictional financial-services organization:

**Virtual Bank**

Implement these business areas:

1. Customer Management
2. Credit Applications
3. Human Resources
4. IT Operations
5. Document Management
6. Security Administration
7. AI Agent Assistance

Use deterministic synthetic business data.

Example identifiers:

- Customer `C-1002`
- Customer `C-5001`
- Credit application `K-100`
- Employee `E-100`
- Document `DOC-501`

Identifiers and relationships must be consistent across:

- Database records
- API responses
- Document contents
- Authorization policies
- Tool definitions
- Test fixtures
- Demo scenarios

Never use actual customer or employee data.

---

# 4. SYSTEM ARCHITECTURE

## 4.1 Trust-boundary-driven deployment

Do not create microservices solely to demonstrate microservices.

**Implemented business backend (M1):**

- Angular frontend with OIDC Authorization Code + PKCE
- One modular-monolith ASP.NET Core business API (`VirtualBank.Web`)
- Customers and Credit modules within the same API
- Keycloak (`virtual-bank` realm) and one `virtual-bank-api` audience
- Two PostgreSQL databases with separate runtime DB identities and real RLS

**Planned additions:**

- M2: separate Agent Service with tool authorization and API delegation
- M3: separate ingestion worker, local CPU embedding provider, secure pgvector retrieval,
  document ACL administration
- M6: HR/Operations features in the modular monolith unless explicitly changed by ADR
- Optional later: Ollama, OpenLDAP, Nextcloud, BookStack, OpenFGA, observability and Aspire

## 4.2 Logical architecture

```text
Angular Frontend --PKCE--> Keycloak
       |
       | Validated access token (aud = virtual-bank-api)
       v
VirtualBank.Web (single business API / composition root)
       +--> Presentation --HTTP routes--> Application use cases
       |                           |
       |                           +--> Model (domain rules)
       |                           +--> Application authorization policies
       +--> Infrastructure (repository + identity adapters)
                   +--> Customer DB (customer_api, RLS)
                   +--> Credit DB (credit_api, RLS)

M2: Agent Service --> tool registry --> ToolPolicyGate --> VirtualBank business API
M3: Ingestion --> document ACLs --> embeddings --> pgvector secure index
     Agent Service --> secure retriever --> authorized knowledge context
```

## 4.3 Authorization and resource boundary

The **single** business resource server must independently validate JWT issuer, audience,
expiry and signing key, then enforce module-specific authorization in Application plus
PostgreSQL RLS. This is not process isolation between Customers and Credit; a compromised
host has a larger blast radius, and a trusted application can influence DB context.
The Agent Service must not query Customers/Credit business tables directly.

When adding more host processes or token audiences, introduce an ADR and update integration
and token exchange tests; do not assume separate Customer and Credit audiences exist.

## 4.4 Database ownership

Two service databases and their least-privilege runtime DB identities remain independent,
even though the business API is one process. Migration identities are separate from runtime
identities. RLS protects against accidental query mistakes, not a fully compromised trusted host.

## 4.5 Docker Compose

Docker Compose is the canonical runtime. Aspire may be optional developer orchestration.

---

# 5. KEYCLOAK IDENTITY MODEL

Keycloak provides:

- Authentication
- User identities
- Groups
- Roles
- Identity attributes
- Client authentication
- Access tokens
- Service accounts
- Token exchange

Keycloak does not automatically implement all resource-level authorization.

The application must answer:

**Can this principal perform this action on this resource under these conditions?**

## 5.1 Principal categories

- Human User
- Administrative User
- Machine / Service Account

These are identity categories, not sufficient authorization grants.

## 5.2 Roles

Implement:

- `platform-admin`
- `security-admin`
- `auditor`
- `customer-advisor`
- `credit-analyst`
- `credit-approver`
- `hr-specialist`
- `operations-operator`
- `svc-ingestion`
- `svc-reporting`

## 5.3 Permissions

Implement a centralized permission catalog.

At minimum:

- `customer.read`
- `customer.read-sensitive`
- `credit.read`
- `credit.note.write`
- `credit.approve`
- `employee.read`
- `employee.salary.read`
- `operations.read`
- `audit.read`
- `diagnostics.read`
- `document.read`
- `document.ingest`
- `document.acl.manage`
- `system.configure`

Application policies should evaluate granular permissions and resource conditions, not broad role names alone.

## 5.4 Attributes

Support:

- `branch`
- `department`
- `clearance`
- `approvalLimit`
- `employmentStatus`

## 5.5 Groups

Support:

- `/Customer/Stuttgart`
- `/Customer/Munich`
- `/Credit`
- `/Credit/Analysis`
- `/Credit/Approval`
- `/HR`
- `/IT/Operations`
- `/Security`

## 5.6 Hierarchical group resolution

Group claims must expand to ancestor paths.

Example:

`/Credit/Analysis`

resolves to:

- `/Credit/Analysis`
- `/Credit`

Requirements:

- Segment-aware matching
- No accidental prefix collisions
- Explicit deny handling
- No automatic child-group access for parent-group users
- Unit-test coverage

`/Credit` must not match `/Creditors`.

Use trusted identity data, not client-supplied groups.

---

# 6. DEMO PERSONAS

Create deterministic demo identities.

| Principal        | Role             | Purpose                          |
| ---------------- | ---------------- | -------------------------------- |
| `lena.admin`     | platform-admin   | Platform administration          |
| `sarah.security` | security-admin   | Document ACL administration      |
| `tom.auditor`    | auditor          | Audit and privileged diagnostics |
| `anna.berater`   | customer-advisor | Stuttgart customer access        |
| `mehmet.kredit`  | credit-analyst   | Assigned credit applications     |
| `max.approver`   | credit-approver  | Credit approval                  |
| `sophie.hr`      | hr-specialist    | HR data                          |
| `svc-ingestion`  | service account  | Ingestion                        |
| `svc-reporting`  | service account  | Operations reporting             |

## 6.1 Anna

Anna may:

- Read customers authorized by her Stuttgart branch.
- Read relevant customer-advisor documents.
- Use customer-related read tools.

Anna may not:

- Read arbitrary restricted Credit information.
- Read HR salaries.
- Approve credit applications.
- Read sensitive customer fields without explicit permission.

## 6.2 Mehmet

Mehmet may:

- Read assigned credit applications.
- Read customers linked to applications assigned to him.
- Read authorized Credit documents.
- Add permitted credit notes.

Mehmet may not:

- Read arbitrary customers.
- Automatically approve credit applications.
- Read HR salary information.

Sensitive customer fields must remain masked unless independently authorized.

## 6.3 Max

Max has:

`approvalLimit = 50000 EUR`

Approval requires:

- `credit.approve`
- Valid application state
- Amount within approval limit
- Application prepared by another person
- Explicit human confirmation
- Idempotent execution

Seed:

- One valid application
- One application above the limit
- One application prepared by Max

## 6.4 Lena

Lena administers the platform.

She does not automatically gain business-data access to:

- Confidential credit records
- HR salary data
- Arbitrary customer records

## 6.5 Sarah

Sarah may manage permitted document ACLs.

She cannot automatically read protected document contents.

ACL administration must not bypass content authorization.

## 6.6 Tom

Tom is an auditor.

Tom has:

- `audit.read`
- `diagnostics.read`

Tom may:

- Read authorized audit metadata.
- View the privileged diagnostic trace.
- Inspect sanitized authorization decisions.

Tom may not:

- Read protected business records merely through his auditor role.
- Read confidential documents without explicit permission.
- Modify ACLs.
- Modify business data.
- Execute administrative mutations.

Use Tom in Playwright tests demonstrating privileged trace access.

Audit visibility must not reveal confidential document contents.

## 6.7 Service accounts

`svc-ingestion` may ingest permitted documents.

`svc-reporting` may perform authorized operations reporting.

Neither machine identity receives unrestricted business-data privileges or interactive user permissions.

---

# 7. CENTRAL AUTHORIZATION MODEL

Implement:

- RBAC
- ABAC
- ACL
- Separation of Duties
- Four-Eyes Principle
- Optional later ReBAC

Create an authorization abstraction such as:

```
public interface IResourceAuthorizationService
{
    Task<AuthorizationDecision> AuthorizeAsync(
        PrincipalContext principal,
        ResourceDescriptor resource,
        Permission action,
        CancellationToken cancellationToken);
}
```

Authorization decisions must include:

- Allow/Deny
- Reason code
- Permission
- Resource ID
- Policy ID
- Correlation ID
- Disclosure policy
- Sanitized audit metadata

## 7.1 General rules

- Deny by default.
- Explicit deny takes precedence within the canonical ACL policy.
- Mandatory authorization dimensions cannot bypass one another.
- Resource-specific business rules remain mandatory.
- Role permissions do not automatically imply unrestricted resource access.

## 7.2 Reason codes and notice codes

Use separate internal and external representations.

**Internal reason codes** describe the authorization decision and are written to audit records.

Examples:

- `MISSING_PERMISSION`
- `BRANCH_MISMATCH`
- `RESOURCE_NOT_ASSIGNED`
- `APPROVAL_LIMIT_EXCEEDED`
- `FOUR_EYES_VIOLATION`
- `DOCUMENT_ACL_DENIED`
- `INSUFFICIENT_CLEARANCE`
- `ACL_STALE`

**Notice codes** are derived deterministically from the internal reason code, resource type and disclosure policy.

Example:

Internal reason:

`MISSING_PERMISSION`

Resource:

`credit.application`

External notice:

`CREDIT_READ_REQUIRED`

The notice code is then localized.

Never allow the LLM to create arbitrary permission explanations.

For disclosure level `None`, do not produce a resource-revealing notice.

---

# 8. CREDIT ASSIGNMENT PROJECTION — MANDATORY M1 DECISION

The Customer API must enforce Mehmet's assigned-customer rule without making a synchronous HTTP call to Credit API for every customer read.

## 8.1 Projection table

Create a read-only authorization projection in the Customer data store:

```
CREATE TABLE credit_assignments (
    customer_id text NOT NULL,
    analyst_id text NOT NULL,
    PRIMARY KEY (customer_id, analyst_id)
);
```

The `analyst_id` must correspond to the authenticated principal's stable identity identifier, not the username.

Use appropriate indexes and referential integrity where feasible.

The Credit domain remains authoritative for actual assignment relationships.

The Customer projection is a derived copy.

## 8.2 Deterministic initial seeding

During M1:

- Seed Credit assignment records.
- Generate the Customer projection deterministically from the same canonical seed fixture.
- Verify the projection matches the Credit source records.

Do not maintain unrelated hardcoded fixture copies that may drift.

## 8.3 Customer authorization

For customer-advisor access:

- Require `customer.read`.
- Require matching branch.
- Enforce field restrictions.

For credit-analyst access:

- Require the appropriate customer-read permission.
- Require an existing assignment projection row linking the customer to the authenticated analyst.
- Enforce sensitive-field restrictions.

## 8.4 RLS

The Customer RLS policy must support both business access paths:

- Authorized branch-based access
- Authorized assigned-customer access

Conceptually:

```
(
    customer-advisor permission
    AND customer.branch = current_branch
)
OR
(
    credit-analyst customer-read permission
    AND EXISTS (
        matching credit_assignments row
    )
)
```

Use trusted server-established permission context.

Do not permit the client to select which access path applies.

Do not treat possession of a matching branch or assignment row as sufficient without the corresponding business permission.

## 8.5 Projection synchronization

M1 uses deterministic seed projection.

M4 introduces actual projection synchronization behavior.

Choose and document a suitable mechanism, such as:

- Transactional outbox plus event processing; or
- A controlled periodic synchronization process

Do not introduce Kafka solely for this feature.

The implementation must explicitly handle:

- Assignment creation
- Assignment removal
- Reassignment
- Duplicate events
- Synchronization failures
- Stale projection data

## 8.6 Security implications

A stale projection may preserve an assignment after it has been revoked.

Document this risk in the threat model.

Define an explicit authorization freshness policy.

For sensitive access, use a freshness threshold or authoritative revalidation when required.

Do not claim immediate assignment revocation if the projection is eventually consistent.

Test removal and reassignment scenarios.

---

# 9. BUSINESS API ROUTES (ONE RESOURCE SERVER)

The Customers and Credit routes belong to the same VirtualBank business API and share the
`virtual-bank-api` audience. The API must independently validate:

- JWT signature
- Issuer
- Audience
- Token lifetime
- Relevant authorization claims

The Agent Service must not bypass the business API authorization. Each module owns its
resource policy and least-privilege database connection.

## 9.1 Customers module

Implement:

- `GET /api/customers`
- `GET /api/customers/{id}`
- `GET /api/customers/{id}/accounts`

Demonstrate:

- Branch ABAC
- Assigned-customer relationship authorization
- Sensitive-field masking
- PostgreSQL RLS
- Resource hiding where appropriate

## 9.2 Credit module

Implement:

- `GET /api/credit/applications`
- `GET /api/credit/applications/{id}`
- `POST /api/credit/applications/{id}/notes`
- `POST /api/credit/applications/{id}/approve`

Demonstrate:

- Read versus approve permissions
- Assignment restrictions
- Approval limits
- Four-eyes
- Human-in-the-loop
- Idempotency
- Concurrency control
- Audit records

## 9.3 HR module (M6)

Implement during M6.

Endpoints:

- `GET /api/hr/employees/{id}`
- `GET /api/hr/employees/{id}/salary`
- `GET /api/hr/salary-bands`

Protect salary information separately.

Administrative access must not imply HR access.

## 9.4 Operations module (M6)

Implement during M6.

Endpoints:

- `GET /api/ops/jobs/failed`
- `GET /api/ops/incidents`
- `GET /api/ops/audit`

Demonstrate service-account authorization and read-only audit access.

## 9.5 HTTP semantics

Use:

- 401 for authentication failures.
- 403 for authenticated authorization failures when disclosure permits.
- 404 when resource existence should be concealed or the resource does not exist.

Document the resource-disclosure policy.

---

# 10. POSTGRESQL ROW-LEVEL SECURITY

Implement actual PostgreSQL RLS.

Do not simulate RLS using only EF Core query filters.

Use separate identities for:

- Migrations
- Customer API runtime
- Credit API runtime
- Ingestion
- Retrieval

Runtime identities must not possess `BYPASSRLS`.

Use `FORCE ROW LEVEL SECURITY` where appropriate.

Do not use superuser or unrestricted schema-owner connections at runtime.

## 10.1 Trusted context

Create transaction-local authorization context based on verified identity information.

Example:

```
SELECT set_config('app.principal_id', '...', true);
SELECT set_config('app.branch', 'STR', true);
```

Only the trusted server establishes context.

Do not trust client-provided branch, role or assignment values.

## 10.2 Required RLS behavior

Test:

- Same-branch access
- Cross-branch denial
- Assigned-customer access
- Unassigned-customer denial
- Missing authorization context
- Wrong permission context
- Connection reuse
- Concurrent principals
- API-side filter omitted
- Runtime database role restrictions
- Table-owner behavior

## 10.3 Threat-model limitation

RLS protects against certain programming mistakes, including accidentally missing query predicates.

It does not fully protect against a compromised trusted application capable of falsifying database authorization context.

Document this limitation explicitly.

---

# 11. AGENT RUNTIME AND TOOL CALLING

Use an agent framework through a replaceable abstraction (M2: Semantic Kernel behind `IAssistantRuntime`, ADR-0011).

Create:

- Agent Runtime
- Tool Registry
- ToolPolicyGate
- Tool Executor
- Secure Retriever
- Response Composer
- Authorization Notice Service
- Audit Service

The LLM may request tool execution but cannot authorize it.

## 11.1 Tool metadata

Each tool defines:

- Name
- Description
- Input schema
- Required permission
- Resource policy
- Target API
- Target audience
- Disclosure mode
- Side-effect classification
- Human approval requirement
- Timeout
- Audit category

## 11.2 Tool discovery modes

**Hide mode**

Unauthorized tools are not included in the model-visible catalog.

**Explain mode**

Restricted tool requests may receive deterministic, policy-approved explanations without executing the tool.

Always reauthorize immediately before execution.

## 11.3 Correct partial-success example

For `anna.berater`:

- `customer.get` → ALLOW
- `credit.get_application` → DENY, `PermissionHint`
- `credit.approve` → HIDDEN in Hide mode

Do not invoke `credit.approve`.

Do not include an unrelated HR tool in the customer-credit partial-success example.

The agent may return available customer information and a safe notice explaining the missing Credit permission.

## 11.4 Structured response

Example:

```
{
  "answer": "Authorized customer information...",
  "citations": [],
  "notices": [
    {
      "code": "CREDIT_READ_REQUIRED",
      "level": "PermissionHint",
      "text": "Credit application information requires the appropriate permission."
    }
  ],
  "correlationId": "example-id"
}
```

The LLM produces the answer.

The authorization layer produces the notices.

The UI displays them separately.

---

# 12. TOKEN EXCHANGE AND DELEGATED ACCESS

Implement Keycloak Standard Token Exchange V2 during M5.

The Agent Service must not use a universal business-data administrator token for downstream operations.

Use appropriately restricted tokens and target audiences.

The downstream API remains responsible for resource authorization.

## Required tests

Verify:

1. Subject propagation.
2. Target audience restriction.
3. Scope behavior.
4. Rejection by unrelated APIs.
5. Absence of privilege escalation.
6. Invalid issuer rejection.
7. Expired token rejection.
8. Wrong-audience rejection.
9. Audit identity correlation.
10. Actual supported delegation semantics.

Do not assume specific semantics for:

- `azp`
- `act`
- `actor_token`
- `may_act`

Verify behavior against the pinned Keycloak version.

Document unsupported semantics in an ADR.

---

# 13. SECURE MULTILINGUAL RAG

Multilingual RAG is mandatory.

Support:

- English
- German
- Turkish
- Mixed-language queries
- Code-switching
- Cross-lingual semantic retrieval
- Authorization-aware search

## 13.1 Independent language dimensions

Treat separately:

- UI locale
- Query language
- Document language
- Response language

The frontend language must never restrict retrieval.

## 13.2 Mixed-language examples

Support queries such as:

"Stuttgart branch'teki Benutzer için Kredit approval limit nedir?"

"Mehmet'in Kredit Antrag approval limit'i ve ilgili Richtlinie nedir?"

"Find the Benutzer access policy for müşteri danışmanları."

Do not require manual language selection.

## 13.3 Cross-lingual semantics

Support semantic relationships such as:

- user / Benutzer / kullanıcı
- credit application / Kreditantrag / kredi başvurusu
- access policy / Zugriffsrichtlinie / erişim politikası

Use real multilingual embeddings.

Do not rely exclusively on hardcoded translation dictionaries.

## 13.4 Embedding model

Initial model:

`intfloat/multilingual-e5-small`

**Expected vector dimension: 384.**

Default target runtime:

A pinned, CPU-compatible Hugging Face Text Embeddings Inference container.

Verify actual model and image compatibility.

If TEI cannot reliably serve the selected model, use a pinned SentenceTransformers-based CPU service.

Keep the same application embedding interface.

Document the runtime substitution.

Do not silently change models.

## 13.5 E5 formatting

Use:

`query: `

for search queries.

Use:

`passage: `

for document chunks.

Apply prefixes consistently through the embedding adapter.

Verify whether the serving runtime already applies preprocessing to avoid duplicate prefixes.

Test the actual embedding input.

## 13.6 Embedding metadata

Store:

- Model identifier
- Model revision
- Vector dimension
- Embedding configuration
- Index version

Do not mix incompatible vectors.

Re-index when the embedding space changes.

## 13.7 Fake model separation

Use FakeLLM for deterministic agent and security tests.

Use actual multilingual embeddings for semantic retrieval evaluations.

Do not claim FakeLLM proves semantic search quality.

---

# 14. BUSINESS ENTITIES VERSUS RAG

Structured business records must primarily be accessed through tools and APIs.

Examples:

- Customer IDs
- Credit application IDs
- Employee IDs

For customer `C-1002`, use Customer API.

For credit application `K-100`, use Credit API.

For employee salary data, use HR API.

Use RAG for:

- Policies
- Guidelines
- Manuals
- Procedures
- Internal knowledge documents

A request may combine API tools and RAG.

## 14.1 Exact identifiers

Do not rely on dense embeddings to distinguish:

- `C-1002`
- `C-1003`

Use explicit identifier parsing and structured tool routing.

## 14.2 Lexical retrieval

Use PostgreSQL full-text search with the `simple` configuration for language-neutral lexical retrieval.

Support exact metadata matching for:

- Document IDs
- Product codes
- Other structured document identifiers

Do not assume full-text tokenization preserves punctuation-delimited IDs exactly.

## 14.3 Optional hybrid retrieval

Support extensible:

- Dense semantic retrieval
- Lexical retrieval
- Exact metadata lookup
- Ranking fusion
- Reranking

Every retrieval branch must apply the same authorization policies.

---

# 15. DOCUMENT INGESTION

Support:

- PDF
- DOCX
- Markdown
- TXT
- JSON

Generate at least three meaningful documents per format.

Minimum total: 15.

Prefer 18–20 documents.

Use English, German and Turkish content.

Documents should include interconnected fictional business facts.

## 15.1 Required topics

Include:

- Public product information
- Online banking FAQ
- Branch information
- Customer onboarding
- Internal IT access policy
- Product conditions
- Employee onboarding
- Credit guidelines
- Scoring model
- Special credit cases
- HR salary bands
- Remote work policy
- Restricted restructuring draft
- Operations runbook
- Incident report
- Core banking handbook
- Prompt-injection sample

## 15.2 Parsing

PDF:

Preserve page references where possible.

DOCX:

Preserve headings.

Markdown:

Chunk by semantic sections.

TXT:

Use paragraph-aware chunking.

JSON:

Chunk meaningful records, not arbitrary text fragments.

## 15.3 Pipeline

Document Source → ACL Resolution → Parsing → Chunking → Embedding → Secure Index.

Store stable document/chunk IDs.

Support:

- Content hash
- Content version
- ACL version
- Source metadata
- Language metadata
- Citation metadata
- Idempotent ingestion

---

# 16. ACL SOURCE OF TRUTH

## 16.1 Filesystem

`_acl.yaml` files initialize local document permissions.

After initial import:

**PostgreSQL is the canonical authority for local filesystem ACLs.**

Content synchronization must not silently overwrite administrator changes.

Changes to seed ACL files require an explicit controlled re-seed or migration.

## 16.2 Document ACL Administration API

Mandatory in M3.

Implement:

- `GET /api/document-acls/{documentId}`
- `PUT /api/document-acls/{documentId}`
- `GET /api/document-acls/{documentId}/effective`
- `GET /api/document-acls/{documentId}/history`

Require:

`document.acl.manage`

Use:

- ACL versioning
- Optimistic concurrency
- Audit history
- Validation
- Inheritance evaluation
- Explicit deny

Sarah may manage permitted ACL metadata without reading protected content.

## 16.3 External sources

For Nextcloud, BookStack and future connectors:

The external system remains authoritative for its ACL.

The local ACL is a synchronized read-only mirror.

The local administration API must not falsely claim to modify external permissions.

Either reject unsupported external ACL mutations or implement a real source-specific write-back operation.

## 16.4 Content and ACL separation

ACL changes must not trigger unnecessary re-embedding.

Content changes must not reset customized permissions.

Store separate content and ACL versions.

---

# 17. CANONICAL DOCUMENT ACL MODEL

Support:

- Public access
- Internal access
- Group allow
- Role allow
- User allow
- Explicit deny
- Folder inheritance
- Document override
- Classification
- Branch restrictions
- Disclosure policy

Metadata:

- Resource ID
- Parent ID
- Source ID
- Allowed principals
- Denied principals
- Classification
- Branch
- Inheritance mode
- Disclosure mode
- ACL version
- ACL synchronization timestamp

Define inheritance semantics explicitly.

Normalize external ACLs safely.

If permissions cannot be represented accurately, deny access until resolved.

---

# 18. SECURE RETRIEVAL

Unauthorized chunks must never enter LLM context.

Do not retrieve unrestricted context and ask the model to ignore prohibited data.

Use authorization-aware candidate selection.

## Retrieval pipeline

1. Authenticate principal.
2. Resolve trusted identity claims.
3. Expand group ancestors.
4. Parse original query.
5. Resolve structured entities through tools when necessary.
6. Generate multilingual query embedding.
7. Apply authorization constraints.
8. Retrieve authorized semantic and lexical candidates.
9. Merge and deduplicate results.
10. Validate freshness.
11. Construct authorized context.
12. Generate answer.
13. Return authorized citations and deterministic notices.

## 18.1 Vector search

Begin with exact vector similarity search.

Introduce HNSW only after correctness tests pass.

Investigate approximate-search behavior under strong authorization filters.

## 18.2 Security tests

Verify:

- Forbidden document IDs absent from model context.
- Forbidden content absent from model context.
- Forbidden titles absent from citations.
- Language switching does not bypass ACLs.
- Query expansion does not bypass ACLs.
- Stale permissions do not silently authorize retrieval.
- Cache does not expose another user's content.
- Conversation history cannot reintroduce revoked data.

---

# 19. REVOCATION AND CONSISTENCY

Treat the following separately.

## 19.1 Group membership changes

Use trusted identity claims and configured freshness rules.

Initial access-token lifetime target:

Approximately five minutes.

For sensitive actions, evaluate authoritative current authorization checks.

Do not claim immediate revocation while stale valid tokens are accepted.

## 19.2 Document ACL changes

Store:

- ACL version
- Last synchronization timestamp

Local ACLs are authoritative in PostgreSQL.

External ACLs synchronize from their source.

Use a configurable freshness threshold.

Fail closed when required authorization freshness cannot be guaranteed.

## 19.3 Document deletion

Use tombstones.

Exclude detected deleted documents from retrieval.

Cleanup chunks safely.

Document source deletion detection latency.

## 19.4 Content updates

Re-embed only when relevant content changes.

Keep ACL versioning independent.

## 19.5 Credit assignment projection

M4 must implement synchronization and staleness handling for:

`credit_assignments(customer_id, analyst_id)`

Test:

- Assignment created
- Assignment removed
- Analyst reassigned
- Synchronization delayed
- Duplicate update
- Stale projection
- Revocation during active session

Implement a policy that does not silently preserve sensitive access indefinitely.

## 19.6 Cache and conversation isolation

Never reuse another user's authorized context.

Revalidate authorization before reusing protected retrieval results.

Do not carry old tool results into prompts after access revocation.

---

# 20. PROMPT INJECTION SECURITY

Treat documents and external tool outputs as untrusted.

Include malicious test content attempting to:

- Invoke restricted tools.
- Override system instructions.
- Reveal hidden documents.
- Modify authorization notices.
- Trigger a state-changing operation.
- Access another user's context.

Include this exact statement in README and threat model:

**"Authorization limits the blast radius of prompt injection; it does not prevent it."**

Authorization prevents unauthorized operations.

It does not guarantee that an authorized tool cannot be misused through model manipulation.

Mitigations:

- Side-effect classification
- Human approval
- Reauthorization
- Input validation
- Restricted tool surface
- No arbitrary data-egress tools in the core demonstration
- Audit logging
- Security regression tests

Test that an injection-triggered authorized state-changing operation cannot execute without explicit confirmation.

---

# 21. HUMAN-IN-THE-LOOP

For credit approval:

1. Agent proposes an operation.
2. Backend validates parameters.
3. Backend creates a pending action.
4. UI displays action details.
5. User confirms or rejects.
6. Backend revalidates identity and permissions.
7. Backend revalidates business state.
8. Action executes exactly once.
9. Audit record is written.

Support:

- Pending
- Approved
- Rejected
- Expired
- Already executed
- Permission revoked
- Business state changed

Use idempotency and concurrency controls.

Model-generated text must never count as user approval.

---

# 22. FRONTEND

Use Angular.

Features:

- Keycloak login
- Logout
- Session handling
- Identity summary
- Chat
- Multilingual questions
- Optional response-language selection
- Citations
- Deterministic notices
- Tool status
- Pending approvals
- Authorization trace
- ACL administration for authorized security users

## 22.1 Localization

Support:

- English
- German
- Turkish

Frontend locale must not affect retrieval authorization or restrict the language of queries.

## 22.2 Normal trace

Display:

- Authorized tool activity
- Safe status information
- Authorized citations
- Policy-approved notices
- Correlation ID

## 22.3 Privileged trace

Users with `diagnostics.read`, including Tom, may view permitted sanitized diagnostics:

- Policy decisions
- Internal reason codes where safe
- Tool outcomes
- API status
- Retrieval diagnostics
- Redacted audit metadata

Do not expose protected document contents through diagnostics.

## 22.4 Playwright authorization tests

Test:

- Tom can access privileged trace.
- Anna cannot access privileged trace.
- Sarah can administer permitted ACLs.
- Sarah cannot read protected content.
- Lena does not automatically receive auditor visibility.
- User switching does not expose previous user state.

---

# 23. PRIMARY DEMONSTRATION SCENARIOS

## Scenario A — Customer and Credit

Question:

"Summarize customer C-1002, the related credit application and available internal documents."

Personas:

- Anna
- Mehmet
- Max
- Lena

Demonstrate:

- Branch-based customer access
- Assigned-application customer access
- Credit-specific permissions
- Sensitive-field masking
- Restricted documents
- Safe partial success
- Approval limits
- Four-eyes
- Human confirmation

Use separate seeded credit applications to demonstrate:

- Approval above EUR 50,000 → DENY
- Own prepared application → DENY
- Valid application → requires explicit confirmation

## Scenario B — Human Resources

Question:

"Summarize the employee salary bands."

Personas:

- Sophie
- Lena
- Sarah

Expected:

- Sophie → authorized HR results.
- Lena → no automatic HR content access.
- Sarah → permitted ACL metadata, no content access.

## Scenario C — Multilingual RAG

Ask equivalent questions in:

- English
- German
- Turkish
- Mixed Turkish/English/German

Demonstrate:

- Cross-language semantic matching
- Appropriate response language
- Correct citations
- Identical authorization constraints
- No disclosure through language switching

## Scenario D — Privileged Diagnostics

Personas:

- Tom
- Anna
- Lena

Demonstrate that diagnostics require an explicit permission.

Tom can view permitted sanitized audit and diagnostic information.

Anna cannot.

Lena does not automatically gain all auditor capabilities.

---

# 24. SECURITY INVARIANTS — EXECUTABLE CONTRACT

Create:

`docs/security-invariants.md`

Every invariant must map to at least one automated test.

| ID  | Security invariant                                                                                             |
| --- | -------------------------------------------------------------------------------------------------------------- |
| I1  | LLM cannot authorize operations.                                                                               |
| I2  | Downstream APIs independently enforce access.                                                                  |
| I3  | Unauthorized content never reaches model context.                                                              |
| I4  | APIs validate JWT issuer, signature, lifetime and audience.                                                    |
| I5  | DB access fails closed without trusted context.                                                                |
| I6  | Cache and conversation context are principal-isolated.                                                         |
| I7  | Revocation follows documented consistency guarantees.                                                          |
| I8  | Admin roles do not automatically grant business-data access.                                                   |
| I9  | Token exchange cannot create unauthorized business access.                                                     |
| I10 | Prompt injection cannot bypass deterministic authorization.                                                    |
| I11 | State-changing operations require explicit human confirmation.                                                 |
| I12 | ACL inheritance and explicit deny are enforced.                                                                |
| I13 | Confidential resource metadata is not disclosed.                                                               |
| I14 | Runtime DB roles cannot bypass RLS.                                                                            |
| I15 | Secrets and sensitive contents are not exposed through logs.                                                   |
| I16 | Query language cannot change effective permissions.                                                            |
| I17 | Query expansion cannot bypass authorization.                                                                   |
| I18 | Conversation history cannot bypass current access controls.                                                    |
| I19 | Stale ACL metadata follows the fail-closed policy.                                                             |
| I20 | Machine identities cannot exceed configured permissions.                                                       |
| I21 | Group hierarchy matching is segment-aware and directionally correct.                                           |
| I22 | Credit analysts access only customers linked to their assigned applications.                                   |
| I23 | Local canonical ACL changes are not overwritten by filesystem synchronization.                                 |
| I24 | External source ACL authority is respected.                                                                    |
| I25 | Sensitive side-effect tools cannot execute without required confirmation.                                      |
| I26 | Structured business identifiers resolve through authorized APIs rather than unreliable semantic matching.      |
| I27 | Database principal context and assignment projection state do not leak authorization across users or requests. |

For each invariant document:

- Invariant ID
- Threat addressed
- Enforcement layer
- Test name
- Test level
- Expected behavior
- Result
- Known limitations

CI must fail when required security tests fail.

---

# 25. EXECUTABLE AUTHORIZATION MATRIX

Create:

`tests/specifications/authorization-matrix.yaml`

Use it as the independent source of expected authorization outcomes.

Example:

```
cases:
  - id: CUSTOMER_SAME_BRANCH
    principal: anna.berater
    resource: customer:C-1002
    action: customer.read
    expected: allow

  - id: CUSTOMER_OTHER_BRANCH
    principal: anna.berater
    resource: customer:C-5001
    action: customer.read
    expected: deny

  - id: ANALYST_ASSIGNED_CUSTOMER
    principal: mehmet.kredit
    resource: customer:C-1002
    action: customer.read
    expected: allow

  - id: ANALYST_UNASSIGNED_CUSTOMER
    principal: mehmet.kredit
    resource: customer:C-5001
    action: customer.read
    expected: deny

  - id: CREDIT_APPROVAL_OVER_LIMIT
    principal: max.approver
    resource: credit:K-OVER-LIMIT
    action: credit.approve
    expected: deny

  - id: CREDIT_FOUR_EYES
    principal: max.approver
    resource: credit:K-OWN
    action: credit.approve
    expected: deny

  - id: HR_ADMIN_DENY
    principal: lena.admin
    resource: employee:E-100
    action: employee.salary.read
    expected: deny

  - id: AUDITOR_TRACE_ALLOW
    principal: tom.auditor
    resource: diagnostics:authorization-trace
    action: diagnostics.read
    expected: allow

  - id: AUDITOR_ACL_MUTATION_DENY
    principal: tom.auditor
    resource: document:DOC-501
    action: document.acl.manage
    expected: deny
```

Extend this matrix for:

- Tools
- APIs
- Documents
- Database rows
- Group inheritance
- Service accounts
- ACL management
- Diagnostic access
- Approval operations

Expected outcomes must not be generated from implementation behavior.

Tests must read the independently reviewed specification.

---

# 26. UNIT TESTS

Use xUnit.

Test:

- Principal resolution
- Group ancestor expansion
- Segment-aware group matching
- Role-permission mappings
- RBAC
- ABAC
- ACL inheritance
- Explicit deny
- Classification
- Branch restrictions
- Assigned-customer access
- Approval limits
- Four-eyes
- ToolPolicyGate
- Disclosure policies
- Reason-to-notice mapping
- Cache isolation
- Revocation
- ACL versioning
- Embedding input prefixes
- Identifier parsing
- Human approval state transitions
- Projection synchronization logic

Pure unit tests must run without Docker.

Use mocks only where appropriate.

Collect coverage.

---

# 27. INTEGRATION TESTS

Use:

- Testcontainers for .NET
- ASP.NET Core WebApplicationFactory
- Real PostgreSQL
- Real pgvector
- Real Keycloak for identity integration tests

Cover:

- Database migrations
- Keycloak realm import
- Token acquisition
- JWT validation
- Wrong issuer
- Wrong audience
- Expired tokens
- Customer API authorization
- Credit API authorization
- Credit assignment projection
- Branch and relationship RLS
- Connection reuse
- Concurrent principals
- Service accounts
- Token exchange
- ACL administration
- Document ingestion
- Secure retrieval
- Revocation
- Projection synchronization

Use deterministic fixtures.

Tests must not depend on execution order.

---

# 28. BRUNO API TESTS

Bruno is mandatory.

Use Git-versioned API collections.

Suggested structure:

```
tests/api/bruno/
  bruno.json
  environments/
    local.bru
    ci.bru
  auth/
    UserToken.bru
    ServiceToken.bru
    TokenExchange.bru
  customers/
    ReadAllowedCustomer.bru
    ReadDeniedCustomer.bru
    ReadAssignedCustomer.bru
    ReadUnassignedCustomer.bru
  credit/
    ReadApplication.bru
    ApproveWithinLimit.bru
    RejectOverLimit.bru
    RejectOwnApplication.bru
  agent/
    Chat.bru
    PartialSuccess.bru
    ForbiddenTool.bru
  documents/
    ReadAcl.bru
    UpdateAcl.bru
    RestrictedDocument.bru
  rag/
    EnglishQuery.bru
    GermanQuery.bru
    TurkishQuery.bru
    MixedLanguageQuery.bru
    CrossLanguageDenied.bru
  diagnostics/
    AuditorAllowed.bru
    NormalUserDenied.bru
  security/
    MissingToken.bru
    WrongAudience.bru
    InvalidIssuer.bru
    CrossBranchAccess.bru
```

Assert:

- HTTP status codes
- JSON response structure
- Expected fields
- Authorization outcomes
- Field masking
- Denial reason behavior
- Partial tool success
- Deterministic notices
- Cross-language retrieval
- Restricted diagnostic access

Provide:

`make test-api`

Use the verified CLI syntax for the pinned Bruno version.

Do not commit tokens.

Run Bruno against real running APIs.

Include Bruno tests in GitHub Actions.

---

# 29. PLAYWRIGHT E2E TESTS

Test:

- Login
- Logout
- Session handling
- Chat submission
- Multilingual input
- Response-language selection
- Authorization notices
- Citations
- User switching
- Normal trace restrictions
- Tom's privileged trace access
- Sarah's ACL administration
- Credit approval confirmation
- Forbidden operation feedback

Use deterministic fixtures.

Do not expose passwords or tokens through logs or screenshots.

---

# 30. MULTILINGUAL RETRIEVAL EVALUATION

Create:

`tests/evals/multilingual-golden-dataset.json`

Each case must include:

- Test ID
- Principal
- Query
- Query-language classification
- Expected authorized document IDs
- Forbidden document IDs
- Expected facts
- Optional response language

Evaluate:

1. English → German document.
2. Turkish → English document.
3. German → Turkish document.
4. Mixed TR/EN/DE → German document.
5. `user` terminology.
6. `Benutzer` terminology.
7. `kullanıcı` terminology.
8. Turkish special characters.
9. German special characters.
10. Document IDs.
11. Product codes.
12. Unauthorized cross-language access.
13. Secure query expansion.
14. Same query under different UI locales.

Metrics:

- Recall\@K
- Precision\@K
- MRR
- nDCG\@K where useful
- Citation accuracy
- Unauthorized context exposure count

Use real multilingual embeddings.

Do not rely on FakeLLM for semantic quality claims.

---

# 31. OBSERVABILITY AND AUDIT

Use OpenTelemetry.

Capture:

- Request correlation
- Agent execution
- Tool authorization
- Tool execution
- Downstream HTTP calls
- Retrieval duration
- Ingestion duration
- Error status

Do not log:

- Passwords
- Tokens
- Client secrets
- Protected document contents
- Unredacted sensitive personal data

Create structured audit records.

Separate operational logs from security audit data.

Tom's diagnostic permission must not automatically expose restricted business content.

---

# 32. OPTIONAL ENTERPRISE CONNECTORS

Introduce after core security hardening.

Support:

- Filesystem — mandatory
- Nextcloud — optional
- BookStack — optional
- OpenLDAP — optional federation

Implement:

- Source authentication
- Document enumeration
- Content retrieval
- ACL extraction
- ACL normalization
- Change detection
- Deletion detection
- Synchronization
- Freshness validation

External ACLs remain source-authoritative.

Document unsupported permission semantics.

---

# 33. OPTIONAL OPENFGA AND MCP

After the required milestones:

Optionally add OpenFGA for relationship-based authorization.

Example relationships:

- Owner
- Viewer
- Shared-with
- Parent folder

Optionally expose tools through MCP.

MCP must use the same authorization principles.

Do not introduce an authentication or authorization bypass through MCP.

---

# 34. DOCKER COMPOSE

Docker Compose is the canonical runtime.

The completed core platform must start with:

```
docker compose up
```

Optional profiles:

```
docker compose --profile ollama up
docker compose --profile connectors up
docker compose --profile observability up
docker compose --profile openfga up
```

Requirements:

- Pinned images
- Health checks
- Dependency readiness
- Realm import
- Database migrations
- Deterministic seeding
- Internal service networking
- Correct issuer configuration
- Development-only credentials
- Configurable ports
- Persistent model cache
- No mandatory paid API keys

During M1/M2, the embedding service need not run.

From M3 onward, the default stack must include the working local embedding provider.

Keep FakeLLM as the deterministic demonstration provider for agent workflows.

---

# 35. REPOSITORY STRUCTURE

Use this target structure.

```
virtual-bank-agent/
|
+-- PROJECT_SPEC.md
+-- README.md
+-- LICENSE
+-- .gitignore
+-- .env.example
+-- compose.yaml
+-- Makefile
+-- VirtualBank.slnx
|
+-- .github/
|   +-- workflows/
|       +-- build.yml
|       +-- tests.yml
|       +-- docker.yml
|
+-- docs/
|   +-- architecture.md
|   +-- authorization-model.md
|   +-- security-invariants.md
|   +-- threat-model.md
|   +-- multilingual-rag.md
|   +-- api-testing.md
|   +-- demo-scenarios.md
|   +-- local-development.md
|   +-- adr/
|
+-- infra/
|   +-- keycloak/
|   +-- postgres/
|   +-- embeddings/
|   +-- ldap/
|   +-- nextcloud/
|   +-- bookstack/
|   +-- observability/
|
+-- src/
|   +-- VirtualBank.Model/            (modules: Customers, Credit, later Hr, Operations, Documents)
|   +-- VirtualBank.Application/
|   +-- VirtualBank.Infrastructure/
|   +-- VirtualBank.Presentation/
|   +-- VirtualBank.Web/              (API host)
|   |
|   +-- Agent/                          (M2: separate process, same layering)
|   +-- Ingestion/                      (M3: separate process, machine identity)
|   |
|   +-- Frontend/
|       +-- virtual-bank-ui/
|
+-- sample-docs/
|   +-- public/
|   +-- internal/
|   +-- credit/
|   +-- hr/
|   +-- operations/
|
+-- policies/
|   +-- permissions.json
|   +-- openfga/
|
+-- tests/
|   +-- specifications/
|   |   +-- authorization-matrix.yaml
|   |
|   +-- unit/
|   +-- integration/
|   +-- security/
|   +-- evals/
|   |   +-- multilingual-golden-dataset.json
|   |
|   +-- api/
|   |   +-- bruno/
|   |
|   +-- e2e/
|       +-- playwright/
|
+-- scripts/
    +-- setup/
    +-- seed/
    +-- smoke/
    +-- test/
```

Do not create empty folders for optional components that have not been implemented.

---

# 36. DEVELOPER COMMANDS

Provide functional commands:

```
make up
make down
make seed
make test-unit
make test-integration
make test-security
make test-api
make test-e2e
make test-evals
make test-all
make smoke
```

Every command must invoke actual functionality.

Do not create fake successful commands.

---

# 37. MILESTONES

Follow incremental, demo-driven implementation.

## M1 — Identity, Customer/Credit APIs and RLS

Implement:

- Docker Compose
- Keycloak realm
- Nine demo personas
- Roles
- Permissions
- Groups
- Group hierarchy expansion
- Attributes
- Authorization matrix
- PostgreSQL
- Customer API
- Credit API
- Credit assignment projection
- Customer RLS covering branch and assignment access
- Deterministic seed data
- Unit tests
- Integration tests
- Bruno API collection
- Minimal Angular login/API demonstration

Acceptance:

- Anna accesses authorized Stuttgart customers.
- Anna cannot access restricted Munich customers.
- Mehmet accesses assigned customers.
- Mehmet cannot access unassigned customers.
- Runtime RLS fails closed.
- Wrong-audience tokens are rejected.
- Tom's audit role is configured without business-data privileges.

Record an ADR explaining the projection and M4 synchronization plan.

Exit criterion (M1 verification): the CI workflow is green on a machine with registry access —
real `dotnet test`, container builds, Keycloak realm import, smoke and Bruno runs against the stack.

## M1.5 — Architecture Refactoring

Implement before M2, without changing externally visible behavior:

- Credit context split into Domain / Application / Infrastructure / Api.
- Domain: `CreditApplication` aggregate with approval rules (state, four-eyes, limit) and the only
  transition to *Approved*; value objects for money, note text and identifiers.
- Application: CQRS handlers without MediatR, transport-neutral results, repository port,
  credit policies and notice rules. No HTTP or database driver types.
- Approval domain rules may exist; the approval HTTP endpoint stays closed until M6.
- Customer API remains a vertical slice and owns its policies.
- Shared authorization core keeps mechanisms only; `AddResourceServerSecurity` receives the policies of the
  registering service.
- Architecture tests enforce the dependency rules.

Acceptance:

- All M1 tests stay green (authorization matrix, database, store SQL, Bruno).
- Architecture tests pass and fail on a deliberately introduced violation.
- ADR-0006 records the decision and the DI lifetimes.

## M1.5.1 — Hardening after review round 6

- Aggregate invariants (approved ⇔ approver ⇔ approval timestamp, version ≥ 1, positive credit amount)
  identical to the table's CHECK constraints; strongly typed `CreditApplicationId`.
- Machine principals only from the signed `principal_type` claim issued to client-credentials-only clients.
- Readiness verifies issuer, JWKS location and an RS256 signing key.
- Customer API organized per feature; route rules tested on registered endpoints (allowlist).
- Capability rules of modules that do not exist are `planned` matrix cases, denied with `NO_POLICY` at runtime.
- M1.5.2: the machine claim is honored only when `azp` is on `Authorization:MachineClients` (kept equal to the
  realm by a test); blank approvers and undefined status values are rejected by domain and CHECK constraints.

## M1.6 — EF Core Code First with PostgreSQL Security Migrations

As decided in ADR-0007: EF Core migrations as the only schema source (bootstrap SQL keeps databases and roles),
RLS/grants as SQL migrations in the same chain, `customer_migrator` / `credit_migrator` LOGIN roles acting through
the NOLOGIN owner roles, APIs never migrate, explicit transaction carrying the RLS context for EF queries and
`SaveChanges`, Testcontainers integration tests (pooling, concurrency, context leakage, `IgnoreQueryFilters`),
`has-pending-model-changes` in CI, Compose `migrator` service gating the APIs.

Acceptance: all existing suites green against the migrated database; the old schema scripts removed.

## M2 — Agent and Tool Authorization

Implement:

- Agent Service
- FakeLLM
- Tool Registry
- ToolPolicyGate
- Hide/Explain modes
- Partial success
- Deterministic notices
- Audit
- Agent UI integration
- Tests

Acceptance:

For Anna:

- Customer tool executes.
- Restricted Credit tool is denied.
- Credit approval tool is hidden.
- No unauthorized downstream operation executes.

## M3 — Secure Multilingual RAG and ACL Administration

Implement:

- CPU embedding service
- E5 model
- `query:` / `passage:` formatting
- Five document parsers
- Minimum three documents per format
- Multilingual retrieval
- Mixed-language queries
- ACL inheritance
- Group hierarchy
- Canonical ACL store
- Document ACL Administration API
- pgvector
- Secure retrieval
- Lexical/exact document lookup
- Citations
- Evaluations

Acceptance:

- Cross-language retrieval works against the golden dataset.
- Mixed-language queries are supported.
- Forbidden chunks never enter model context.
- Sarah manages permitted ACL metadata without reading protected content.

## M4 — Revocation and Consistency

Implement:

- ACL versioning
- Content versioning
- Synchronization
- Tombstones
- Group revocation tests
- Assignment projection synchronization
- Projection staleness policy
- Cache isolation
- Conversation isolation

Acceptance:

- ACL changes do not unnecessarily trigger re-embedding.
- Local ACL administration is not overwritten by filesystem sync.
- Assignment removal follows documented revocation guarantees.
- Stale projections are handled according to policy.

## M5 — Token Exchange

Implement:

- Keycloak Standard Token Exchange
- Target audience restriction
- Verified identity propagation
- Delegation audit
- Real negative tests

Acceptance:

- Tokens for one API are rejected by unrelated APIs.
- Business permissions cannot be escalated through exchange.

## M6 — Security Hardening, HR and Operations

Implement:

- HR API
- Operations API
- HITL
- Approval limits
- Four-eyes
- Prompt-injection tests
- RLS bypass tests
- All mandatory security invariants
- Extended Bruno tests
- Playwright tests
- Audit/diagnostic UI

Acceptance:

- All mandatory invariant tests pass.
- State-changing actions require confirmation.
- Prompt injection cannot bypass deterministic authorization.
- Tom can view permitted privileged diagnostics.
- Normal users cannot.

## M7 — Optional Enterprise Connectors

Implement:

- Nextcloud
- BookStack
- OpenLDAP
- Source ACL normalization
- Connector synchronization tests

Acceptance:

External permission changes are reflected according to documented freshness guarantees.

## M8 — Optional Advanced Features and Showcase

Optional:

- OpenFGA
- MCP
- Ollama
- Hybrid retrieval improvements
- Reranking
- HNSW evaluation
- Aspire AppHost

Complete:

- README
- ADRs
- Architecture diagrams
- Demo GIF
- CI pipelines
- Coverage reports
- Security matrix
- Interview demonstration script

---

# 38. GITHUB ACTIONS

Implement workflows for:

- Dependency restore
- Backend build
- Frontend build
- Unit tests
- Integration tests
- Keycloak tests
- PostgreSQL RLS tests
- Bruno API tests
- Security regression tests
- Playwright smoke tests
- Multilingual retrieval evaluation where practical
- Coverage reports
- Container builds

Use least-privilege GitHub Actions permissions.

Do not require personal commercial API credentials.

Separate fast deterministic suites from resource-intensive model evaluations.

Publish sanitized test artifacts.

---

# 39. THREAT MODEL

Create:

`docs/threat-model.md`

Cover:

- Assets
- Attackers
- Entry points
- Trust boundaries
- Threats
- Mitigations
- Residual risks
- Assumptions

Include:

- Confused deputy
- Privilege escalation
- Wrong-audience tokens
- Token exchange misuse
- Prompt injection
- Authorized-tool misuse
- Cross-branch access
- Stale assignment projection
- Assignment revocation latency
- Group hierarchy errors
- ACL inheritance errors
- ACL drift
- Stale identity claims
- RAG data leakage
- Cache isolation failures
- Conversation leakage
- Database context injection
- Compromised trusted application
- Audit metadata exposure
- Language-switching authorization bypass
- External connector failures

Include:

"Authorization limits the blast radius of prompt injection; it does not prevent it."

Do not claim that the reference application is automatically production-certified.

---

# 40. README REQUIREMENTS

Explain:

1. Project purpose.
2. Why an enterprise AI agent needs independent authorization.
3. Keycloak identity model.
4. Roles versus permissions.
5. RBAC, ABAC and ACL.
6. Group hierarchy.
7. Branch and assigned-customer access.
8. Credit assignment projection.
9. PostgreSQL RLS.
10. RLS threat-model limitations.
11. ToolPolicyGate.
12. Partial success.
13. Deterministic notices.
14. Token exchange.
15. Secure multilingual RAG.
16. Mixed-language query processing.
17. Exact identifiers versus semantic retrieval.
18. ACL source-of-truth decisions.
19. Revocation and synchronization.
20. Prompt-injection limitations.
21. Human approval.
22. Auditor diagnostics.
23. Bruno API testing.
24. Unit/integration/security/E2E testing.
25. Docker Compose setup.
26. GitHub Actions.
27. Demo scenarios.
28. Known limitations.
29. Architectural decisions.

README must be in English.

A short German interview summary may be included as an explicitly localized section.

Describe the work as a personal project.

Suggested introduction:

"As a personal project, I designed and developed a permission-aware enterprise AI platform using .NET, Keycloak and PostgreSQL..."

Never imply production banking experience that did not occur.

---

# 41. DEFINITION OF DONE

A milestone is complete only when:

1. Source code compiles.
2. Required containers start.
3. Required health checks pass.
4. Database migrations succeed.
5. Seed data is reproducible.
6. Endpoints operate correctly.
7. Authorization is enforced.
8. Unit tests pass.
9. Integration tests pass.
10. Relevant Bruno tests pass.
11. Relevant security tests pass.
12. Relevant frontend tests pass.
13. Documentation matches actual behavior.
14. No production secrets are committed.
15. The milestone can be demonstrated without editing source code.

Do not treat generated source files as evidence of completed functionality.

---

# 42. IMPLEMENTATION INSTRUCTIONS

Do not start another lengthy architecture discussion.

This specification is the binding implementation contract.

First create:

1. `PROJECT_SPEC.md`
2. `docs/architecture.md`
3. `docs/authorization-model.md`
4. `docs/security-invariants.md`
5. `docs/threat-model.md`
6. `tests/specifications/authorization-matrix.yaml`

Keep the architecture documents concise but precise.

Then implement M1 immediately.

M1 implementation tasks:

1. Create the .NET solution.
2. Create the authorization core.
3. Create the Keycloak realm.
4. Seed all nine identities.
5. Configure groups, roles and attributes.
6. Implement group hierarchy expansion.
7. Configure PostgreSQL.
8. Create database migrations.
9. Implement Customer API.
10. Implement Credit API.
11. Create deterministic Credit assignment seed data.
12. Create the Customer `credit_assignments` projection.
13. Implement branch and assignment-based RLS.
14. Implement sensitive-field masking.
15. Create Docker Compose.
16. Implement unit tests.
17. Implement integration tests.
18. Implement Bruno API collections.
19. Implement minimal Angular authentication and API interaction.
20. Build and execute the system.
21. Fix failures before proceeding.

Record an ADR for the assignment projection and planned synchronization strategy.

At the end of M1, report:

- Files created
- Features implemented
- Architecture decisions
- Commands executed
- Actual test outcomes
- Known limitations
- Incomplete features
- Next exact implementation steps

Do not attempt to complete every future service and optional integration through one unverified code-generation operation.

---

# 43. FINAL SUCCESS CRITERION

The completed GitHub repository must demonstrate a real, working permission-aware enterprise AI agent.

It must combine:

- Keycloak authentication
- Human and machine identities
- Granular permissions
- Group hierarchy
- RBAC
- ABAC
- Document ACLs
- Fine-grained customer relationships
- Credit assignment projection
- PostgreSQL RLS
- Secure multilingual RAG
- Cross-lingual semantic retrieval
- Mixed-language queries
- Policy-aware tool calling
- Independent downstream API authorization
- Token exchange
- Revocation and synchronization
- Human-in-the-loop
- Four-eyes restrictions
- Prompt-injection risk mitigation
- Deterministic authorization notices
- Safe diagnostic traces
- Unit tests
- Integration tests
- Bruno API tests
- Playwright tests
- AI retrieval evaluations
- GitHub Actions
- Docker Compose

Every significant security claim must have automated verification.

The repository must explain not only how the technologies work together, but also why each security boundary exists, what it protects against and what limitations remain.

**Authorization is not a prompt instruction. It is a consistently enforced, independently tested architectural property.**

**This repository is a personal engineering reference implementation demonstrating secure enterprise AI integration and software architecture skills.**

---

# 45. AMENDMENT LOG

| Version | Change | ADR |
| ------- | ------ | --- |
| 1.0.1 | Repository name `secure-multilingual-rag-agent` (superseded by 1.0.2). | — |
| 1.0.2 | Historical BoundedAccess product naming; superseded by VirtualBank in 1.0.8. | — |
| 1.0.8 | Rename code, solution, frontend and repository to VirtualBank; keep the virtual-bank demo realm. No AI functionality added. | ADR-0010 |
| 1.0.7 | One layered API with Customers/Credit modules, single audience, Virtual Bank demo realm. | ADR-0009 |
| 1.0.6 | M1 web UI (Angular, PKCE, same-origin proxy, layer rules, browser tests). | ADR-0008 |
| 1.0.5 | M1.5.2: trusted machine-client allowlist; blank approver / undefined status rejected. | — |
| 1.0.4 | M1.5.1 hardening; milestone M1.6 (EF Core Code First + RLS migrations, migrator identities). | ADR-0006 amendment, ADR-0007 |
| 1.0.3 | Client IDs `virtual-bank-ui` / `virtual-bank-cli`, DB owners per service, configurable realm/client IDs; milestone M1.5 (Credit Clean Architecture + CQRS, Customer Vertical Slice, policies per context, architecture tests). | ADR-0006 |
| 1.0.1 | M1 permission-aware RLS context: the API sets `app.principal_id`, `app.branch` and `app.permissions` (comma-separated canonical permission IDs) transaction-locally. RLS policies require the permission **and** the branch/assignment condition. | ADR-0005 |

## M2/M3 implementation supplement (2026-11-22)

The latest implementation adds tool visibility restrictions, non-executable model write tools, scoped conversation history, and a **source implementation** for BGE-M3 + pgvector-secured multilingual document search. **Do not treat these features as independently verified**: see [`docs/M2_M3_IMPLEMENTATION.md`](docs/M2_M3_IMPLEMENTATION.md) for exact commands, remaining limitations, and the distinction between code, fake-model testing and live integration. In case of conflict with historical M1/M2 text, the latest ADR and this supplement govern current runtime status.



## M3 source-code security remediation amendment (2026-11-22)

The latest source patch corrects M2/M3 compile errors, role/matrix drift, service-account API audience,
HR clearance and document authorization invariants. Document retrieval has two mandatory enforcement
layers: PostgreSQL RLS **before** top-K and Application `DocumentReadPolicy` **after** the database's
allow-checked candidate projection but **before** model context. PDF page citations and ingestion
embedding-skip checks are included. The patch remains **unverified with actual .NET/NuGet, PostgreSQL
pgvector and real embedding/LLM runtimes**. This paragraph supersedes contradictory implementation
status claims elsewhere in this historical specification. See `docs/M3_SECURITY_REVIEW_FIXES.md`.
