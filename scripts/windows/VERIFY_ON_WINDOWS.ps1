# Read-only validation. Does not reset volumes or modify Keycloak/PostgreSQL data.
param([switch]$SkipDocker)
$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
function Check([string]$Label, [scriptblock]$Action) {
    Write-Host "`n==> $Label" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Label failed (exit code $LASTEXITCODE)" }
}
Check 'NuGet restore' { dotnet restore .\VirtualBank.slnx }
Check 'Release build' { dotnet build .\VirtualBank.slnx -c Release --no-restore }
Check 'Real .NET unit/architecture tests' { dotnet test .\VirtualBank.slnx -c Release --no-build }
if (-not $SkipDocker) {
    Check 'Docker Compose config' { docker compose config --quiet }
    Check 'Docker API + frontend build and health' { docker compose up -d --build --wait }
    Check 'Container status' { docker compose ps }
}
Write-Host "`nLocal checks executed. Test real Ollama function calling separately (mode must be ollama)." -ForegroundColor Green
