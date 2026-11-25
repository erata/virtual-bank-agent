> **Historical milestone / patch note.** This file documents an earlier revision and may include obsolete defaults or expected test counts. For the CURRENT reference release, see [`README`](../README.md), [`architecture`](architecture.md), [`quality gate`](PUBLICATION_QUALITY_GATE.md) and [`validation status`](VALIDATION_STATUS.md).

# M2 security hardening + M3 multilingual RAG integration

## Implementation status (2026-11-22)

This repository contains **source code**, not a claim of successful live integration. The authoring environment has no .NET SDK, Docker daemon or PostgreSQL CLI. Static checks are possible; the real backend, Semantic Kernel, Ollama, Keycloak and pgvector have **not** been executed end to end in this environment.

## Implemented source code

- Model-visible **Hide** and **Explain** modes for read-only tool registration (server configuration), with execution-time gate always enforced. Approval and credit-note mutations never become model-visible.
- **Write safety:** model-originated `add_credit_note` now returns a deterministic human-confirmation-required notice. Existing manually authenticated Credit note HTTP endpoint is unaffected. A real pending-action/HITL flow must be built before the assistant can mutate data.
- **Per-principal conversations:** `conversationId` returned to Angular, owner-checked memory store, bounded history; prior tool results are deliberately never replayed. Memory is ephemeral, not persistent across restarts. Revocation is re-evaluated on each new tool invocation.
- **Local real model mode:** Semantic Kernel via Ollama's OpenAI-compatible chat-completions endpoint (`qwen2.5:3b`); OpenRouter remains available. Both use exactly the same ToolPolicyGate. Offline is deterministic and is not a real LLM.
- **Real embeddings:** Ollama `/api/embed` with `bge-m3`, strictly **1024**-dimensional vectors. This is NOT an E5 model; do not add E5 `query:`/`passage:` prefixes.
- **RAG storage:** separate `documents_db`, least-privilege document read/ingest/ACL credentials, `pgvector` cosine `<=>` top-k query, explicit transaction and verified principal context set via `set_config(..., true)`.
- **RLS:** document clearance, branch restriction, `document.read`, allowed user/role/group, explicit-deny precedence. The read-only DB identity cannot bypass FORCE RLS, and does not receive SELECT access to ACL rows.
- **Document ingestion:** 15 actual synthetic documents, 3 each for PDF/DOCX/MD/TXT/JSON, multilingual, canonical manifest with initial ACLs, safe path handling, deterministic overlapping chunks, content-hash detection, versioned content updates, preservation of admin-edited ACLs on reindex.
- **Document ACL HTTP endpoints:** read, effective snapshot, put with optimistic version check, change history. ACL-only role has no access to chunk content.
- **Angular:** sources/citations and owner-specific conversation ID in the assistant page.

## Prerequisites

- Docker Compose with access to container registries
- .NET 10 SDK and real NuGet dependencies
- Ollama models: `bge-m3` (embedding) and optionally `qwen2.5:3b` (real local LLM)
- PostgreSQL pgvector provided by the existing pinned image
- Keycloak developer realm and existing demo personas

## Quickstart (new database volume)

```bash
cp .env.example .env
make up
make ai-up
make ai-pull
make ai-index
make test-rag-db
```

To run the real local Semantic Kernel chat completion provider, configure `.env` *before recreating the API*:

```dotenv
ASSISTANT_PROVIDER=Ollama
ASSISTANT_MODEL=qwen2.5:3b
ASSISTANT_ENDPOINT=http://ollama:11434/v1/
```

Then run `docker compose up -d --force-recreate api`. Login to Angular at `http://localhost:4200` and ask about a policy such as **Welche Kreditrichtlinie gilt für meine Anträge?** The assistant may call `search_documents`, subject to the user ACL.

Alternatively, set `ASSISTANT_PROVIDER=OpenRouter` and `OPENROUTER_API_KEY` in `.env`; only synthetic fixture data should leave the local machine.

**Existing database volumes:** PostgreSQL `/docker-entrypoint-initdb.d` files execute only on the *first* volume initialization. To create `documents_db` for an existing volume, either apply a reviewed one-time SQL migration or run `make reset` (deletes ALL local demo databases) followed by `make up`. Never reset real data.

## Routes

- `POST /api/assistant/chat`: `{ "message": "...", "conversationId": null }` (returns `citations` and `conversationId`)
- `GET /api/documents/search?query=...&topK=5`: authenticated and ACL-filtered semantic search
- `POST /api/documents/ingest`: only the authenticated `svc-ingestion` machine client with `document.ingest`
- `GET /api/documents/DOC-105/acl`: `document.acl.manage` metadata only
- `GET /api/documents/DOC-105/acl/effective`: same effective document snapshot in this initial no-folder-inheritance implementation
- `PUT /api/documents/DOC-105/acl`: `{ "expectedVersion":1,"entries":[{"subjectKind":"group","subjectValue":"/Credit","effect":"allow"}] }`
- `GET /api/documents/DOC-105/acl/history`: administrator audit metadata

## Critical trust limitations

- **The LLM never decides authorization.** Server-side ToolPolicyGate, existing use cases and SQL RLS enforce access.
- Authorization limits the blast radius of prompt injection; it does not prevent it.
- One modular-monolith process currently possesses separate DB credentials. A compromised trusted process may fake trusted authorization context: RLS is **not** isolation from full process compromise.
- Query-time rights rely on token freshness; changes to group membership need token expiry or stronger introspection. Local ACL changes become effective in DB immediately for subsequent retrieval queries.
- `DocumentAclAdministration` currently supports explicit per-document ACLs, not full inherited folder ACL resolution. The `effective` endpoint now returns a structured, non-principal-specific view of classification, branch, allow/deny entries and documented resolution rules. It is **not** a per-user effective access decision, and folder inheritance is not implemented.
- No OCR for scanned PDFs. DOCX extracts paragraphs and drops images; parser/layout quality must be evaluated with real documents.
- No automatic source deletions/tombstones, external ACL synchronization, HNSW tuning or hybrid lexical reranker. These are future hardening tasks.
- No durable conversation store and no approval pending-action state machine. These are deliberate, documented limitations.
- Semantic retrieval quality (Recall@K/MRR) is not validated until real embeddings are pulled and evaluated against a golden dataset. Do not claim that mocked tests prove retrieval relevance.
- The added integration and SQL code is **not compiled or live-tested** in the authoring environment. Real `dotnet build`, NuGet restore, Keycloak, PostgreSQL, Angular and Ollama integration checks are mandatory before declaring completion.

## Tests added

- Unit: document-search capability fail-closed, query bounds, Unicode deterministic chunking, tool visibility and write denial.
- SQL: `make test-rag-db` verifies no-context fail-closed, public doc access, classification and explicit-deny behavior after indexing.
- Existing architecture route allowlist updated for document endpoints; original authorization tests are preserved.

## Model APIs

- Semantic Kernel uses the OpenAI-compatible chat completions protocol for OpenRouter / Ollama.
- Ollama real embedding endpoint: `POST /api/embed` and `embeddings[0]` float array; rejected unless dimension is exactly 1024.
- `Pgvector.Npgsql` mapping uses `UseVector()` on the document data source builders, with a `Vector(float[])` parameter for cosine search.


### Follow-up: review fixes

The source-level fixes described in [`M3_SECURITY_REVIEW_FIXES.md`](M3_SECURITY_REVIEW_FIXES.md) add
real token-audience mapping for ingestion, an independent Application policy for document candidates,
positive RLS persona tests, source PDF page numbers, and content-hash-based embedding skips.
They remain unverified end to end against the actual model/pgvector in this environment.
