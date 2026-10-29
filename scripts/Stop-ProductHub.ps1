[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$stateFile = Join-Path $projectRoot '.run\producthub-processes.json'

if (Test-Path -LiteralPath $stateFile) {
    $state = Get-Content -Raw -LiteralPath $stateFile | ConvertFrom-Json
    foreach ($processId in @($state.BackendPid, $state.FrontendPid)) {
        if ($processId) {
            & taskkill.exe /PID $processId /T /F 2>$null | Out-Null
        }
    }
    Remove-Item -LiteralPath $stateFile -Force
}

Set-Location -LiteralPath $projectRoot
docker compose stop api | Out-Null

Write-Host 'Backend and frontend stopped.' -ForegroundColor Green
Write-Host 'SQL Server and Qdrant are still running. Use docker compose down if you also want to stop them.'
