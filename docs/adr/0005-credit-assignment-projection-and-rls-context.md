# ADR-0005: Credit assignment projection and permission-aware RLS context

> **Updated by [ADR-0014](0014-human-confirmed-changes-and-core-banking-operations.md) (2026-11-25):** `credit_approval` is now a LOGIN role used by the approval executor after an explicit, single-use confirmation; decisions include rejection.

- Status: Accepted
- Date: 2026-11-21

## Context
Credit analysts may read customers linked to applications assigned to them. Assignments belong to
the Credit domain, while customers live in `customer_db`. A synchronous Customer → Credit call per
read would couple availability, require an extra service identity and keep the rule out of RLS.

## Decision
1. `customer.credit_assignments(customer_id, analyst_id)` is a read-only **projection** in
   `customer_db`. In M1 it is generated from the same canonical fixture as the Credit data
   (`tools/VirtualBank.SeedGenerator`; CI runs `--check`). `analyst_id` is the stable Keycloak subject ID.
2. Two distinct permissions select the access path: `customer.read` (branch) and
   `customer.read.assigned` (assignment). The client cannot choose the path; it follows from the
   caller's permissions.
3. The API sets the trusted context transaction-locally: `app.principal_id`, `app.branch`,
   `app.permissions` (comma-separated permission IDs) and, in `credit_db`, `app.approval_limit`.
4. Every RLS policy requires the permission **and** the row condition. Projection rows are visible
   only to their own analyst. Missing context matches no policy (fail closed).
5. Synchronization (assignment created, removed, reassigned; duplicates; failures; staleness policy)
   is implemented in M4, preferably with a transactional outbox in `credit_db` and an idempotent
   projector. Kafka is not introduced for this.

6. Least privilege for state changes: only the role `credit_approval` (used by the M6 approval executor,
   NOLOGIN until then) may `UPDATE` credit applications. The Credit API runtime role cannot.

## Consequences
- The rule is enforced twice (policy and RLS) and verified by both test suites.
- **Until M4, a removed assignment is not revoked in the projection.** This is a documented gap in the
  threat model, not a hidden one.
- `customer.read.assigned` extends the permission catalog of PROJECT_SPEC §5.3 (amendment 1.0.1).
