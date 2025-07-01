#!/usr/bin/env pwsh

# Variables
$TestIndex = ""
# Use a trusted connection instead of specific database
$TestDb = "Server=localhost;Database=SenseNet;Trusted_Connection=True;"
$RepositoryPath = "/Root"

# Set a specific path to test with real content
$TestPath = "/Root/Content/Sites"
$OutputFile = "timestamp-fix-test.html"

# Run a targeted test with timestamp debugging
Write-Host "Running a targeted test with timestamp debugging..."
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $TestPath `
    --output $OutputFile `
    --report-format detailed `
    --format html `
    --verbose

Write-Host "Done! Check the '$OutputFile' file for results."

# Test with a more specific path to reduce the number of items and focus debugging
Write-Host "Running a focused test with a specific path..."
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path "/Root/Content" `
    --recursive false `
    --verbose

Write-Host "Test complete. Check the console output for timestamp debug information."
