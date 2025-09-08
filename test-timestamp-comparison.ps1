#!/usr/bin/env pwsh

# This script tests the timestamp comparison fix in the SenseNet Index Maintenance Tool
# It focuses on displaying the numeric timestamp values to help debug comparison issues

# Variables
$TestIndex = ""
# $TestDb = "Server=localhost;Database=SenseNet;Trusted_Connection=True;"
$TestPath = "/Root/Content"
$OutputFile = "timestamp-comparison-results.html"

Write-Host "Testing timestamp comparison fix..." -ForegroundColor Green

Write-Host "Running compare with timestamp numeric values displayed..."
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj compare `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $TestPath `
    --recursive false `
    --verbose `
    --output $OutputFile

Write-Host "Done! Check console output and $OutputFile for results." -ForegroundColor Green

# Now run SQL query to directly show timestamp values for comparison
Write-Host "Fetching raw timestamp values from SQL for comparison..." -ForegroundColor Cyan
sqlcmd -E -S localhost -d SenseNet -Q "SELECT TOP 10 N.NodeId, V.VersionId, N.Path, 
       CAST(V.Timestamp as bigint) as TimestampNumeric, 
       CONVERT(varchar, V.Timestamp, 121) as TimestampFormatted
FROM Nodes N 
JOIN Versions V ON N.NodeId = V.NodeId
JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
WHERE LOWER(N.Path) LIKE LOWER('$TestPath%')
ORDER BY N.Path"

Write-Host "Test complete. Compare the SQL values with the values shown in the tool output." -ForegroundColor Green
