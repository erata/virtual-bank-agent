> **Historical design — partially superseded by ADR-0009 and ADR-0011.** The current runtime has one business API with an in-process Assistant; do not use this page to infer current service topology. See `docs/architecture.md`.

# ADR-0002: Split services along trust boundaries, not for scale

- Status: Accepted
- Date: 2026-11-21

## Context
The project must show that every resource server protects itself even when the agent is wrong.
That requires separate processes with separate token audiences. A full microservice landscape
would add operational noise without adding security evidence.

## Decision
Separate processes exist only where identity or trust differs: Agent Service (acts on behalf of
users), Ingestion Worker (machine identity), one API per business domain (own audience, own
database role), UI. The Agent Service is internally a modular monolith. Small APIs use vertical
slices; the authorization core uses a hexagonal design with no framework dependencies.

## Consequences
- Bypass tests are meaningful: disabling the agent gate still leaves the API's own checks.
- One PostgreSQL server hosts one database per service with distinct runtime roles.
- A shared authorization library is a deliberate shared kernel; services still enforce independently.

## Amendment (ADR-0009)

Customer and Credit no longer run as separate processes: they share the same trust level and are modules of one
layered API. Their separation is kept where it carries security weight — one database and one least-privilege DB
login per module, plus module-boundary tests. The Agent Service and the ingestion worker stay separate processes.

