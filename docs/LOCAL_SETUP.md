# Local setup: Windows, Linux and macOS

This is the **current, canonical** first-run guide for the VirtualBank reference repository. The checked-in files are designed to run through Docker Compose; Windows-specific PowerShell and Unix Bash scripts are simply two frontends for the same containers. They do **not** represent different backends.

> **Validation status:** Verified end-to-end on Windows 11 with Docker Desktop (Compose, Keycloak, PostgreSQL + pgvector, OpenRouter and Ollama), including a fresh install with empty volumes. Linux/macOS scripts are checked for structure; ARM64 images were not run. See [validation status](VALIDATION_STATUS.md).

## 1. Prerequisites

| | Windows | macOS | Linux |
|---|---|---|---|
| Containers | Docker Desktop with Compose v2+ | Docker Desktop (Intel or Apple Silicon) with Compose v2+ | Docker Engine plus Compose plugin |
| Startup shell | PowerShell 7 recommended; Windows PowerShell 5.1 generally works | macOS Terminal, Bash 3.2+ | Bash |
| First-run internet | Needed to download container images, Ollama and model weights | Same | Same |
| Embedding ingestion | Included in PowerShell startup script | Requires `curl` and `jq` | Requires `curl` and `jq` |
| Source-only build/tests | Optional .NET 10 SDK | Optional .NET 10 SDK | Optional .NET 10 SDK |
| Retrieval evaluation | Optional Python 3 | Optional Python 3 | Optional Python 3 |

**Hardware planning:** Chat `qwen3:4b-instruct` (~2.5 GB model file) and embeddings `bge-m3` (~1.2 GB) are separate models; **the Ollama image, Docker images, PostgreSQL data, KV cache and inference memory are additional**. The totals are *download* sizes, not RAM/VRAM requirements. Plan approximately 10+ GB of free disk for a comfortable first run and substantially more than 4 GB of available RAM for Docker and model inference. CPU inference works but may be slow; GPU acceleration depends on Docker/Ollama platform configuration. On Apple Silicon verify that each pinned image supports ARM64; this exact release has not been live-tested on ARM.

```text
Required: git (or download ZIP), Docker, Docker Compose
Optional: .NET 10 SDK, Python 3, Bruno, Node 22 (source development)
macOS: brew install jq                       # if jq is missing
Ubuntu: sudo apt-get install -y curl jq     # if missing
```

Do **not** install PostgreSQL, Keycloak, Node or the .NET SDK separately just to run the prebuilt container workflow. Docker Compose builds their respective images.

## 2. Clone and first run: local Ollama, no API key

