# Local Ollama-first VirtualBank bootstrap (Windows PowerShell; no make/WSL/API key).
# Run from anywhere: .\scripts\windows\start-local.ps1 [-SkipIndex] [-NativeOllama]
# Default: explicitly starts the opt-in Docker `ai` profile (Ollama in a container).
# -NativeOllama: uses an Ollama installed directly on Windows (https://ollama.com/download) instead of the
#   container. CPU inference inside Docker/WSL can be several times slower than native; compare with
#   the AssistantTurnTiming lines in `docker compose logs api`. Nothing in .env is changed: the endpoints are
#   overridden for this run only, so a later run without the switch returns to the container setup.
# DOES NOT run docker compose down -v.
param([switch]$SkipIndex, [switch]$NativeOllama)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$original = Get-Location
try {
    Set-Location $repo
    if (-not (Test-Path '.env')) {
        Copy-Item '.env.example' '.env'
        Write-Host 'Created .env from .env.example (local Ollama defaults, no API key).'
    }
    $settings = @{}
    Get-Content '.env' -Encoding UTF8 | ForEach-Object {
        $line = $_.Trim()
        if ($line -match '^([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
            $settings[$Matches[1]] = $Matches[2].Trim().Trim('"').Trim("'")
        }
    }
    function Config([string]$key, [string]$fallback) {
        if ($settings.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($settings[$key])) {
            return $settings[$key]
        }
        return $fallback
    }
    $chatProvider = Config 'ASSISTANT_PROVIDER' 'Ollama'
    $embedProvider = Config 'DOCUMENTS_EMBEDDING_PROVIDER' 'Ollama'
    if ($chatProvider -ne 'Ollama' -or $embedProvider -ne 'Ollama') {
        throw "The local starter requires both providers=Ollama; found chat=$chatProvider and embedding=$embedProvider. For cloud providers run 'docker compose up -d --build --wait' WITHOUT --profile ai."
    }
    $chat = Config 'ASSISTANT_MODEL' 'qwen3:4b-instruct'
    $embed = Config 'DOCUMENTS_EMBEDDING_MODEL' 'bge-m3'
    if ($embed -ne 'bge-m3') {
        throw "Unsupported local 1024-dim embedding model: $embed"
    }
    $profiles = Config 'COMPOSE_PROFILES' ''
    if ($profiles) { Write-Warning 'COMPOSE_PROFILES is set in .env. Cloud users must remove it to avoid unwanted Ollama startup.' }

    if ($NativeOllama) {
        if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
            throw 'Native Ollama not found. Install it from https://ollama.com/download (or run without -NativeOllama).'
        }
        try { Invoke-RestMethod -Uri 'http://localhost:11434/api/tags' -TimeoutSec 5 | Out-Null }
        catch {
            throw 'Native Ollama is installed but not answering on http://localhost:11434. Start the Ollama app first. If the Docker Ollama container holds the port, stop it: docker compose --profile ai stop ollama'
        }
        # The container would otherwise compete for port 11434 and memory. Stop only; volumes are kept.
        & cmd.exe /D /C 'docker compose --profile ai stop ollama 1>NUL 2>NUL'
        Write-Host "Pulling models into native Ollama: $chat, $embed"
        ollama pull $chat
        if ($LASTEXITCODE -ne 0) { throw "Failed to pull $chat" }
        ollama pull $embed
        if ($LASTEXITCODE -ne 0) { throw "Failed to pull $embed" }
        # Process-scoped overrides; Compose gives shell variables precedence over .env.
        $env:ASSISTANT_ENDPOINT = 'http://host.docker.internal:11434/v1/'
        $env:DOCUMENTS_EMBEDDING_ENDPOINT = 'http://host.docker.internal:11434/'
        Write-Host 'Building and starting EF migrator, Keycloak, API, Angular and databases (native Ollama)...'
        docker compose up -d --build --wait
        if ($LASTEXITCODE -ne 0) { throw 'Docker Compose failed. Check: docker compose logs --tail=100 db-migrate api keycloak' }
        docker compose ps
        if (-not $SkipIndex) {
            Write-Host 'Indexing synthetic documents with native Ollama embeddings...'
            & (Join-Path $repo 'scripts/windows/INDEX_DOCUMENTS_WINDOWS.ps1')
            if (-not $?) { throw 'Document ingestion failed; check API and Keycloak logs.' }
        }
        Write-Host 'Local VirtualBank starter completed with native Ollama. Open http://localhost:4200'
        return
    }

    Write-Host 'Starting the optional Ollama service (one-time Docker image download possible)...'
    docker compose --profile ai up -d ollama
    if ($LASTEXITCODE -ne 0) { throw 'Failed to start Ollama.' }
    # Windows PowerShell 5.1 turns native stderr into a terminating
    # NativeCommandError when ErrorActionPreference=Stop.  A cold Ollama
    # startup normally emits connection errors until the server is ready.
    # Run the readiness probe through cmd.exe to suppress only the expected
    # transient errors; check its real exit code instead of aborting early.
    $ready = $false
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        & cmd.exe /D /C 'docker compose --profile ai exec -T ollama ollama list 1>NUL 2>NUL'
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        if ($attempt % 10 -eq 0) {
            Write-Host 'Waiting for the Ollama server to accept requests...'
        }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) {
        Write-Host 'Ollama startup failed or timed out. Recent logs:' -ForegroundColor Yellow
        & docker compose --profile ai logs --tail=60 ollama
        throw 'Ollama did not become ready. Check Docker resources, port 11434, and the Ollama container logs.'
    }
    Write-Host "Downloading the local tool-calling model: $chat"
    docker compose --profile ai exec -T ollama ollama pull $chat
    if ($LASTEXITCODE -ne 0) { throw "Failed to pull $chat" }
    Write-Host "Downloading the local RAG embedding model: $embed"
    docker compose --profile ai exec -T ollama ollama pull $embed
    if ($LASTEXITCODE -ne 0) { throw "Failed to pull $embed" }
    Write-Host 'Building and starting EF migrator, Keycloak, API, Angular and databases...'
    docker compose --profile ai up -d --build --wait
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose failed. Check: docker compose logs --tail=100 db-migrate api keycloak' }
    docker compose ps
    if (-not $SkipIndex) {
        Write-Host 'Indexing synthetic PDF/DOCX/TXT/JSON/MD documents with a real local embedding model...'
        & (Join-Path $repo 'scripts/windows/INDEX_DOCUMENTS_WINDOWS.ps1')
        if (-not $?) { throw 'Document ingestion failed; check API and Keycloak logs.' }
    }
    Write-Host 'Local VirtualBank starter completed. Open http://localhost:4200'
} finally {
    Set-Location $original
}
