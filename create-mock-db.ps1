#!/usr/bin/env pwsh

# This script creates a mock database and index for testing the timestamp handling fix
# It will use SQLite for the database to avoid connection issues

# Ensure we have the necessary NuGet packages
dotnet add src/MainProgram/sn-index-maintenance-suite.csproj package System.Data.SQLite

# Create a mock SQLite database
Add-Type -Path "$(dotnet nuget locals global-packages -l | ForEach-Object { $_.Split(' ')[1] })\system.data.sqlite\1.0.118\lib\netstandard2.0\System.Data.SQLite.dll"

$dbPath = ".\mock-sensenet.db"
$connString = "Data Source=$dbPath;Version=3;"

# Remove existing database if it exists
if (Test-Path $dbPath) {
    Remove-Item $dbPath -Force
}

# Create a new database
$conn = New-Object System.Data.SQLite.SQLiteConnection($connString)
$conn.Open()

# Create tables
$createNodesSql = @"
CREATE TABLE Nodes (
    NodeId INTEGER PRIMARY KEY,
    Path TEXT NOT NULL,
    NodeTypeId INTEGER NOT NULL
);
"@

$createVersionsSql = @"
CREATE TABLE Versions (
    VersionId INTEGER PRIMARY KEY,
    NodeId INTEGER NOT NULL,
    Timestamp BLOB NOT NULL
);
"@

$createNodeTypesSql = @"
CREATE TABLE NodeTypes (
    NodeTypeId INTEGER PRIMARY KEY,
    Name TEXT NOT NULL
);
"@

$cmd = $conn.CreateCommand()
$cmd.CommandText = $createNodesSql
$cmd.ExecuteNonQuery()

$cmd.CommandText = $createVersionsSql
$cmd.ExecuteNonQuery()

$cmd.CommandText = $createNodeTypesSql
$cmd.ExecuteNonQuery()

# Insert data
$insertNodeTypeSql = "INSERT INTO NodeTypes (NodeTypeId, Name) VALUES (@NodeTypeId, @Name);"
$cmd.CommandText = $insertNodeTypeSql
$cmd.Parameters.AddWithValue("@NodeTypeId", 1)
$cmd.Parameters.AddWithValue("@Name", "Document")
$cmd.ExecuteNonQuery()

$cmd.Parameters.Clear()
$cmd.Parameters.AddWithValue("@NodeTypeId", 2)
$cmd.Parameters.AddWithValue("@Name", "Folder")
$cmd.ExecuteNonQuery()

# Insert nodes
$insertNodeSql = "INSERT INTO Nodes (NodeId, Path, NodeTypeId) VALUES (@NodeId, @Path, @NodeTypeId);"
$cmd.CommandText = $insertNodeSql

# Root folder
$cmd.Parameters.Clear()
$cmd.Parameters.AddWithValue("@NodeId", 1)
$cmd.Parameters.AddWithValue("@Path", "/Root")
$cmd.Parameters.AddWithValue("@NodeTypeId", 2)
$cmd.ExecuteNonQuery()

# Documents folder
$cmd.Parameters.Clear()
$cmd.Parameters.AddWithValue("@NodeId", 2)
$cmd.Parameters.AddWithValue("@Path", "/Root/Documents")
$cmd.Parameters.AddWithValue("@NodeTypeId", 2)
$cmd.ExecuteNonQuery()

# Document 1
$cmd.Parameters.Clear()
$cmd.Parameters.AddWithValue("@NodeId", 3)
$cmd.Parameters.AddWithValue("@Path", "/Root/Documents/Doc1")
$cmd.Parameters.AddWithValue("@NodeTypeId", 1)
$cmd.ExecuteNonQuery()

# Insert versions
$insertVersionSql = "INSERT INTO Versions (VersionId, NodeId, Timestamp) VALUES (@VersionId, @NodeId, @Timestamp);"
$cmd.CommandText = $insertVersionSql

# Use a binary timestamp for testing our fix
$randomBytes = [byte[]]::new(8)
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($randomBytes)

$cmd.Parameters.Clear()
$cmd.Parameters.AddWithValue("@VersionId", 1)
$cmd.Parameters.AddWithValue("@NodeId", 1)
$cmd.Parameters.AddWithValue("@Timestamp", $randomBytes)
$cmd.ExecuteNonQuery()

$cmd.Parameters.Clear()
$cmd.Parameters.AddWithValue("@VersionId", 2)
$cmd.Parameters.AddWithValue("@NodeId", 2)
$cmd.Parameters.AddWithValue("@Timestamp", $randomBytes)
$cmd.ExecuteNonQuery()

$cmd.Parameters.Clear()
$cmd.Parameters.AddWithValue("@VersionId", 3)
$cmd.Parameters.AddWithValue("@NodeId", 3)
$cmd.Parameters.AddWithValue("@Timestamp", $randomBytes)
$cmd.ExecuteNonQuery()

$conn.Close()

Write-Host "Mock SQLite database created at $dbPath"
Write-Host "Connection string: $connString"

# Create a mock index directory
$indexDir = ".\mock-index"
if (-not (Test-Path $indexDir)) {
    New-Item -ItemType Directory -Path $indexDir -Force
}

# Update the test script to use our mock database and index
$testScript = @"
#!/usr/bin/env pwsh

# Variables
`$TestIndex = "$indexDir"
`$TestDb = "$connString"
`$RepositoryPath = "/Root"

# Run the subtree index checker
Write-Host "Running subtree index checker with timestamp handling fixes..."
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path `$TestIndex `
    --connection-string `$TestDb `
    --repository-path `$RepositoryPath `
    --output "timestamp-fix-check.html" `
    --report-format detailed `
    --format html

Write-Host "Done! Check the 'timestamp-fix-check.html' file for results."
"@

Set-Content -Path ".\test-mock-db.ps1" -Value $testScript

Write-Host "Created test script: test-mock-db.ps1"
Write-Host "You can run this script to test the timestamp handling fix with a mock database."
