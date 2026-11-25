# USE ONLY FOR SYNTHETIC DEVELOPMENT DATA. Creates a backup before a deliberate rebuild.
param([switch]$ConfirmReset)
$ErrorActionPreference='Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $ConfirmReset) {
    throw 'Not resetting anything. Re-run with -ConfirmReset only after reviewing the documentation.'
}
if (-not (Test-Path .env)) { throw 'Copy .env.example to .env and configure your actual provider key before first start.' }
$backupDir=Join-Path (Get-Location).Path 'backups'
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
$backup=Join-Path $backupDir ("virtualbank-legacy-"+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.sql')
Write-Host 'Backing up ALL databases/roles in the Compose postgres container...'
docker compose exec -T postgres pg_dumpall -U postgres | Out-File -FilePath $backup -Encoding utf8
if ($LASTEXITCODE -ne 0 -or (Get-Item $backup).Length -lt 4096) {
    throw 'pg_dumpall failed or backup too small. No volumes have been removed.'
}
Write-Host "SQL backup created at $backup"
Write-Host 'Stopping ONLY this Compose project (not other apps).'
docker compose down
if ($LASTEXITCODE -ne 0) { throw 'Compose down failed. Volume untouched.' }
$volume='virtual-bank-agent_postgres-data'
if (-not (docker volume ls --format '{{.Name}}' | Where-Object { $_ -eq $volume })) {
    throw "Expected volume $volume not found. No other volumes touched."
}
Write-Warning "Removing $volume ONLY. Data is backed up at $backup and NOT restored automatically."
docker volume rm $volume
if ($LASTEXITCODE -ne 0) { throw 'Volume remove failed.' }
docker compose up -d --build --wait
if ($LASTEXITCODE -ne 0) { throw 'EF installation failed; inspect docker compose logs db-migrate.' }
docker compose ps
Write-Host 'EF Core-first fresh demo complete. Previous dataset retained only in backup, not restored.'
