[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $projectRoot '.env'
$webRoot = Join-Path $projectRoot 'src\ProductHub.Web'
$runRoot = Join-Path $projectRoot '.run'
$stateFile = Join-Path $runRoot 'producthub-processes.json'

if (-not (Test-Path -LiteralPath $envFile)) {
    throw "Missing .env. Copy .env.example to .env and set MSSQL_SA_PASSWORD."
}

Get-Content -LiteralPath $envFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#')) {
        $parts = $line -split '=', 2
        if ($parts.Count -eq 2) {
            [Environment]::SetEnvironmentVariable($parts[0].Trim(), $parts[1].Trim(), 'Process')
        }
    }
}

if ([string]::IsNullOrWhiteSpace($env:MSSQL_SA_PASSWORD)) {
    throw 'MSSQL_SA_PASSWORD is missing from .env.'
}

foreach ($command in @('docker', 'node', 'npm')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' was not found."
    }
}

Set-Location -LiteralPath $projectRoot
Write-Host 'Building and starting API, SQL Server, and Qdrant...' -ForegroundColor Cyan
if ($SkipBuild) {
    docker compose up -d --wait
} else {
    docker compose up -d --build --wait
}
if ($LASTEXITCODE -ne 0) { throw 'Docker services did not start successfully.' }

if (-not (Test-Path -LiteralPath (Join-Path $webRoot 'node_modules'))) {
    Write-Host 'Installing frontend packages...' -ForegroundColor Cyan
    Push-Location -LiteralPath $webRoot
    try { npm install } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'Frontend package installation failed.' }
}

New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$webOut = Join-Path $runRoot 'frontend.log'
$webErr = Join-Path $runRoot 'frontend-error.log'

Write-Host 'Starting frontend...' -ForegroundColor Cyan
$web = Start-Process -FilePath 'npm.cmd' -ArgumentList @('run', 'dev', '--', '--host', '127.0.0.1') -WorkingDirectory $webRoot -WindowStyle Hidden -RedirectStandardOutput $webOut -RedirectStandardError $webErr -PassThru

Start-Sleep -Seconds 3
if ($web.HasExited) { throw "Frontend stopped during startup. See $webErr" }

@{ FrontendPid = $web.Id; StartedAt = (Get-Date).ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8

Write-Host ''
Write-Host 'Product Hub is running.' -ForegroundColor Green
Write-Host 'Frontend: http://localhost:5173'
Write-Host 'Backend:  http://localhost:5099'
Write-Host "Frontend logs: $runRoot"
Write-Host 'Backend logs:  docker compose logs -f api'
Write-Host 'Stop with: .\scripts\Stop-ProductHub.ps1'
