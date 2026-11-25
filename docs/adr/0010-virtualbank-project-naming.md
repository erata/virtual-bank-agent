# ADR-0010 — VirtualBank Project Identity

**Status:** Accepted (2026-11-22)

## Context

The personal portfolio repository used another product name, while the fictional bank
was named Virtual Bank. The author wants a single recognizable product name in source code.

## Decision

Use `VirtualBank` for the .NET solution, assemblies, namespaces and code-level identity.
Use `virtual-bank-agent` for the repository, `virtual-bank-ui` for the Angular application,
`virtual-bank-api` for the business API audience and `virtual-bank` for the demo Keycloak realm.
Keep the same synthetic personas, authorization rules, database roles and RLS policies.
The organization in seed data is fictional and also named Virtual Bank.

## Consequences

The code is now explicitly branded as the fictional bank's agent platform, rather than as a
sector-neutral reusable product. A future multi-tenant platform could restore a separate product
identity without changing security semantics. No agent runtime, tool calling, embedding model or
vector index is implemented by this rename; these remain later milestones.

The original layered ZIP's solution file still contained paths from the former split-API
architecture. During this rename `VirtualBank.slnx` was rebuilt from the 13 current project files
so the solution references the actual layered source and test projects.
