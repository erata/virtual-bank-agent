# Secure multilingual RAG evaluation: corpus, ACL and retrieval quality

**Status:** Fixtures and scripts are checked in; **live measurements must be collected separately**. A dry-run confirms manifest/evaluation consistency but cannot prove that BGE-M3 + pgvector retrieved the right chunk or that Ollama used the tool correctly.

## Dataset design

The corpus consists of **30 synthetic documents** in `sample-docs/manifest.json` (8 PDF, 7 DOCX, 6 MD, 5 TXT, 4 JSON). Sources are intentionally divided into distinct classification levels, role/group access rules, branch rules and historical/current versions. The new samples are reproducible via `tools/generate_extended_corpus.py`; the generated PDF/DOCX bytes are **already tracked** so normal users do not need ReportLab or python-docx to run the app.

| Group | IDs | Purpose |
|---|---|---|
| Existing fixtures | DOC-101…DOC-115 | Core ACL invariants, 5 formats, TR/DE/EN, prompt-injection fixture DOC-112 |
| Public archived/current fees | DOC-116 (2024), DOC-117 (2026), DOC-130 | Near-duplicate terminology with changed fees; force date disambiguation |
| Public SEPA and help | DOC-118, DOC-119, DOC-120, DOC-129 | Cross-lingual SEPA, standing orders, customer-facing assistance |
| Branch-restricted internal | DOC-121 | Stuttgart-specific 2026 staff guide (STR branch, advisor/analyst roles) |
| Credit archived/current | DOC-122 (2023), DOC-123 (2026) | Two related approval documents with **different thresholds** |
| Credit exceptions/collateral | DOC-124, DOC-125 | Related high-ACL material in TR/EN |
| HR | DOC-126 | Classification 3; HR only |
| Operations | DOC-127, DOC-128 | Severity, RTO/RPO, recovery procedures |

**Five newly authored PDF documents have three actual, searchable pages each**, with separate scenarios per page. Existing PDF page numbers travel into stored chunk metadata and citations. DOCX, MD, TXT and JSON content is also ingested as text. This is a synthetic test fixture, not authoritative banking advice.

## Access is evaluated before relevance

For a document hit to be returned to a human, **all** of these conditions must hold:

1. Caller has `document.read` and a validated, active human identity.
2. Document `classification <= principal.clearance` (0 Public, 1 Internal, 2 Confidential, 3 StrictlyConfidential).
3. Optional document branch matches the identity-derived branch claim.
4. At least one ACL `allow` matches a verified user/role/group/public rule.
5. No matching explicit `deny` overrides the allow.
6. Restricted database role and PostgreSQL `FORCE ROW LEVEL SECURITY` filter candidates *before* top-K.
7. Application `DocumentReadPolicy` independently rechecks returned candidates before the LLM context is constructed.

**Bank customers** are granted `document.read` with `public` clearance. They may see public fees and FAQ/SEPA guides **but not** internal staff, Credit or HR documents. Admin Lena has no `document.read`, so she cannot see even public documents. The corpus manifest supplies initial ACLs; after ingestion, the DB remains the authoritative ACL store. The evaluation's fixture-based ACL oracle assumes a **fresh seed without later ACL administration edits**.

## Evaluation questions and metrics

`tests/evaluation/rag_golden.json` contains **42 labeled questions**, exactly 14 Turkish, 14 German and 14 English. Each positive question defines `expectedAny` (acceptable evidence IDs); every case can include explicit `forbidden` IDs. Negative cases have empty `expectedAny` but still assert that protected results do not appear. One admin case expects **HTTP 403**, not an empty HTTP 200 answer.

The live evaluator also independently computes whether **every returned ID** is allowed by the seed manifest, role permissions, Keycloak user attributes and ACL rules; it is **not limited to the manually listed forbidden IDs**. An unknown returned document ID is a failure. This is an evaluation oracle, not an assertion that the model itself enforces security.

