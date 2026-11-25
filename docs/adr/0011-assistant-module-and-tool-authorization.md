> **Implementation update (2026-11-23):** The accepted in-process Semantic Kernel adapter remains the architecture, but the *historical M2 details below* refer to an earlier implementation. Current public-starter defaults are local Ollama `qwen3:4b-instruct` (chat) and `bge-m3` (embedding); OpenRouter/OpenAI remain optional alternatives. Offline is test-only, not the default. Self-service tools and document tools have been added. Mutating and approval tools are not model-visible pending human confirmation. The current list is `AssistantToolCatalog.cs`, the current deployment shape is `docs/architecture.md`.

> **Updated by [ADR-0014](0014-human-confirmed-changes-and-core-banking-operations.md) (2026-11-25):** write tools now prepare confirmation cards (risk `PreparesConfirmation`); the model still cannot execute anything.

# ADR-0011: Assistant module with Semantic Kernel tool calling inside the API

- Status: Accepted
- Date: 2026-11-22
- Amends: PROJECT_SPEC §0 and §11 (agent as a separate process on Microsoft Agent Framework)

## Context

VirtualBank continues an earlier lab of the author (RBAC-Agent / RAG-Lab): a .NET prototype of a role-based
assistant with Semantic Kernel, OpenRouter (`gpt-4o-mini`), tool calling with authorization and pgvector RAG with
an `access_level` filter. M2 brings the tool-calling part into this repository, on top of the M1 use cases,
Keycloak identities and row-level security that the lab did not have.

The specification planned the agent as a separate process on Microsoft Agent Framework. That design needs
token exchange (M5) to call the API on behalf of the user. For M2 the simpler and equally strict option is
available: the assistant runs in the same process and calls the **same use-case handlers** with the **same
trusted `PrincipalContext`** as the HTTP endpoints.

## Decision

**A fourth module, `Assistant`, in the existing layers.**

```text
Application/Assistant/   AskAssistantHandler (command), AssistantToolCatalog, ToolPolicyGate, ToolSession,
                         AssistantPolicies (assistant.use), IAssistantRuntime / IAssistantDiagnostics ports
Infrastructure/Assistant/ SemanticKernelAssistantRuntime (OpenRouter), OfflineAssistantRuntime, options
Presentation/Assistant/   POST /api/assistant/chat
```

1. **Capability gate.** `assistant.use` (advisor, analyst, approver) plus a human principal. Service accounts are
   denied with `PRINCIPAL_TYPE_NOT_ALLOWED` even if a role mapping error granted the permission.
2. **Tools are adapters over use cases.** Seven tools; each calls an existing handler. The assistant has no
   repository access, so it can never see more than the same user sees through the API. Architecture tests
   enforce this (`Assistant_reaches_modules_only_through_application_use_cases`).
3. **`ToolPolicyGate`, fixed order:** call budget → known tool → permission → risk tier → write budget →
   arguments (declared parameters only, anchored identifier patterns, length limits). It is a coarse pre-check;
   the handler still applies the resource policies and PostgreSQL re-applies them with RLS.
4. **Risk tiers.** `Read`; `LowRiskWrite` (`add_credit_note`: append-only, audited, one per turn);
   `RequiresHumanConfirmation` (`approve_credit_application`: listed so the model can route the request, never
   executed — the M6 pending-action workflow will own it).
5. **Partial success with deterministic notices.** Each tool call is recorded (`SUCCEEDED`, `NOT_FOUND`, `DENIED`,
   `INVALID`, `FAILED`); notice codes come from the gate or the use case, never from model text. `NOT_FOUND`
   carries no notice, so "not visible" stays indistinguishable from "does not exist".
6. **Two runtimes behind `IAssistantRuntime`.**
   - `Offline` (default): deterministic keyword/identifier routing in EN/DE/TR. No network, no key; used for the
     demo, the browser tests and Bruno.
   - `OpenRouter`: Semantic Kernel OpenAI connector with the OpenRouter endpoint, `openai/gpt-4o-mini`,
     temperature 0, automatic function calling, a fresh kernel per turn whose only plugin is the turn's
     `ToolSession`, and an `IAutoFunctionInvocationFilter` as hard stop for the model loop.
7. **Tool schemas are not secrets.** All tools are offered to the model; authorization happens at invocation.
   Offering only permitted tools would hide denials and let the model guess; offering all of them makes the
   denial explicit and deterministic.

## Why Semantic Kernel and not Microsoft Agent Framework (for now)

Semantic Kernel is what the earlier lab used, and it provides exactly what M2 needs: function calling with
filters, an OpenAI-compatible connector and a small surface. Microsoft Agent Framework is the designated
successor for multi-agent orchestration. The runtime adapter is about 150 lines behind `IAssistantRuntime`; the
gate, tools, notices and tests do not reference Semantic Kernel, so a later switch is local to Infrastructure.

## Consequences

- The claim "the LLM never decides authorization" is now executable: scripted hostile "models" in
  `AssistantTurnTests` request approvals, other users' data, malformed identifiers, unknown tools and endless
  loops; every case is stopped by application code. Mutation checks confirm the gate tests fail when a gate
  rule is removed.
- With `Provider=OpenRouter`, the user's message and **masked, synthetic** tool results leave the system and go
  to a third-party provider. That is acceptable for this demo only; a real bank would need a data-processing
  agreement, EU hosting or a local model. The default stays `Offline`.
- Single-turn only: no conversation memory yet (I6, I18 remain M4). No streaming.
- Prompt injection: authorization does not depend on the prompt, so injection cannot widen access. What it can
  still do — make the model *say* misleading things — is limited by showing application-computed notices and the
  tool trace next to the answer. LLM evaluations are not yet part of CI.
- The Semantic Kernel package version (`1.60.0`) could not be restored in the authoring sandbox; the adapter was
  compiled against API stand-ins only. CI must confirm restore, build and `SemanticKernelRuntimeTests`.
