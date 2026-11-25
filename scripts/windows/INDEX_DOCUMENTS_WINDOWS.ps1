# Runs ingestion against the configured chat/embedding providers (Ollama OR cloud).
# Chat and embedding provider selection comes from .env; Ollama is the local default.
# The service-account token is never printed. This script does not reset databases.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$envFile = Join-Path $repo '.env'
if (-not (Test-Path $envFile)) { throw 'Missing .env. Copy .env.example, then start the configured model provider first.' }
$settings = @{}
Get-Content $envFile -Encoding UTF8 | ForEach-Object {
    $line = $_.Trim()
    if ($line -match '^([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
        $settings[$Matches[1]] = $Matches[2].Trim().Trim('"').Trim("'")
    }
}
function Setting([string]$name, [string]$fallback) {
    if ($settings.ContainsKey($name) -and -not [string]::IsNullOrWhiteSpace($settings[$name])) {
        return $settings[$name]
    }
    return $fallback
}
$keycloakPort = Setting 'KEYCLOAK_PORT' '8080'
$apiPort = Setting 'API_PORT' '8081'
$realm = Setting 'KEYCLOAK_REALM' 'virtual-bank'
$serviceId = Setting 'KEYCLOAK_INGESTION_CLIENT' 'svc-ingestion'
$serviceSecret = Setting 'KEYCLOAK_INGESTION_SECRET' 'svc-ingestion-dev-only-secret'
Write-Host 'Requesting LOCAL Keycloak machine token (token not printed)...'
$auth = Invoke-RestMethod -Method Post -Uri "http://localhost:$keycloakPort/realms/$realm/protocol/openid-connect/token" `
    -ContentType 'application/x-www-form-urlencoded' -Body @{
        grant_type='client_credentials'; client_id=$serviceId; client_secret=$serviceSecret
    }
if (-not $auth.access_token) { throw 'Keycloak did not issue an access token for svc-ingestion.' }
Write-Host 'Indexing authorized synthetic documents using the configured embedding provider...'
$result = Invoke-RestMethod -Method Post -Uri "http://localhost:$apiPort/api/documents/ingest" `
    -Headers @{Authorization="Bearer $($auth.access_token)"}
$result | ConvertTo-Json -Depth 10
Write-Host 'Ingestion request completed. Next: python tools/evaluate_rag.py --min-hit-rate 0.70'
