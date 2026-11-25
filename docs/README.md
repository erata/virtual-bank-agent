# Documentation index

## Current reference

| Document | What it covers |
|---|---|
| [Architecture](architecture.md) | Layers, modules, request flow, diagrams |
| [Banking operations](BANKING_OPERATIONS.md) | What each role can do, confirmation cards, money-movement rules, credit lifecycle |
| [Authorization model](authorization-model.md) | Personas, permissions, resource policies, RLS session context |
| [Security invariants](security-invariants.md) | Invariants with enforcement points and tests |
| [Threat model](threat-model.md) | Threats, mitigations, accepted limitations |
| [Design patterns](design-patterns.md) | Patterns used and where to find them |
| [Role document access](ROLE_DOCUMENT_ACCESS.md) | Which persona may read which sample document |
| [RAG evaluation](RAG_EVALUATION.md) | Retrieval metrics, golden set, release gate, latest result |
| [Local setup](LOCAL_SETUP.md) | Prerequisites, start scripts, troubleshooting |
| [Local and cloud model profiles](LOCAL_AND_CLOUD_MODEL_PROFILES.md) | Ollama / OpenRouter / OpenAI configuration |
| [First run with providers](GITHUB_FIRST_RUN_PROVIDERS.md) | First run after cloning, per provider |
| [EF Core and AI providers](EF_CORE_AND_AI_PROVIDERS.md) | EF Code First, migrations, provider wiring |
| [EF snapshot verification](EF_SNAPSHOT_VERIFICATION.md) | Drift checks before a release |
| [Validation status](VALIDATION_STATUS.md) | What was executed for this revision and what was not |
| [Publication quality gate](PUBLICATION_QUALITY_GATE.md) | Checklist before publishing |
| [Architecture decision records](adr/) | ADR-0001 … ADR-0014 |

Diagrams (SVG renders and editable DOT sources) are in [assets](assets/).

## Historical records

These documents record earlier milestones, reviews and patches. They are kept for traceability and may describe
defaults, test counts or limitations that no longer apply; the current reference above takes precedence.

[Review findings](REVIEW_FINDINGS.md) · [R3 follow-up review](R3_FOLLOWUP_REVIEW.md) ·
[M2/M3 implementation](M2_M3_IMPLEMENTATION.md) · [M3 security review fixes](M3_SECURITY_REVIEW_FIXES.md) ·
[Security dependency upgrade 2026-11-22](SECURITY_DEPENDENCY_UPGRADE_2026-11-22.md) ·
[Release notes: cross-platform RAG](RELEASE_NOTES_CROSS_PLATFORM_RAG.md) · [Publication polish](PUBLICATION_LAST_POLISH.md) ·
[NL/RAG UX patch validation](NL_RAG_UX_PATCH_VALIDATION.md) · [Offline vs RAG formatting](OFFLINE_VS_RAG_AND_FORMATTING.md) ·
[Semantic assistant balances](SEMANTIC_ASSISTANT_BALANCES.md) · [Self-service on Windows](SELF_SERVICE_WINDOWS.md) ·
[OpenRouter-only on Windows](OPENROUTER_ONLY_WINDOWS.md) · [Validation status v3](VALIDATION_STATUS_V3.md)
