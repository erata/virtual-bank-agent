#!/usr/bin/env bash
# VirtualBank local-first bootstrap for Linux and macOS; no .NET SDK, Node, make or API key required.
# Uses committed Compose and pre-generated docs. Requires Docker+Compose, curl, jq, Bash 3.2+.
# Usage: bash scripts/unix/start-local.sh [--skip-index]
set -euo pipefail
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$ROOT"
SKIP_INDEX=0
case "${1:-}" in
  '') ;;
  --skip-index) SKIP_INDEX=1 ;;
  --help|-h) echo 'Usage: bash scripts/unix/start-local.sh [--skip-index]'; exit 0 ;;
  *) echo 'Unknown option. Use --help.' >&2; exit 2 ;;
esac
NEEDED=(docker)
if [[ $SKIP_INDEX -eq 0 ]]; then NEEDED+=(curl jq); fi
for command in "${NEEDED[@]}"; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Missing $command. See docs/LOCAL_SETUP.md for macOS/Linux setup." >&2; exit 1
  fi
done
if ! docker compose version >/dev/null 2>&1; then
  echo 'Docker Compose v2+ is required. Start Docker Desktop or Docker Engine.' >&2; exit 1
fi
if [[ ! -f .env ]]; then
  cp .env.example .env
  echo 'Created .env from .env.example (local Ollama only, no API key).'
fi
# Read only plain, active KEY=VALUE records; do not eval arbitrary .env input as shell code.
read_env() {
  local key=$1 default=$2 result
  result=$(awk -v k="$key" '
    $0 ~ "^[[:space:]]*"k"[[:space:]]*=" {
      sub("^[^=]*=", "", $0); sub(/\r$/, "", $0); print $0; exit
    }' .env)
  result=${result%\"}; result=${result#\"}; result=${result%\'}; result=${result#\'}
  printf '%s' "${result:-$default}"
}
CHAT_PROVIDER=$(read_env ASSISTANT_PROVIDER Ollama)
EMBED_PROVIDER=$(read_env DOCUMENTS_EMBEDDING_PROVIDER Ollama)
if [[ "$CHAT_PROVIDER" != Ollama || "$EMBED_PROVIDER" != Ollama ]]; then
  echo "Local starter needs Ollama for chat and embeddings (got $CHAT_PROVIDER / $EMBED_PROVIDER)." >&2
  echo 'Cloud mode: docker compose up -d --build --wait (without --profile ai).' >&2
  exit 1
fi
if [[ -n "$(read_env COMPOSE_PROFILES '')" ]]; then
  echo 'Remove COMPOSE_PROFILES from .env: the local script selects the ai profile explicitly.' >&2; exit 1
fi
CHAT_MODEL=$(read_env ASSISTANT_MODEL 'qwen3:4b-instruct')
EMBED_MODEL=$(read_env DOCUMENTS_EMBEDDING_MODEL 'bge-m3')
DIMS=$(read_env DOCUMENTS_EMBEDDING_DIMENSIONS '1024')
if [[ "$EMBED_MODEL" != bge-m3 || "$DIMS" != 1024 ]]; then
  echo 'The shipped pgvector schema expects bge-m3 (1024-dim). Other models require verified schema/reindex changes.' >&2
  exit 1
fi
printf 'Starting local Ollama...\n'
docker compose --profile ai up -d ollama
# On cold start, Ollama may be running but not yet ready to pull a model.
ready=0
for ((i = 0; i < 45; i++)); do
  if docker compose --profile ai exec -T ollama ollama list >/dev/null 2>&1; then ready=1; break; fi
  sleep 2
done
if [[ $ready -ne 1 ]]; then echo 'Ollama did not become ready; inspect docker compose logs ollama' >&2; exit 1; fi
printf 'Pulling chat model %s...\n' "$CHAT_MODEL"
docker compose --profile ai exec -T ollama ollama pull "$CHAT_MODEL"
printf 'Pulling multilingual embedding model %s...\n' "$EMBED_MODEL"
docker compose --profile ai exec -T ollama ollama pull "$EMBED_MODEL"
printf 'Starting PostgreSQL, EF migrator, Keycloak, API and Angular...\n'
docker compose --profile ai up -d --build --wait
if [[ "$SKIP_INDEX" -eq 0 ]]; then
  printf 'Ingesting authorized, synthetic multi-format documents...\n'
  bash scripts/ai-index.sh
fi
docker compose ps
printf '\nVirtualBank local demo: http://localhost:4200\n'
