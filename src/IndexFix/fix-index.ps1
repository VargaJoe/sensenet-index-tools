Param(
  [Parameter(Mandatory = $false)]
  [string]$contentPath = "/Root/Content",
  [Parameter(Mandatory = $false)]
  [string]$ContentId,
  [Parameter(Mandatory = $true)]
  [string]$repoUrl,
  [Parameter(Mandatory = $true)]
  [string]$apiKey,
  [Parameter(Mandatory = $false)]
  [string]$action = "RebuildIndex",
  [Parameter(Mandatory=$False)]
  [string]$recursive = "false",
  [Parameter(Mandatory=$False)]
  [string]$rebuildLevel = "IndexOnly"
)


$ErrorActionPreference = "Stop"
$baseUri = [Uri]$repoUrl
if (!$baseUri.IsAbsoluteUri -or $baseUri.Scheme -notin @("https", "http") -or $baseUri.UserInfo -or $baseUri.Query -or $baseUri.Fragment) { throw "repoUrl must be an absolute HTTP(S) URL without credentials, query or fragment." }
$repoUrl = $repoUrl.TrimEnd('/')
if ($ContentId) {
  $id = 0
  if (![int]::TryParse($ContentId, [ref]$id) -or $id -le 0) { throw "ContentId must be a positive Int32." }
  $actionUrl = "$repoUrl/odata.svc/Content($id)/$action"
} else {
  $segments = $contentPath.TrimEnd('/') -split '/' | Where-Object { $_ }
  if (!$segments -or $segments[0] -cne "Root") { throw "contentPath must start with /Root." }
  $name = [Uri]::EscapeDataString($segments[-1].Replace("'", "''"))
  $parent = if ($segments.Count -gt 1) { ($segments[0..($segments.Count - 2)] | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/' } else { "" }
  $target = if ($parent) { "$parent('$name')" } else { "('$name')" }
  $actionUrl = "$repoUrl/odata.svc/$target/$action"
}

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$session.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0"

$payload = "{}"
if ($action -eq "RebuildIndex") {
  $payload = @{ Recursive = [bool]::Parse($recursive); RebuildLevel = $rebuildLevel } | ConvertTo-Json -Compress
}

Write-Verbose "actionUrl: $($actionUrl)"
Write-Verbose "payload: $($payload)"

$response = Invoke-WebRequest -UseBasicParsing -Uri "$($actionUrl)" `
-Method "POST" `
-WebSession $session `
-Headers @{
  "apikey"=$apiKey
} `
-ContentType "application/json;charset=UTF-8" `
-Body "$($payload)"
Write-Verbose "statuscode: $($response.StatusCode)"

if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
  Write-Error "Failed to rebuild index"
  exit 1
}
