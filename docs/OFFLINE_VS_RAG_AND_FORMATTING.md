> **Historical milestone / patch note.** This file documents an earlier revision and may include obsolete defaults or expected test counts. For the CURRENT reference release, see [`README`](../README.md), [`architecture`](architecture.md), [`quality gate`](PUBLICATION_QUALITY_GATE.md) and [`validation status`](VALIDATION_STATUS.md).

# Assistant language handling, structured results and evidence guard

This patch is an incremental change to the existing five-layer VirtualBank codebase.

## Which component answered “müsterilerimi göster”?

When `ASSISTANT_PROVIDER=Offline`, the system does **not** use a language model, vector database or RAG to determine the requested business operation. `OfflineAssistantRuntime` is a deterministic rule-based router. Before this patch it used case-sensitive-ish substring matching for a few words and rendered raw JSON-like tool results. `müsterilerimi` was not matched because it differs from the exact configured text.

Now the offline router normalizes Turkish / German accents and common inflections (ş/s, ı/i, ö/o, ü/u), using `NaturalLanguageIntent`. For example:

- `müşterilerimi göster`, `musterilerimi goster`, `müsterilerimi göster` → `list_customers`
- `başvurularımı göster`, `basvurulari goster` → `list_credit_applications`
- `Zeig mir meine Kunden` → `list_customers`
- `Welche Kreditrichtlinie gilt?` → `search_documents`

This is a bounded set of **offline intents**, not arbitrary natural language understanding. In real LLM mode (`ASSISTANT_PROVIDER=Ollama` or `OpenRouter`) Semantic Kernel supplies the tool schemas, the model chooses tools, and `ToolPolicyGate` still authorizes each call. Never substitute vector similarity for tool authorization.

## Structured results / Angular

ToolSession projects the successfully authorized `list_customers`, `list_credit_applications`, and `list_customer_accounts` use-case results to typed `tables`. The API response retains `answer`, `toolCalls`, `notices`, `citations`, `conversationId` and adds:

```json
"tables": [{
  "title": "Müşteriler",
  "columns": ["Müşteri No", "Ad Soyad", "Şube", "Segment"],
  "rows": [["C-1002", "Jana Weidner", "STR", "private"]],
  "totalCount": 3
}]
```

Rows are projections of existing authorized query results, **not LLM-generated markup**. Angular sanitizes shapes/lengths and renders cells with interpolation (no `innerHTML`). Results are limited to 100 displayed rows; `totalCount` indicates the actual count.

## RAG / hallucinations

RAG is separate from customer/credit record lookups:

1. `search_documents` obtains the question embedding from Ollama `bge-m3`.
2. PostgreSQL RLS and DocumentReadPolicy restrict retrieved chunks before they enter model context.
3. Cosine scores below `Documents:MinimumCosineSimilarity` (default **0.45**, environment `DOCUMENTS_MIN_SIMILARITY`) are filtered.
4. If a policy/document question has **no authorized citations**, `AskAssistantHandler` replaces the model narrative with a localized **not-enough-evidence** answer. A model cannot forge those citations.
5. A bank-data question that obtains **no tool calls** also cannot return an ungrounded LLM claim.

**Caution:** A similarity score does not guarantee that an excerpt actually entails an answer. The threshold needs validation with labelled TR/DE/EN relevant and irrelevant queries, negative tests and a real Ollama+pgvector stack. Citations prove what was retrieved, not that every model sentence is true. The demo must not claim perfect anti-hallucination.

The `Offline` mode is useful for authorization and format tests but it is not a semantic RAG evaluation. For genuine multilingual semantic search, start Ollama, pull bge-m3, ingest the documents, run semantic retrieval tests, and optionally enable a real tool-capable LLM.

## PowerShell smoke checks

In the directory containing `compose.yaml`:

```powershell
# Do not reset your database volumes; rebuild the two changed images.
docker compose up -d --build --wait

dotnet test .\VirtualBank.slnx -c Release

# Watch for errors from startup and the AI modules:
docker compose logs --tail=100 api
```

Open http://localhost:4200, sign in with a demo advisor, and ask `müsterilerimi göster` and `musterilerimi goster` — both should yield the same rows. `başvurulari goster` should be denied for an unauthorized advisor and produce a deterministic code. In Offline mode a policy question only retrieves existing docs when the embedding/pgvector stack is running; if no supporting documents are found, expect an explicit uncertainty message.