Get the repository from its actual GitHub URL (replace `<REPO_URL>` with the URL shown by the repository's **Code** menu):

```bash
git clone <REPO_URL>
cd <REPO_DIRECTORY>
```

Or download the repository ZIP and open a terminal inside the extracted root containing `compose.yaml` and `.env.example`.

### Windows (PowerShell)

```powershell
# First run creates .env from the committed .env.example when missing.
.\scripts\windows\start-local.ps1
```

To skip document ingestion while investigating API startup, run `-SkipIndex`:

```powershell
.\scripts\windows\start-local.ps1 -SkipIndex
```

### Linux / macOS (Bash)

```bash
# No chmod required if called through bash:
bash scripts/unix/start-local.sh
```

To skip the ingestion step:

```bash
bash scripts/unix/start-local.sh --skip-index
```

**Both scripts**: create a local `.env` only when absent; explicitly start optional `ollama` using `--profile ai`; pull **`qwen3:4b-instruct`** and **`bge-m3`** as configured; start PostgreSQL, one-shot EF Core migrator, Keycloak, ASP.NET Core API and Angular; index the synthetic PDF/DOCX/MD/TXT/JSON corpus. Neither script deletes Docker volumes or asks for a cloud key.

The Windows script uses native `Invoke-RestMethod` for ingestion; the Unix script uses the checked-in `scripts/ai-index.sh` (curl + jq). They use the same `svc-ingestion` client credentials from the **local-only** Keycloak demo realm, not a fabricated user identity.

### First-run checks

```bash
docker compose ps
docker compose logs --tail=100 db-migrate
docker compose logs --tail=100 api
```

Expected components:

| Component | How to reach it | Expected state |
|---|---|---|
| Angular UI | http://localhost:4200 | Opens the app / redirects to Keycloak login |
| Keycloak | http://localhost:8080 | Starts and reports healthy; fresh import may take time |
| ASP.NET Core API | http://localhost:8081/health/ready | HTTP 200 *only after its dependencies are ready* |
| PostgreSQL | Internal `postgres:5432`, optionally Windows host `localhost:5432` | Healthy |
| EF migration job | `docker compose ps -a` | Completed with exit code 0 |
| Ollama | http://localhost:11434 (local only) | Models appear in `ollama list` |

For the API readiness check, use PowerShell `Invoke-WebRequest http://localhost:8081/health/ready` or Bash `curl -i http://localhost:8081/health/ready`. Ports are subject to your `.env` overrides.

### CPU performance: measure first, then choose native Ollama

Without a supported GPU, chat inference runs on the CPU. Every assistant turn logs timing only (no prompt or data):

```powershell
docker compose logs api | Select-String 'AssistantTurnTiming|AssistantTokenUsage'
# AssistantTurnTiming mode=ollama model=qwen3:4b-instruct outcome=completed totalMs=… modelMs=… toolMs=… toolCalls=…
```

`modelMs` far above `toolMs` means the model is the bottleneck, not the use cases or PostgreSQL. To measure Ollama alone:
`docker compose --profile ai exec ollama ollama ps` (shows `100% CPU` when no GPU is used).

On Windows, Ollama installed natively (<https://ollama.com/download>) usually runs faster than inside Docker/WSL:

```powershell
.\scripts\windows\start-local.ps1 -NativeOllama
```

This stops (does not delete) the Ollama container, pulls the configured models into the native install and points the
API at `http://host.docker.internal:11434` **for this run only**; `.env` is not modified. `host.docker.internal` is the
only extra plain-HTTP host the API accepts for Ollama; cloud providers still require HTTPS. Run the script without the
switch to return to the container setup. Compare `AssistantTurnTiming` before and after.

Questions such as "Bu asistanla neler yapabilirim?" / "Was kann ich hier tun?" / "What can I do?" are answered
instantly from the caller's permission-filtered tool list (mode `catalog`, no model call).

## 3. Demo users and the visible authorization boundary

Every user and account is **synthetic**. The development-only password in the checked-in realm is `VirtualBank-Dev-2026!` (never reuse in a real system).

| Login | Persona | Useful demo |
|---|---|---|
| `anna.berater` | Stuttgart advisor | Lists customers only in the authorized branch |
| `mehmet.kredit` | Credit analyst | Assigned credit applications and confidential Credit-group policy documents |
| `max.approver` | Approver | Can view permitted credit documents; the assistant never executes an approval |
| `sophie.hr` | HR specialist | Restricted HR documents, no customer account access |
| `oliver.ops` | Operations | Operations documents, no confidential credit or HR content |
| `lena.admin` | Platform admin | Administrative context is **not** permission to read business documents |
| `deniz.customer` | Bank customer | Own IBAN, balances, recorded payments **and public fee/FAQ/SEPA documents** |
| `thomas.customer` | Another customer | Same operations but NEVER Deniz's personal data |

Additional `bank-customer` fixtures (Meryem, Lucas, Elif, Emilia) have EUR/USD/TRY accounts. Customer balances are shown **per currency**: don't combine EUR, USD and TRY without a trusted exchange rate.

The `bank-customer` role is granted **`document.read`** but the token's **clearance is `public` (0)**. Both the independent Application document policy and PostgreSQL RLS must enforce this limit, plus document ACL, branch and explicit deny. Hence a customer may ask "What are the 2026 bank fees?" but cannot retrieve Credit or HR policies. A document's `public` ACL alone never bypasses a missing `document.read` permission: for example `lena.admin` cannot read public docs by virtue of being a platform admin.

## 4. Change providers without downloading Ollama

The `.env.example` file has **active Ollama lines** and completely commented OpenRouter/OpenAI alternatives. Modify your **untracked** `.env` only; do not add a second active assignment for the same key.

### OpenRouter — GPT-4o and OpenAI-compatible embeddings

Comment the active `ASSISTANT_*` and `DOCUMENTS_EMBEDDING_*` lines, then uncomment the whole OpenRouter block, including `# OPENROUTER_API_KEY=` and put your own secret in `.env`:

```dotenv
ASSISTANT_PROVIDER=OpenRouter
ASSISTANT_MODEL=openai/gpt-4o
ASSISTANT_ENDPOINT=https://openrouter.ai/api/v1
DOCUMENTS_EMBEDDING_PROVIDER=OpenRouter
DOCUMENTS_EMBEDDING_MODEL=openai/text-embedding-3-small
DOCUMENTS_EMBEDDING_ENDPOINT=https://openrouter.ai/api/v1/
DOCUMENTS_EMBEDDING_DIMENSIONS=1024
OPENROUTER_API_KEY=your-own-key
```

Then run (Windows PowerShell **or** Linux/macOS terminal):

```bash
# Do not specify --profile ai; also remove COMPOSE_PROFILES=ai if set.
docker compose up -d --build --wait
```

No new Ollama image is pulled because `api` has no Ollama dependency and the Ollama service has an **optional** Compose profile. If a previous Ollama container is still running, stop it explicitly (without deleting model files):

```bash
docker compose --profile ai stop ollama
```

For direct OpenAI use the separately documented `ASSISTANT_PROVIDER=OpenAI`, `gpt-4o`, `DOCUMENTS_EMBEDDING_PROVIDER=OpenAI`, `text-embedding-3-small`, `OPENAI_API_KEY` block in `.env.example`. Chat and embedding providers are independent choices; both cloud variants may incur fees. Only use **synthetic demo data** with external models.

**Critical RAG note:** `bge-m3` and `text-embedding-3-small` both produce 1024-dimensional vectors *in this configuration* but their embedding spaces are incompatible. After switching embedding **provider or model**, re-ingest the corpus and verify the stored `embedding_model` identifier. Switching only the chat model does not require re-embedding. Ideally isolate different embedding models in distinct collections/indices; the present demo maintains one corpus.

### Offline: test mode, not a model

`ASSISTANT_PROVIDER=Offline` activates a limited rule-based C# tool router; it is **not** local model inference and cannot be assumed to understand arbitrary user paraphrases. It is helpful for deterministic tests. The regular public demo should use Ollama or a configured cloud model.

## 5. Test the complete stack, not only its UI

After real ingestion, run:

**Windows (with .NET 10 SDK and optional Python):**

```powershell
.\scripts\windows\VERIFY_PUBLICATION_WINDOWS.ps1
.\scripts\windows\INDEX_DOCUMENTS_WINDOWS.ps1
python .\tools\evaluate_rag.py --dry-run
python .\tools\evaluate_rag.py --min-hit-rate 0.70
```

**Linux/macOS (with .NET 10 SDK and Python 3):**

```bash
bash scripts/unix/verify-publication.sh
bash scripts/ai-index.sh
python3 tools/evaluate_rag.py --dry-run
python3 tools/evaluate_rag.py --min-hit-rate 0.70
# Requires psql; separately check the migrated, indexed PostgreSQL database:
# PGHOST=localhost PGUSER=postgres PGPASSWORD=postgres-dev-only bash scripts/test/document-rls-tests.sh
```

The quality test now has **42 multilingual, role-scoped cases** including competing 2023/2026 credit documents and 2024/2026 fee schedules. It measures Hit@5, MRR@5, macro Recall@5, temporal Top-1 and unauthorized-document leakage, and compares returned IDs with an independent seed-manifest ACL oracle. **`--dry-run` is only fixture validation; it is not a retrieval benchmark.** The benchmark requires a genuinely running API, valid tokens, the matching embedding service and a populated pgvector index. See [RAG evaluation design](RAG_EVALUATION.md).

Source-only `dotnet build/test`, browser tests, Bruno, RLS scripts and actual tool calls each check a different layer. An all-green unit suite is not a substitute for a live model-generated tool call or a source-cited RAG answer.

## 6. Common problems and how to diagnose them

| Symptom | Likely cause and safe diagnostic action |
|---|---|
| `make: command not recognized` in PowerShell | `make` is optional. Use `start-local.ps1` or native `docker compose` commands. |
| `Bind for 127.0.0.1:5432 failed` | Another PostgreSQL installation/container owns the host port. In `.env` set `POSTGRES_PORT=5433` without changing internal `postgres:5432`. |
| `Bind for 127.0.0.1:8081 failed` | Another API/container owns the host port. Set `API_PORT=8082` and use that port in evaluation/API tools. |
| `Keycloak ... Waiting` for 30–60 seconds | Fresh realm import may take time. `docker compose logs --tail=100 keycloak`; inspect health. Don't reset the DB as a response to a slow startup. |
| User/role changes don't appear | Make sure the current realm JSON is mounted; check `docker compose logs keycloak`. Do not assume an existing persistent realm DB automatically imports changed users. |
| `dotnet ef migrations ... pending model changes` | The Code First snapshot has drifted from the runtime mapping; inspect/regenerate with real EF CLI. Never mark mismatched migration history as applied. |
| `NU1904` or other NuGet audit errors | Do not disable audit globally. Upgrade to a fixed dependency version and confirm build/security scan. |
| Ollama unexpectedly downloads in cloud mode | Check for `--profile ai`, `COMPOSE_PROFILES=ai`, or an already running Ollama container. `docker compose --profile ai stop ollama` preserves models. |
| Assistant says `mode offline` | `.env` still selects `Offline`; set chat provider to `Ollama`, `OpenRouter` or `OpenAI`, then recreate the API. |
| Chat works but `search_documents` is empty | Ingest the 30 documents, confirm the embedding model is available and indexed vectors match the configured `embedding_model`. |
| HTTP 401 on document ingestion | Check the `svc-ingestion` Keycloak client, machine `principal_type`, and `virtual-bank-api` token audience. Do not bypass audience validation. |
| A customer sees credit or HR documents | **Security failure.** Stop publishing the demo; review `document.read`, customer clearance `public`, ACL, Application policy and RLS; run leakage tests. |
| `docker compose` says migrations cannot adopt old schema | An old SQL-created volume is being reused. Back it up, create a separate clean demo environment or perform a reviewed data migration. Do not blindly delete important volumes. |

Use `docker compose ps -a` and `docker compose logs --tail=150 api db-migrate postgres keycloak` for diagnostic output. Remove bearer tokens, secrets or personal data before sharing logs.

## 7. Stop, restart and remove demo data safely

```bash
# Stop this Compose project's containers and network; keep named volumes and models:
docker compose --profile ai down
# Restart using your chosen provider. For local Ollama use its OS-specific starter.
# ONLY for completely disposable, backed-up demo data:
# docker compose --profile ai down -v
```

`down -v` irreversibly removes Compose-managed PostgreSQL and Ollama named volumes. **Never run it against non-disposable data** or because a health check merely takes a minute. The repository contains developer-only credentials and fictional synthetic assets; never publish a live instance with them.

## 8. Platform compatibility caveats

The core .NET/C# program is cross-platform. Its Docker images and shell wrappers are independent concerns: ARM64 availability, GPU passthrough, filesystem permissions/line endings and port collisions depend on the host. The Bash script avoids GNU-specific `sed -i`/`readlink -f` so it is compatible with the default older macOS Bash. Docker Desktop on Apple Silicon may run some pinned AMD64 images under emulation; this costs performance and is **not** yet a validated release guarantee. File any platform-specific failure with the full Compose service log and the host architecture, not with secrets.

For design and security details see [Architecture](architecture.md), [Authorization](authorization-model.md), [Security threat model](threat-model.md) and [Release evidence gate](PUBLICATION_QUALITY_GATE.md).


### Windows PowerShell: `ollama server not responding` immediately after `Started`

On the first Docker start, the Ollama container can be **running** before its server accepts commands. The Windows starter retries readiness for up to three minutes; early connection refusals are expected and do not by themselves mean the setup is broken.

```powershell
# Check whether it is actually ready and inspect logs if needed
docker compose --profile ai ps
docker compose --profile ai logs --tail=80 ollama
docker compose --profile ai exec -T ollama ollama list
```

The old starter used `*> $null` with PowerShell 5.1's `$ErrorActionPreference = 'Stop'`, which could terminate at the first transient `NativeCommandError`. The revised starter probes readiness without treating transient stderr as fatal. If the container continually restarts, investigate memory/GPU configuration and Docker logs; do not reset PostgreSQL volumes.
