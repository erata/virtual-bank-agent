# Threat model

Scope: the reference implementation described in [`PROJECT_SPEC.md`](../PROJECT_SPEC.md).
It is a personal engineering reference, **not** a production-certified banking system, and it
contains only synthetic data.

> **Authorization limits the blast radius of prompt injection; it does not prevent it.**

## Assets

Customer master data and accounts, credit applications and decisions, salary data, confidential
documents, authorization audit records, signing keys and client secrets, conversation history.

## Principals and trust boundaries

| Boundary | Trusted side | Untrusted side |
| --- | --- | --- |
| Browser → APIs/Agent | validated JWT | everything in the request body, including claimed branch or role |
| Agent → LLM | deterministic gate and retriever | model output, retrieved document text, tool output |
| Agent → APIs | each API's own token validation and policy | the agent's claims about the user |
| API → PostgreSQL | RLS policies | the SQL the API issues |
| Ingestion → sources | canonical ACL store | document content (may contain injection) |

## Threats and controls

| Threat | Control | Status |
| --- | --- | --- |
| Horizontal escalation (Anna reads a Munich customer) | Branch ABAC in policy and RLS | ✅ verified (U, D) |
| Relationship abuse (analyst reads unassigned customers) | Assignment rule in policy and RLS; projection rows visible only to their analyst | ✅ verified (U, D) |
| Vertical escalation (admin reads salaries) | Permission catalog separates admin from business permissions | ✅ verified (U) |
| Database context injection (client sends `branch`) | Context built only from validated token, transaction-local | ✅ RLS side verified (D); API side pending |
| Connection-pool context leakage | `set_config(..., true)` inside explicit transactions | ✅ verified on one connection (D); concurrency test pending |
| Runtime role bypasses RLS | `NOBYPASSRLS`, non-owner runtime roles, `FORCE ROW LEVEL SECURITY` | ✅ verified (D) |
| Cross-service DB access | Database-per-service, `CONNECT` revoked from `PUBLIC` | ✅ verified (D) |
| Unauthorized approval (over limit, own application, forged approver) | Policy + RLS `UPDATE ... WITH CHECK`; optimistic concurrency | ✅ verified (U, D) |
| Wrong-audience or forged tokens | JwtBearer issuer/audience/lifetime validation | ⏳ API work |
| Confused deputy (agent's own power used for the user) | M2: the in-process assistant has no identity of its own; every tool runs with the caller's `PrincipalContext`, captured server-side. M5: token exchange for a separate agent process | ✅ M2 (U) / ⏳ M5 |
| Prompt injection triggers a forbidden tool | `ToolPolicyGate` + use-case policies + RLS; undeclared arguments dropped; call budget | ✅ verified with scripted hostile models (U); ⏳ LLM evals |
| **Authorized-tool misuse** through injection | Write tools only prepare single-use confirmation cards that the same user must confirm; no data-egress tools; every confirmation re-authorizes | ✅ (U + live) |
| Unauthorized RAG retrieval, also via language switch or query expansion | Security trimming in the query | ⏳ M3 |
| Confidential metadata disclosure (titles, counts, notices, traces) | Disclosure modes, separate privileged trace (`diagnostics.read`) | core ✅ / ⏳ UI |
| Stale identity claims after revocation | 5-minute access tokens, refresh revalidation | ⏳ M4 |
| **Stale assignment projection** keeps access after reassignment | M1: seed only — *no revocation guarantee yet*. M4: synchronization with freshness policy | ⚠️ known gap |
| ACL drift / stale external ACLs | Versioned canonical ACL, freshness threshold, fail closed | ⏳ M4/M7 |
| Data egress to the model provider (OpenRouter) | Local Ollama is default (no cloud egress); OpenRouter/OpenAI are explicit opt-in with synthetic data only, keys from environment and HTTPS endpoints | ⚠️ accepted for the demo — production needs a DPA, EU hosting or a local model |
| Model output presented as authoritative | Notices and tool trace computed by application code and shown next to the answer; answer rendered as text only | ✅ (U, E) |
| Cache or conversation leakage across users | Principal- and freshness-scoped keys | ⏳ M4 |
| Excessive audit disclosure | Sanitized audit, separate `audit.read`/`diagnostics.read` | ⏳ |
| Development credentials reused elsewhere | All secrets are labeled dev-only; realm and SQL must be re-keyed outside local use | documented |

## Residual risks and assumptions

- **Compromised trusted application:** an attacker controlling the API process (which holds both module DB logins since ADR-0009) can set a
  false RLS context. RLS defends against programming mistakes, not against this case.
- The **password grant client** `virtual-bank-cli` exists only for automated local tests
  ([ADR-0004](adr/0004-development-only-password-grant-client.md)); it must not exist in any shared environment.
- `sslRequired=external` and plain HTTP are acceptable only on a developer machine.
- Keycloak itself, the container runtime and the host OS are trusted.

## Out of scope

Network-level attacks, denial of service, physical security, supply-chain verification of
container images beyond version pinning, and regulatory certification.
