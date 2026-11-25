# Authorization model — current source and design intent

> This is the v4 authorization overview. Actual decisions are encoded in `policies/permissions.json`, module policies under `src/VirtualBank.Application`, seeded Keycloak identities and PostgreSQL RLS migrations. **Live claim/RLS verification is still required** for the published commit; see [release gate](PUBLICATION_QUALITY_GATE.md).

## Identity is not access

Keycloak authenticates the subject and issues roles, groups, branch, clearance and other attributes in an audience-restricted JWT. ASP.NET Core validates signature, issuer, audience and expiry before `PrincipalResolver` constructs a `PrincipalContext`. Application permissions are derived using `PermissionCatalog` from `policies/permissions.json`. A role name alone does not authorize reading a specific customer's bank data.

## Personas and business constraints

| Persona | Role | Example authorized scope | Not implied |
| --- | --- | --- | --- |
| Anna `anna.berater` | `customer-advisor` | customers in verified branch STR; branch cash desk (deposits/withdrawals) | All branches, unassigned credit applications |
| Mehmet `mehmet.kredit` | `credit-analyst` | assigned credit applications and associated customers; add notes | HR policy, all customers, credit decisions |
| Max `max.approver` | `credit-approver` | approve/reject submitted applications within limit, four-eyes, after confirming | Autonomous AI approval, own applications |
| Sophie `sophie.hr` | `hr-specialist`, restricted clearance | permitted HR document ACL | Customer and credit business records |
| Oliver `oliver.ops` | `operations-operator` | operations documents | HR salary bands |
| Lena `lena.admin` | `platform-admin` | configured system capabilities | Business-data bypass |
| Sarah `sarah.security` | `security-admin` | document ACL administration | Read private document contents |
| Tom `tom.auditor` | `auditor` | permitted audit metadata | Customer/HR records by default |
| Deniz `deniz.customer` | `bank-customer` | own accounts, balances, payments, transfers, standing orders, credit applications and PUBLIC documents via verified JWT sub | Thomas' accounts, booking on others' accounts (only crediting via a transfer) |
| Thomas `thomas.customer` | `bank-customer` | own accounts, transfers and PUBLIC documents | Deniz' accounts |
| Meryem, Lucas, Elif, Emilia | `bank-customer` | synthetic own multi-currency accounts | Other bank customers' accounts |
| `svc-ingestion` | machine | document ingestion API after machine audience checks | Customer/Credit reads or chat |

The complete list of synthetic users and stable subject identifiers is `infra/keycloak/realm-virtual-bank.json`; new personas should be added to that file and the canonical seed fixtures rather than invented in tests.

## Resource policies

- `customer.read`: advisor's branch matches the customer branch.
- `customer.read.assigned`: credit analyst has an application assignment relationship in the Customer projection.
- `customer.read.self`: verified subject matches the authoritative `identity_subject_id` of the customer; self-service endpoints accept no arbitrary customer identifier.
- `customer.read-sensitive`: additional permission for sensitive profile fields, only after customer visibility.
- `credit.read`: subject is assigned to the requested application, or approver satisfies its approval-read conditions.
- `credit.approve`: approve or reject (reason required) within the approver's limit, only `SUBMITTED` applications and never one the approver prepared (four-eyes); executed only after the approver confirms a single-use card, under the separate `credit_approval` database role.
- `credit.note.write`: add notes to assigned applications (from the assistant only as a confirmation card).
- `credit.request`: a bank customer applies for credit and reads only the applications they prepared, plus decision notes.
- `customer.payment.write`: a bank customer moves money on their own accounts (SEPA transfers, transfers between own accounts, standing orders, cancelling planned payments); RLS restricts updates to own rows and a CHECK constraint forbids overdrafts.
- `account.cash`: a branch advisor books cash deposits/withdrawals for customers of their own branch.
- `document.acl.manage`: change document ACLs (metadata only, optimistic version check, history); grants no content access.
- `document.read`: requires clearance and a matching document ACL, with explicit deny before allow; branch restrictions also apply. `bank-customer` has the permission but a **public (0) clearance**, so the customer can retrieve public fees/FAQ without gaining internal, credit or HR content.
- `assistant.use`: enables the assistant **but is never a substitute for each individual tool permission**.

The `IResourceAuthorizationService` denies any resource/action lacking a registered active policy. Resource not-visible responses may deliberately avoid revealing whether a record exists. Coarse permission denial and resource hiding need separate, deterministic disclosure decisions.

## Trusted RLS session context

The server begins a transaction using a restricted database role and sets trusted, transaction-local `app.*` values derived from the validated principal. PostgreSQL RLS policies enforce the same permission/resource constraints. Missing or invalid context must return no rows. Application-level row checks are repeated after retrieval; a mismatch is fail-closed and audited. The LLM, Angular UI and browser request cannot set `app.*` values directly.

The database is not a full protection boundary after compromise of the trusted API host, because the host can set the session context. This is an accepted limitation of the one-host modular monolith.

## Security evidence to collect

`tests/specifications/authorization-matrix.yaml` declares expected policy outcomes. `tests/database/rls-tests.sql`, `self-service-rls.sql`, and `scripts/test/document-rls-tests.sh` provide real PostgreSQL checks (authorization, branch, assignment, own account, positive document access, explicit deny). `tests/evaluation/rag_golden.json` adds language- and persona-specific retrieval checks. **These files are not live test results.** Run them against a freshly migrated, initialized Compose stack before publication.
