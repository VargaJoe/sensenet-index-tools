#!/usr/bin/env pwsh

# This script verifies the timestamp fix in the SenseNet Index Maintenance Tool

Write-Host "Verifying timestamp comparison fix..." -ForegroundColor Green

# Define test parameters
$TestIndex = ".\TestIndex\"  # Replace with your index path
$TestDb = "Server=.;Database=YourDatabase;Trusted_Connection=True;"  # Replace with your connection string
$TestPath = "/Root/Content/ExamplePath"  # Replace with your repository path
$HtmlOutput = "timestamp-fix-verification.html"
$CsvOutput = "timestamp-fix-verification.csv"

# Run the compare command with the fixed timestamp handling
Write-Host "Running timestamp comparison test..." -ForegroundColor Yellow
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj compare `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $TestPath `
    --recursive false `
    --verbose `
    --output $HtmlOutput

# Run a SQL query to directly check actual timestamp values for verification
Write-Host "Running SQL query to verify timestamp values..." -ForegroundColor Yellow
sqlcmd -E -S . -d YourDatabase -Q "SELECT TOP 20 N.NodeId, V.VersionId, N.Path, 
       CAST(V.Timestamp as bigint) as TimestampNumeric, 
       CONVERT(varchar, V.Timestamp, 121) as TimestampFormatted
FROM Nodes N 
JOIN Versions V ON N.NodeId = V.NodeId
JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
WHERE LOWER(N.Path) LIKE LOWER('$TestPath%')
ORDER BY N.Path" -o $CsvOutput -s "," -W

Write-Host "Verification complete!" -ForegroundColor Green
Write-Host "Review the following files to confirm the fix is working:" -ForegroundColor Green
Write-Host "1. $HtmlOutput - HTML report showing timestamp comparison results" -ForegroundColor Cyan
Write-Host "2. $CsvOutput - CSV with direct SQL query results for verification" -ForegroundColor Cyan

# Summarize the fix
Write-Host "`nTIMESTAMP FIX SUMMARY:" -ForegroundColor Green
Write-Host "------------------------------------------------------" -ForegroundColor Green
Write-Host "The timestamp mismatch issue has been fixed by:" -ForegroundColor White
Write-Host "1. Retrieving the timestamp as a numeric value (bigint) from SQL" -ForegroundColor White
Write-Host "2. Storing this value in the ContentItem.TimestampNumeric property" -ForegroundColor White
Write-Host "3. Comparing numeric timestamp values directly using STRICT equality in the Status property" -ForegroundColor White
Write-Host "   - Any difference in timestamp values, no matter how small, is reported as a mismatch" -ForegroundColor White
Write-Host "4. Adding fallback logic to prioritize ID matches ONLY when timestamp data is incomplete" -ForegroundColor White
Write-Host "5. Adding debug output to help diagnose timestamp comparison issues" -ForegroundColor White
Write-Host "------------------------------------------------------" -ForegroundColor Green

Write-Host "`nNEXT STEPS:" -ForegroundColor Yellow
Write-Host "1. Review the HTML report in $HtmlOutput to verify timestamp comparison results" -ForegroundColor White
Write-Host "2. Compare the numeric timestamp values in the report to validate strict matching" -ForegroundColor White
Write-Host "3. Check the CSV file in $CsvOutput for direct SQL timestamp values" -ForegroundColor White
Write-Host "4. Any items with different timestamp values should be marked as 'Timestamp mismatch'" -ForegroundColor White
Write-Host "5. Run additional tests with --verbose flag for more detailed timestamp debugging" -ForegroundColor White
Write-Host "------------------------------------------------------" -ForegroundColor Green
