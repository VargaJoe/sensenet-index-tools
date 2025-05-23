#!/usr/bin/env pwsh

# Example script for running subtree index check and generating a report
# Usage: ./CheckSubtree.ps1 [indexPath] [connectionString] [repositoryPath]
param(
    [Parameter(Mandatory=$true)]
    [string]$indexPath,
    
    [Parameter(Mandatory=$true)]
    [string]$connectionString,
    
    [Parameter(Mandatory=$true)]
    [string]$repositoryPath,
    
    [Parameter(Mandatory=$false)]
    [bool]$recursive = $true,
    
    [Parameter(Mandatory=$false)]
    [string]$outputPath = ""
)

# Default output path if not specified
if ([string]::IsNullOrEmpty($outputPath)) {
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $sanitizedPath = $repositoryPath -replace '[\\\/\:\*\?"<>\|]', '_'
    $outputPath = "subtree_check_${sanitizedPath}_${timestamp}.md"
}

# Build command arguments
$commandArgs = @(
    "run", 
    "--", 
    "check-subtree", 
    "--index-path", $indexPath, 
    "--connection-string", $connectionString, 
    "--repository-path", $repositoryPath
)

if (-not $recursive) {
    $commandArgs += "--recursive"
    $commandArgs += "false"
}

$commandArgs += "--detailed"
$commandArgs += "--output"
$commandArgs += $outputPath

# Display info
Write-Host "Starting subtree index check:"
Write-Host "  Index path: $indexPath"
Write-Host "  Repository path: $repositoryPath"
Write-Host "  Recursive: $recursive"
Write-Host "  Report will be saved to: $outputPath"
Write-Host ""

# Run the command
try {
    & dotnet $commandArgs
    
    if ($LASTEXITCODE -eq 0 -and (Test-Path $outputPath)) {
        Write-Host ""
        Write-Host "Check completed successfully. Report saved to: $outputPath"
        
        # Optionally open the report
        $openReport = Read-Host "Would you like to open the report now? (y/n)"
        if ($openReport -eq "y") {
            Start-Process $outputPath
        }
    } else {
        Write-Host "Check failed or report was not generated properly."
    }
} catch {
    Write-Error "An error occurred while running the command: $_"
}
