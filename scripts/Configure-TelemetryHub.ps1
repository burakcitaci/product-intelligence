[CmdletBinding()]
param(
    [string]$KubeContext = 'kind-observability',
    [string]$Namespace = 'telemetry-hub',
    [string]$Organization = 'demo',
    [string]$Team = 'backend',
    [string]$Environment = 'development',
    [int]$ValidDays = 365
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $projectRoot '.env'
if (-not (Test-Path -LiteralPath $envPath)) {
    throw 'ProductHub .env was not found.'
}

$encodedRegistry = & kubectl --context $KubeContext -n $Namespace get secret telemetry-ingestion -o 'jsonpath={.data.INGESTION_KEYS_JSON}'
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($encodedRegistry)) {
    throw 'Unable to read the telemetry-hub ingestion registry.'
}

$registryJson = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encodedRegistry))
$parsedRegistry = $registryJson | ConvertFrom-Json
$registry = New-Object System.Collections.ArrayList
foreach ($record in @($parsedRegistry)) {
    if ($record.PSObject.Properties.Name -contains 'value') {
        foreach ($nestedRecord in @($record.value)) { [void]$registry.Add($nestedRecord) }
    }
    elseif ($record.PSObject.Properties.Name -contains 'keyHash') {
        [void]$registry.Add($record)
    }
}

# Replace any previous key for this exact ownership scope. This avoids leaving
# inaccessible active keys behind after local rotations or interrupted setup.
for ($index = $registry.Count - 1; $index -ge 0; $index--) {
    $record = $registry[$index]
    $recordEnvironment = if ($record.PSObject.Properties.Name -contains 'env') { $record.env } else { $null }
    if ($record.orgSlug -eq $Organization -and $record.teamId -eq $Team -and $recordEnvironment -eq $Environment) {
        $registry.RemoveAt($index)
    }
}

$keyBytes = New-Object byte[] 32
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $random.GetBytes($keyBytes) } finally { $random.Dispose() }
$rawKey = [Convert]::ToBase64String($keyBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$sha256 = [Security.Cryptography.SHA256]::Create()
try { $hashBytes = $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($rawKey)) } finally { $sha256.Dispose() }
$keyHash = ([BitConverter]::ToString($hashBytes) -replace '-', '').ToLowerInvariant()
$expiresAt = [DateTimeOffset]::UtcNow.AddDays($ValidDays).ToUnixTimeMilliseconds()

[void]$registry.Add([pscustomobject]@{
    keyHash = $keyHash
    orgSlug = $Organization
    teamId = $Team
    expiresAt = $expiresAt
    revoked = $false
    env = $Environment
})

$registryArray = [object[]]$registry
$updatedJson = ConvertTo-Json -InputObject $registryArray -Depth 8 -Compress
$updatedBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($updatedJson))
$patch = @{ data = @{ INGESTION_KEYS_JSON = $updatedBase64 } } | ConvertTo-Json -Depth 4 -Compress

$patchFile = New-TemporaryFile
try {
    $patch | Set-Content -LiteralPath $patchFile.FullName -Encoding utf8
    & kubectl --context $KubeContext -n $Namespace patch secret telemetry-ingestion --type merge --patch-file $patchFile.FullName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to update the telemetry-hub ingestion registry.'
    }
}
finally {
    Remove-Item -LiteralPath $patchFile.FullName -Force -ErrorAction SilentlyContinue
}

$telemetryNames = @(
    'OTEL_EXPORTER_OTLP_ENDPOINT',
    'OTEL_EXPORTER_OTLP_HEADERS',
    'OTEL_EXPORTER_OTLP_PROTOCOL',
    'OTEL_SERVICE_NAME'
)
$existingLines = Get-Content -LiteralPath $envPath | Where-Object {
    $line = $_
    -not ($telemetryNames | Where-Object { $line -match "^$([regex]::Escape($_))=" })
}
$updatedLines = @($existingLines) + @(
    '',
    'OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:3001/api/ingest',
    "OTEL_EXPORTER_OTLP_HEADERS=x-api-key=$rawKey",
    'OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf',
    'OTEL_SERVICE_NAME=product-hub-api'
)
$updatedLines | Set-Content -LiteralPath $envPath -Encoding utf8

& kubectl --context $KubeContext -n $Namespace rollout restart deployment/backend | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to restart the telemetry-hub backend.'
}
& kubectl --context $KubeContext -n $Namespace rollout status deployment/backend --timeout=120s
if ($LASTEXITCODE -ne 0) {
    throw 'The telemetry-hub backend rollout did not complete.'
}

Write-Host 'Telemetry-hub ingestion key registered and ProductHub .env configured.' -ForegroundColor Green
Write-Host "Scope: $Organization / $Team / $Environment"
Write-Host "Expires: $([DateTimeOffset]::FromUnixTimeMilliseconds($expiresAt).ToString('u'))"
Write-Host 'The raw key was written only to ProductHub .env and was not printed.'
