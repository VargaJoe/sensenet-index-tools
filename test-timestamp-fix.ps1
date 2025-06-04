#!/usr/bin/env pwsh

# Variables
$TestIndex = "d:\devgit\joe\!TestIndex\"
# Update connection string to use integrated security (Windows Authentication)
$TestDb = "Server=localhost;Database=SenseNet;Integrated Security=True;"
$RepositoryPath = "/Root"

# Run a targeted test for the specific example path mentioned with the issue
Write-Host "Running a targeted test with the specific path where the issue was observed..."
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj compare `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path "/Root/Content/KELERData/Hírcenter/Hírek/2016" `
    --recursive false `
    --verbose

# Run a small sample from Root for comparison
Write-Host "Running a small sample test from Root..."
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj compare `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path "/Root/System" `
    --recursive false `
    --verbose

Write-Host "Done! Check the console output for timestamp debugging information."
