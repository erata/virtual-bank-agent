# Reference-project release gate: run from the repository root in PowerShell.
# Never resets a database and never prints API keys. A failed step stops the script.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$originalLocation = Get-Location
Set-Location $repoRoot
try {

function Exec([string] $exe, [string[]] $commandArgs) {
    Write-Host "=> $exe $($commandArgs -join ' ')"
    & $exe @commandArgs
    if ($LASTEXITCODE -ne 0) { throw "Failed ($LASTEXITCODE): $exe $($commandArgs -join ' ')" }
}

Exec python @('tools/publication_guard.py')
Exec dotnet @('restore','VirtualBank.slnx')
Exec dotnet @('run','--project','tools/VirtualBank.SeedGenerator','--','--check')
Exec dotnet @('build','VirtualBank.slnx','-c','Release','--no-restore')
Exec dotnet @('test','VirtualBank.slnx','-c','Release','--no-build')

# Install dotnet-ef 10.0.4 once if absent: dotnet tool install --global dotnet-ef --version 10.0.4
$ef = Get-Command dotnet-ef -ErrorAction SilentlyContinue
if (-not $ef) { throw 'dotnet-ef is missing. Run: dotnet tool install --global dotnet-ef --version 10.0.4' }
foreach ($context in @('CustomerDbContext','CreditDbContext','DocumentsDbContext')) {
    Exec dotnet @('ef','migrations','has-pending-model-changes','--context',$context,
        '--project','src/VirtualBank.Infrastructure','--startup-project','src/VirtualBank.Infrastructure',
        '--configuration','Release','--no-build')
    $migrationSql = Join-Path ([System.IO.Path]::GetTempPath()) "VirtualBank-$context-migration.sql"
    try {
        Exec dotnet @('ef','migrations','script','--idempotent',
            '--context',$context,'--project','src/VirtualBank.Infrastructure',
            '--startup-project','src/VirtualBank.Infrastructure',
            '--configuration','Release','--no-build','--output',$migrationSql)
        if (-not (Test-Path $migrationSql) -or (Get-Item $migrationSql).Length -lt 100) {
            throw "EF migration script was not generated for $context"
        }
    } finally { Remove-Item $migrationSql -ErrorAction SilentlyContinue }
}
Write-Host "PASS: local preflight, seed check, .NET build/tests, EF model-drift and SQL generation."
Write-Host 'NEXT: run real Compose + RLS + Keycloak + UI/Bruno + RAG evaluation before publishing.'

} finally { Set-Location $originalLocation }
