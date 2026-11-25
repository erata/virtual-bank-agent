> **Historical milestone / patch note.** This file documents an earlier revision and may include obsolete defaults or expected test counts. For the CURRENT reference release, see [`README`](../README.md), [`architecture`](architecture.md), [`quality gate`](PUBLICATION_QUALITY_GATE.md) and [`validation status`](VALIDATION_STATUS.md).

# M2/M3 security review remediation (2026-11-22)

## What changed

- Removed an invalid copied block in `OfflineAssistantRuntime.CompleteAsync`; fixed missing `MapGroup` and test namespace imports.
- Moved `IngestDocumentsHandler` under `Documents/Commands/IngestDocuments` with a typed command; moved `ConversationState` out of `Abstractions`.
- Updated HR `assistant.use` matrix case to match the permission catalog.
- Added an audience mapper only for the **svc-ingestion** Keycloak client; reporting retains its no-business-audience invariant. Added live token claims checks.
- HR principal now has `strictly_confidential` clearance (3); created `oliver.ops` (clearance 2), and an STR branch-specific document, with matching role permissions and independent matrix cases.
- Added application-level document policy over actual canonical ACL metadata, after PostgreSQL RLS has filtered candidates; mismatches are fail-closed and audited.
- Provided an allow-checked `docs.readable_acl` SECURITY DEFINER projection, without granting direct ACL table SELECT to `document_api`. It rechecks the saved classification/branch, not user-supplied values.
- Added positive persona/document matrix cases, independent application-authorization tests, real RLS positive and negative cases, and an actual explicit-deny-over-allow test.
- PDF parsing uses `ContentOrderTextExtractor` and saves page numbers with chunks. PDFs without extractable text remain unsupported (no OCR).
- Unchanged indexed documents are checked before calling the embedding server. Changed security-relevant metadata triggers indexing; ACL entries managed in DB are not reset.
- Hide-mode explicit credit-application requests receive a safe, generic permission hint without exposing whether a record exists.
- In-memory conversations keep both user and assistant turn text for the development session, but **never replay previous assistant answers to the model**, because access may be revoked.
- `/acl/effective` returns resolution rules and canonical entries instead of being an alias of `/acl`. This is metadata-only, **not a per-principal evaluated effective ACL**.

## Required real runtime verification

The editing environment has **no `dotnet`, `docker`, `psql`, pgvector extension or reachable model runtime**. No compilation or real RAG quality test is claimed here. Do not describe this archive as end-to-end verified.

1. `dotnet restore VirtualBank.slnx && dotnet build VirtualBank.slnx -warnaserror && dotnet test VirtualBank.slnx`.
2. Use a fresh ephemeral database volume for the amended document schema (PostgreSQL init scripts run only on first cluster initialization; DO NOT use `make reset` on valuable data).
3. `make up` and `scripts/test/keycloak-claims.sh`; confirm the new ingestion token has `aud=virtual-bank-api` while reporting does not.
4. `make ai-up && make ai-pull && make ai-index` (real Ollama bge-m3 embeddings).
5. `make test-rag-db` (actual pgvector schema and SQL RLS with positive/negative personas).
6. Against the *real .NET API*, test Anna/Mehmet/Sophie/Oliver/Lena under real Keycloak JWTs, citations with PDF pages, ACL changes, and a second `make ai-index` doing **zero** embeddings for unchanged content.
7. Use real Semantic Kernel package + LLM, not compiler stubs or the offline router, to verify function calling and model-visible filtered context.

## Still limited / planned

- No real model runtime integration test in this environment.
- Sample documents are short, not an enterprise-scale retrieval evaluation dataset.
- Folder ACL inheritance, authoritative external connector sync, durable conversations, prompt-context revocation and full human-in-the-loop approvals remain future work.
- HNSW is an optional speed optimization after exact secure retrieval works; absence of HNSW is **not** a correctness bug.
- Database RLS relies on application-set session context; it does not secure a compromised trusted application that can forge that context.
