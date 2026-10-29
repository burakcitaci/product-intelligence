[CmdletBinding()]
param(
    [Parameter(Mandatory)] [Guid] $ProductId,
    [string] $ApiBaseUrl = 'http://localhost:5099',
    [ValidateSet('de', 'en')] [string] $Language = 'de'
)

$headers = @{ 'Idempotency-Key' = "manual-$([Guid]::NewGuid().ToString('N'))" }
$body = @{ language = $Language } | ConvertTo-Json
$draft = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/products/$ProductId/drafts/generate" `
    -Headers $headers -ContentType 'application/json' -Body $body
$draft | ConvertTo-Json -Depth 5
