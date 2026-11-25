> **Historical milestone / patch note.** This file documents an earlier revision and may include obsolete defaults or expected test counts. For the CURRENT reference release, see [`README`](../README.md), [`architecture`](architecture.md), [`quality gate`](PUBLICATION_QUALITY_GATE.md) and [`validation status`](VALIDATION_STATUS.md).

# OpenRouter-only startup (Windows PowerShell)

This project intentionally separates the chat model from the document embedding service.

## Default Compose startup without Ollama

`compose.yaml` has been fixed: `api.depends_on` only requires PostgreSQL and Keycloak.
`ollama` has `profiles: ["ai"]`, so a normal `docker compose up` will NOT pull
or start the Ollama image, even if the service is not downloaded yet.

1. Keep your existing `.env` (and any port overrides):

   ASSISTANT_PROVIDER=OpenRouter
   ASSISTANT_MODEL=openai/gpt-4o
   ASSISTANT_ENDPOINT=https://openrouter.ai/api/v1
   OPENROUTER_API_KEY=<your-private-key>

2. In PowerShell from the project root:

   docker compose config --services
   docker compose up -d --build --wait
   docker compose ps

Check `docker compose ps` after startup: Ollama should not be running. If your Compose version shows profiled services in `config --services`, that output alone does not mean they will start.

### Quick one-off workaround if you have not updated compose.yaml

If PostgreSQL and Keycloak are already healthy and API/frontend images are built:

   docker compose up -d --no-deps api frontend

This skips dependency traversal; it is not a substitute for fixing the file.

## RAG/pgvector needs an embedding provider

With this patch, GPT-4o chat and non-document tools can run without Ollama.
**The current document embedding implementation remains `OllamaEmbeddingProvider`**
(`bge-m3` at `http://ollama:11434/api/embed`). Document ingestion and
`search_documents` will not work without that embedding service; setting
`ASSISTANT_PROVIDER=OpenRouter` does not automatically change the document
embedding provider. Do not present RAG as operational without an embedding provider.

To enable the existing local embedding adapter later:

   docker compose --profile ai up -d ollama
   docker compose --profile ai exec ollama ollama pull bge-m3

Changing embedding provider to a cloud embedding API would require a dedicated
adapter, supported model, vector-dimension alignment, and reindexing.

## Notes

- This is a Compose dependency fix. It does not alter authentication, database
  roles, user seed data, API source, or Semantic Kernel/OpenRouter credentials.
- Existing `.env` and any previously customized host-port mappings are not
  automatically migrated when you extract a new ZIP. For an existing local
  checkout, edit only the small Compose block as described in the chat.
- Do NOT run `docker compose down -v`; that deletes development volumes.
