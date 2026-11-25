> **Historical ADR:** its original `qwen2.5:3b` default was subsequently superseded by the current `qwen3:4b-instruct` + `bge-m3` local starter. See [README](../../README.md).

> **Updated by [ADR-0014](0014-human-confirmed-changes-and-core-banking-operations.md) (2026-11-25):** self-service is no longer read-only: transfers, transfers between own accounts, standing orders and credit applications exist, each behind a confirmation card.

# ADR-0013 — Real local model by default and subject-bound customer self-service

Status: **Implemented in source; integration verification pending**. Supersedes ADR-0011's runtime default.

## Decision

- Default: `Ollama` with a real tool-capable `qwen2.5:3b` model using Semantic Kernel. No silent keyword-router fallback; if the model is missing, return `ASSISTANT_MODEL_UNAVAILABLE`.
- Explicit `ASSISTANT_PROVIDER=Offline`: deterministic keyword router for narrow tests ONLY, not AI/RAG.
- OpenRouter is an opt-in external LLM; prompts and authorized tool outputs leave the local system.
- `GET /api/assistant/capabilities?lang=tr|de|en` produces examples from actual model-visible tool permissions. `platform-admin` can use `describe_my_access` but cannot read customer/credit data.
- `bank-customer` is not `customer-advisor`: verified JWT `sub` binds to a single `customer.customers.identity_subject_id` row, checked by independent Application policy and FORCE RLS. Self tools take **no customer ID**.
- Read-only Customer self-service: profile/IBAN, balances, scheduled payments, booked transactions, standing orders. No real transfers, payment initiation, banking credentials, wire instructions or approvals.
- Newly seeded `deniz.customer` is customer C-2001, `thomas.customer` is C-5001, with separate accounts and payment data. This tests identity isolation even when both have MUC branch.
- In-memory conversation replay uses prior **user messages** only. It deliberately does not replay old model-generated financial answers, which could be stale or authorization-revoked.

## Threat model

The model may choose a bad tool or hallucinate data. Authorization, row ownership, monetary aggregation, date filtering and tool schema validation are C# / SQL decisions, never model decisions. RLS assumes the server holds trusted runtime credentials. `Authorization limits the blast radius of prompt injection; it does not prevent it.`

## Known limitations

- Real Ollama requires a one-time model pull and sufficient RAM; no C# compiler or Docker engine is provided in the authoring container for independent compilation/E2E.
- `get_document_excerpt` and `/api/documents/{id}/excerpt` return policy-checked text excerpts, NOT original PDF/DOCX download. No standing-order creation, transfers or payment authorization. Document search remains via `search_documents`.
- Payments are synthetic demo fixtures; scheduled due dates are initially relative to database initialization.
- The personal data is synthetic; amounts do not represent net worth or physical cash. No investment advice functionality.
