#!/usr/bin/env pwsh

# Script for running subtree index check and generating a report
# This script checks if items from a SenseNet database subtree exist in a Lucene search index
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
    [string]$outputPath = "",
    
    [Parameter(Mandatory=$false)]
    [bool]$detailed = $true,
    
    [Parameter(Mandatory=$false)]
    [switch]$openReport
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
    "--project", "src/MainProgram/sn-index-maintenance-suite.csproj",
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

if ($detailed) {
    $commandArgs += "--detailed"
}

$commandArgs += "--output"
$commandArgs += $outputPath

# Display info
Write-Host "Starting subtree index check:" -ForegroundColor Cyan
Write-Host "  Index path: $indexPath"
Write-Host "  Repository path: $repositoryPath"
Write-Host "  Recursive: $recursive"
Write-Host "  Detailed report: $detailed"
Write-Host "  Report will be saved to: $outputPath"
Write-Host ""

# Run the command
try {
    & dotnet $commandArgs
    
    if ($LASTEXITCODE -eq 0 -and (Test-Path $outputPath)) {
        Write-Host ""
        Write-Host "Check completed successfully." -ForegroundColor Green
        Write-Host "Report saved to: $outputPath" -ForegroundColor Green
        
        # Automatically open the report if specified
        if ($openReport) {
            Write-Host "Opening report..." -ForegroundColor Cyan
            Start-Process $outputPath
        } else {
            # Optionally offer to open the report
            $userOpenReport = Read-Host "Would you like to open the report now? (y/n)"
            if ($userOpenReport -eq "y") {
                Start-Process $outputPath
            }
        }
    } else {
        Write-Host "Check failed or report was not generated properly." -ForegroundColor Red
    }
} catch {
    Write-Error "An error occurred while running the command: $_"
}