| Metric | Interpretation |
|---|---|
| **Hit@5** | Fraction of positive questions with at least one ID in `expectedAny` among the top 5 |
| **MRR@5** | Reciprocal rank of the first acceptable document, averaged over positive questions |
| **Known-ID coverage@5** | Fraction of explicitly listed expected IDs present in the top 5, averaged per question; **not** full-corpus Recall@5 because `expectedAny` may not be exhaustive |
| **Temporal Top-1** | On `current`/`archived`-labeled questions, first result matches one of the target-period document IDs |
| **Forbidden hits** | Returned IDs that the case explicitly prohibits (must be 0) |
| **Unauthorized or unknown hits** | Any returned result forbidden by the independent seed authorization oracle or missing from manifest (must be 0) |

The default release gate requires **Hit@5 >= 70%**, **0 authorization leakage** and **0 HTTP/response shape errors**. This is a starting acceptance threshold for a **small** synthetic fixture and should not be reported as production quality, zero hallucinations or a validated embedding benchmark across banks.

### Run the actual evaluation

First run a fresh container stack, make sure BGE-M3 is ready, then ingest the synthetic corpus. Normal local starters already perform ingestion.

**Windows:**

```powershell
.\scripts\windows\INDEX_DOCUMENTS_WINDOWS.ps1
python .\tools\evaluate_rag.py --dry-run
python .\tools\evaluate_rag.py --api http://localhost:8081 --min-hit-rate 0.70
```

**Linux/macOS:**

```bash
bash scripts/ai-index.sh
python3 tools/evaluate_rag.py --dry-run
python3 tools/evaluate_rag.py --api http://localhost:8081 --min-hit-rate 0.70
```

If the host API port was changed, supply `--api http://localhost:<PORT>`. For Keycloak port changes supply `--keycloak`. The test obtains **development-only** real user tokens and never logs the JWT itself. It requires live Keycloak, a real API, the actual embedding model, a populated pgvector store and current ACL seed. Print/snapshot only sanitized results before adding them to a technical article.

### Repeat the DB/RLS checks

```bash
# From Linux/macOS or WSL; requires psql client and a freshly indexed Compose database:
PGHOST=localhost PGPORT=5432 PGUSER=postgres \
  PGPASSWORD=postgres-dev-only bash scripts/test/document-rls-tests.sh
```

This SQL script verifies positive document access for Anna, Mehmet, Sophie, Oliver **and Deniz**, negative branch/classification rules, a denied admin, missing context and explicit-deny-over-allow. It is not an embedding-quality test.

## Interpretation and limitations

- **Top-5 can still flatter a small corpus.** Compare Hit@1, temporal Top-1 and MRR@5; the new near-duplicate historical policies make the ranking task harder but do not constitute a large public benchmark.
- **Version confusion is separate from ACL.** Both current and old *authorized* versions may be retrieved. The answer must identify effective dates and not silently use an archive as the current rule.
- **Safety isn't guaranteed by thresholds.** `DOCUMENTS_MIN_SIMILARITY=0.45` is not a universal calibrated cutoff. Very relevant results can score below it; irrelevant results can score above it. Calibrate per embedding model and language.
- **Prompt injection is still possible.** DOC-112 contains deliberately untrusted instructions. Authorization limits access; it cannot force a model to disregard every malicious sentence. Tool execution checks and deny-by-default write policies must remain in C#.
- **An unsupported question needs a truthful response.** If no sufficient authorized evidence exists, do not improvise a policy or claim a private document does not exist. Say that accessible sources did not substantiate the answer.
- **The evaluation covers retrieval, not fully grounded generative accuracy.** For a public portfolio, manually review sample LLM answers and citations, and compare the same questions across TR/DE/EN and at least two chat models.

See also [Architecture](architecture.md).

## Latest measured result

2026-11-25, fresh Compose install, OpenRouter `openai/text-embedding-3-small` (1024 dimensions), 30 documents:
Hit@5 **85.7 %** (DE 11/12, EN 10/11, TR 9/12), MRR@5 0.767, known-ID coverage@5 0.757, temporal Top-1 6/8,
**0 unauthorized/forbidden hits**, 0 errors — gate **PASS**. The misses are cross-lingual queries (EN/DE → the Turkish
HR grades fixture DOC-114, TR → the German operations runbook DOC-115), which became single-language documents in this
revision. A multilingual embedding model such as `bge-m3` (Ollama profile) is expected to do better on these cases;
measure it with `python tools/evaluate_rag.py` after re-indexing.
