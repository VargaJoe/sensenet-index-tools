#!/usr/bin/env pwsh

# Set the path to test with a specific item to diagnose the timestamp issue
$TestPath = "/Root/Content/KELERData/Hírcenter/Hírek/2016"
$TestNodeId = "102928"
$OutputFile = "timestamp-specific-test.html"

# Run a targeted test with specific ID
Write-Host "Running a targeted test for specific NodeId $TestNodeId..."
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path "d:\devgit\joe\!TestIndex\" `
    --connection-string "Server=localhost;Database=SenseNet;Trusted_Connection=True;" `
    --repository-path $TestPath `
    --output $OutputFile `
    --format html `
    --report-format detailed `
    --verbose

Write-Host "Done! Check the '$OutputFile' file for results."

# Now run a direct SQL query to compare with our tool results
Write-Host "Running SQL query to check actual timestamp values..."
sqlcmd -E -S localhost -d SenseNet -Q "SELECT N.NodeId, V.VersionId, 
       CAST(V.Timestamp as bigint) as TimestampBigint, 
       CONVERT(varchar, V.Timestamp, 121) as TimestampFormatted
FROM Nodes N 
JOIN Versions V ON N.NodeId = V.NodeId
WHERE N.NodeId = $TestNodeId
ORDER BY N.NodeId, V.VersionId;"

Write-Host "Test complete. Compare the SQL output with the tool results."
