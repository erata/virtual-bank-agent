# ADR-0012: Local Semantic Kernel and RLS-secured multilingual RAG

Status: **Accepted for implementation, live validation pending** (2026-11-22)

- Retain the five-layer modular monolith; new Assistant and Documents features live inside existing Model/Application/Infrastructure/Presentation projects.
- Semantic Kernel is used only as an Infrastructure adapter and connects to either OpenRouter or local Ollama OpenAI-compatible function calling. No business logic in plugin registration.
- ToolPolicyGate is always authoritative for coarse permissions; existing Application handlers and PostgreSQL RLS remain authoritative for resource rows. Hide mode excludes unauthorized tools before the model sees their schema. Mutations remain disabled until a real HITL protocol is added.
- Use Ollama BGE-M3 **1024 dimensions**, not E5 (384). pgvector retrieves matching authorized chunks; clearance, group/role/user ACL, explicit deny and branch restriction are tested in the database.
- `sample-docs/manifest.json` seeds initial ACL on first import. PostgreSQL ACL rows thereafter remain canonical. Reindexing content must not reset administrative ACL edits.
- Existing schema provision remains SQL until the planned EF migration transition. Never run both SQL bootstrap schema and EF migrations against the same tables independently.
- Explicit limitations: no full folder ACL inheritance, no external connector ACL sync, no persistent conversation storage, no OCR; these need additional ADRs before production use.
