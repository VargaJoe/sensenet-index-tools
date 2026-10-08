Param(
  [Parameter(Mandatory = $true)]
  [string]$repoUrl,
  [Parameter(Mandatory = $true)]
  [string]$apiKey,
  [Parameter(Mandatory = $false)]
  [string]$filePath = (Join-Path $PSScriptRoot "fixTheseItems.txt")
)

$rowCount = 0
Get-Content $filePath -Encoding UTF8 | ForEach-Object {
  $item = $_.Trim()
  if (![string]::IsNullOrWhiteSpace($item) -and !$item.StartsWith("#")) {
    $rowCount = $rowCount + 1
    Write-Output "$rowCount`tInput: $item"
    if ($item -match '^/Root') {
      Write-Verbose "Calling fix-index.ps1 with ContentPath: $item"
      & (Join-Path $PSScriptRoot "fix-index.ps1") -contentPath $item -repoUrl $repoUrl -apiKey $apiKey
    } elseif ($item -match '^[1-9][0-9]*$') {
      Write-Verbose "Calling fix-index.ps1 with ContentId: $item"
      & (Join-Path $PSScriptRoot "fix-index.ps1") -ContentId $item -repoUrl $repoUrl -apiKey $apiKey
    } else {
      Write-Warning "Row $rowCount is not a valid ContentPath or ContentId: $item"
    }
  }
}
