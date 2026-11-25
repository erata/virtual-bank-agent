# Local and cloud model profiles (VirtualBank)

The default `.env.example` selects Ollama `qwen3:4b-instruct` for chat/tool calling and `bge-m3` for 1024-dimensional document embeddings. On Windows run `./scripts/windows/start-local.ps1` (PowerShell syntax: `.\scripts\windows\start-local.ps1`). This explicitly enables the optional `ai` Docker Compose profile. It downloads the Ollama image plus both model files.

For OpenRouter, comment out the Ollama chat and document settings in `.env`, then uncomment the entire OpenRouter block **including** `# OPENROUTER_API_KEY=` and supply your own secret. Choose `openai/gpt-4o` for chat and `openai/text-embedding-3-small` with `DOCUMENTS_EMBEDDING_DIMENSIONS=1024` for vectors. Run `docker compose up -d --build --wait` without `--profile ai`. It will not pull the Ollama service because API does not depend on it.

For direct OpenAI, do the same with the OpenAI block and `# OPENAI_API_KEY=`. `.env` is intentionally excluded by `.gitignore` and must never be committed. Chat and embedding are separate providers; changing the chat model alone does not require reindexing. Changing the embedding model requires re-embedding/reindexing documents because equal dimensionality does **not** imply a common vector space.

`ASSISTANT_PROVIDER=Offline` is an optional deterministic **non-LLM** test router. It is not selected by default and should not be presented as full natural-language understanding.

## Validation status

This package was checked for ZIP integrity and static configuration consistency. The actual .NET build, EF Core migrations, end-to-end Ollama function calling, OpenRouter requests, and pgvector retrieval **have not been executed in this environment**. Run the repository verification scripts and real integration tests before publishing test-success claims.
