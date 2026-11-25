# ADR-0001: Docker Compose as the canonical runtime

- Status: Accepted
- Date: 2026-11-21

## Context
Reviewers must be able to start the platform without installing the .NET SDK, Node or Keycloak.
Aspire could orchestrate the services too, but two orchestration definitions drift apart over time.

## Decision
`compose.yaml` is the single runtime definition. Optional infrastructure (Ollama, connectors,
observability, OpenFGA) is added through Compose profiles. Aspire is not used; the standalone Aspire
Dashboard image may be used later only as an OTLP trace viewer.

## Alternatives
- Aspire AppHost as primary runtime: good developer experience, but requires the .NET toolchain and
  duplicates environment configuration.
- Kubernetes manifests: realistic but too heavy for a local reference project.

## Consequences
Every environment variable lives in one place. Clean Architecture is unaffected: it describes code
inside services, not how processes are started.
