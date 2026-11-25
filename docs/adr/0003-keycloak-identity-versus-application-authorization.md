# ADR-0003: Keycloak provides identity; the application decides resource access

- Status: Accepted
- Date: 2026-11-21

## Context
Keycloak can model roles, groups and even fine-grained Authorization Services (UMA). Resource-level
rules here depend on business data (branch of a customer, assigned analyst, application amount,
preparer), which Keycloak does not own.

## Decision
Keycloak issues identity and coarse entitlements: realm roles, full-path groups and user attributes
(`branch`, `clearance`, `approvalLimit`, `employmentStatus`). Roles are mapped to permissions in
`policies/permissions.json`. Resource decisions are made by `VirtualBank.Authorization` policies and
mirrored by PostgreSQL RLS. Keycloak Authorization Services are not used.

## Alternatives
- Permissions as Keycloak composite/client roles in the token: larger tokens and the same rules
  still need business data.
- Keycloak Authorization Services: powerful, but moves business-data checks into an external policy
  store and hides the logic this project wants to demonstrate.
- OpenFGA: planned as an optional ReBAC provider (M8) once the local engine is complete.

## Consequences
Roles never appear in business code; permissions and resource conditions do. Disabling a user
through `employmentStatus` removes all permissions without changing role assignments.
