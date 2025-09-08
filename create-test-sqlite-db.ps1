#!/usr/bin/env pwsh

# This script creates a simple SQLite test database for subtree checking
# It creates a minimal SenseNet-like structure that can be used for testing

$dbPath = ".\test-sensenet.db"

# Remove existing database if it exists
if (Test-Path $dbPath) {
    Remove-Item $dbPath -Force
    Write-Host "Removed existing database"
}

# Create a new SQLite database with test data
# We'll use PowerShell with SQLite to create a simple test database

try {
    # Create the database file
    $null = New-Item -Path $dbPath -ItemType File -Force
    
    # Add required assembly (this might need adjustment based on your system)
    Add-Type -Path "$env:USERPROFILE\.nuget\packages\system.data.sqlite.core\1.0.118\lib\netstandard2.0\System.Data.SQLite.dll" -ErrorAction SilentlyContinue
    
    if (-not ([System.Management.Automation.PSTypeName]'System.Data.SQLite.SQLiteConnection').Type) {
        Write-Host "SQLite not available via .NET assembly. Using alternative method..."
        
        # Alternative: Create a SQL script file and use sqlite3.exe if available
        $sqlScript = @"
CREATE TABLE Nodes (
    NodeId INTEGER PRIMARY KEY,
    Path TEXT NOT NULL,
    NodeTypeId INTEGER,
    Name TEXT,
    DisplayName TEXT,
    CreationDate TEXT,
    ModificationDate TEXT
);

-- Insert some test data that matches typical SenseNet structure
INSERT INTO Nodes (NodeId, Path, NodeTypeId, Name, DisplayName, CreationDate, ModificationDate) VALUES
(1, '/Root', 1, 'Root', 'Root', '2023-01-01 00:00:00', '2023-01-01 00:00:00'),
(2, '/Root/Content', 2, 'Content', 'Content', '2023-01-01 00:00:00', '2023-01-01 00:00:00'),
(3, '/Root/Content/Documents', 3, 'Documents', 'Documents', '2023-01-01 00:00:00', '2023-01-01 00:00:00'),
(4, '/Root/Content/Documents/Doc1.docx', 4, 'Doc1.docx', 'Document 1', '2023-01-01 00:00:00', '2023-01-01 00:00:00'),
(5, '/Root/Content/Documents/Doc2.pdf', 4, 'Doc2.pdf', 'Document 2', '2023-01-01 00:00:00', '2023-01-01 00:00:00'),
(6, '/Root/Content/Images', 3, 'Images', 'Images', '2023-01-01 00:00:00', '2023-01-01 00:00:00'),
(7, '/Root/Content/Images/Photo1.jpg', 5, 'Photo1.jpg', 'Photo 1', '2023-01-01 00:00:00', '2023-01-01 00:00:00'),
(8, '/Root/Sites', 2, 'Sites', 'Sites', '2023-01-01 00:00:00', '2023-01-01 00:00:00'),
(9, '/Root/Sites/Default_Site', 6, 'Default_Site', 'Default Site', '2023-01-01 00:00:00', '2023-01-01 00:00:00'),
(10, '/Root/Sites/Default_Site/workspaces', 3, 'workspaces', 'Workspaces', '2023-01-01 00:00:00', '2023-01-01 00:00:00');

-- Create NodeTypes table for reference
CREATE TABLE NodeTypes (
    NodeTypeId INTEGER PRIMARY KEY,
    Name TEXT,
    DisplayName TEXT
);

INSERT INTO NodeTypes (NodeTypeId, Name, DisplayName) VALUES
(1, 'PortalRoot', 'Portal Root'),
(2, 'SystemFolder', 'System Folder'),
(3, 'Folder', 'Folder'),
(4, 'File', 'File'),
(5, 'Image', 'Image'),
(6, 'Site', 'Site');
"@
        
        # Save the SQL script
        $sqlScript | Out-File -FilePath ".\create-test-db.sql" -Encoding UTF8
        
        Write-Host "Created SQL script at: create-test-db.sql"
        Write-Host "Test database will be at: $dbPath"
        Write-Host ""
        Write-Host "Connection string to use in webapp:"
        Write-Host "Data Source=$((Resolve-Path $dbPath).Path);Version=3;"
        Write-Host ""
        Write-Host "Note: You'll need sqlite3.exe to execute the SQL script:"
        Write-Host "sqlite3.exe $dbPath < create-test-db.sql"
    }
}
catch {
    Write-Host "Error creating database: $($_.Exception.Message)"
    Write-Host "You can manually create the database using the SQL script method above."
}

Write-Host "Done!"
