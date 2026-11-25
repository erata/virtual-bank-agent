# VirtualBank: API-key-free local starter and cloud switch

## Local first run (recommended for a GitHub visitor)

Windows PowerShell, at repository root:

```powershell
.\scripts\windows\start-local.ps1
```

This script creates `.env` if absent, explicitly enables Compose's **optional `ai` profile**, pulls `qwen3:4b-instruct` (tool-calling chat, ~2.5 GB) and `bge-m3` (1024-dimensional multilingual embeddings, ~1.2 GB), starts PostgreSQL/EF migrations/Keycloak/API/Angular and runs local document ingestion. Docker's own Ollama image is an additional download; models are not guaranteed to fit small machines, even if quantized. Runs may be slow on CPU. Use `-SkipIndex` to test just authenticated banking tools. No OpenRouter key required.

The first step should still be verifying `dotnet build`/`dotnet test` and EF drift on a .NET-enabled developer machine; this author's environment could not execute the .NET toolchain. **Source implementation != verified end-to-end runtime.**

## Switch to OpenRouter — zero Ollama pull on regular Compose startup

Edit `.env`: **disable BOTH** Ollama sections (chat and embeddings), uncomment the OpenRouter chat and embedding settings, and **uncomment** `# OPENROUTER_API_KEY=` to set `OPENROUTER_API_KEY`. Ensure `COMPOSE_PROFILES` is unset/empty. Then:

```powershell
docker compose up -d --build --wait
.\scripts\windows\INDEX_DOCUMENTS_WINDOWS.ps1
```

You don't need to remove the optional `ollama` service from `compose.yaml`. `api` has no `depends_on: ollama`, and it does not appear in the default Compose profile. **Do not use `--profile ai` when starting cloud-only.** A previously running Ollama container remains running until intentionally stopped; switching config does not magically stop existing containers.

To stop an already running local Ollama *without deleting model volumes*:

```powershell
docker compose --profile ai stop ollama
```

OpenAI direct works the same way using the OpenAI section and `OPENAI_API_KEY`.

## Offline versus Ollama

`ASSISTANT_PROVIDER=Offline` is a **deterministic test-only C# rule router**; it is NOT a model or RAG engine. `Ollama` runs a real local model; `OpenRouter` and `OpenAI` call cloud models. No provider selection automatically authorizes a banking tool. Authorization is always enforced by the .NET ToolPolicyGate, Application policies and PostgreSQL RLS.

## Embedding compatibility and test quality

`bge-m3`, and cloud `text-embedding-3-small` can each produce 1024 values. The stored document `embedding_model` key prevents silently mixing their distinct vector spaces. Changing the model requires re-ingestion and measured TR/DE/EN retrieval quality. A low similarity score is NOT itself a security decision: ACL/RLS and independent Application document authorization determine access. Do not infer that tiny LLM weights are security-certified or hallucination-free.

## Before publishing

Run the real .NET and EF CLI checks:

```powershell
dotnet restore .\VirtualBank.slnx
dotnet build .\VirtualBank.slnx -c Release
dotnet test .\VirtualBank.slnx -c Release
.\scripts\windows\VERIFY_PUBLICATION_WINDOWS.ps1
```

Do not label CI as green until all actual checks pass. The checked-in EF snapshots were regenerated with the real EF Core 10.0.4 CLI and `dotnet ef migrations has-pending-model-changes` is clean for all three contexts (see [EF_SNAPSHOT_VERIFICATION.md](EF_SNAPSHOT_VERIFICATION.md)); CI must keep running that check. If it ever fails, regenerate snapshots with the real EF tooling and review generated migrations before a public release.
