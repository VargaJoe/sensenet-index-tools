# SenseNet Index Maintenance Suite

A comprehensive toolkit for managing and maintaining SenseNet Lucene.NET indexes. This suite currently includes tools for managing the LastActivityId value in SenseNet indexes, with plans to expand with more index maintenance capabilities.

## Repository

This project is maintained at: https://github.com/VargaJoe/sensenet-index-tools

## Requirements

- .NET 8.0 or higher
- Compatible with SenseNet Lucene.NET indexes

## Usage

```bash
# Get the current LastActivityId value
dotnet run -- lastactivityid-get --path "<path-to-index>"

# Set a new LastActivityId value
dotnet run -- lastactivityid-set --path "<path-to-index>" --id <new-value>

# Initialize LastActivityId in a non-SenseNet index
dotnet run -- lastactivityid-init --path "<path-to-index>" --id <initial-value>

# Set a new LastActivityId value with a custom backup location
dotnet run -- lastactivityid-set --path "<path-to-index>" --id <new-value> --backup-path "<custom-backup-path>"

# Validate index structure and integrity and save report
dotnet run -- validate --path "<path-to-index>" --detailed --output "<report-file>"

# List items from index and/or database
dotnet run -- list-items --index-path "<path-to-index>" --repository-path "/Root/Path" --source "index" --recursive true --depth 1

# Check if database content exists in the index for a subtree
dotnet run -- check-subtree --index-path "<path-to-index>" --connection-string "<sql-connection-string>" --repository-path "/Root/Path/To/Check"

# Check specific path without recursion and save detailed report
dotnet run -- check-subtree --index-path "<path-to-index>" --connection-string "<sql-connection-string>" --repository-path "/Root/Path/To/Check" --recursive false --detailed --output "report.md"

# Clean up orphaned index entries (items that exist in index but not in database)
dotnet run -- clean-orphaned --index-path "<path-to-index>" --connection-string "<sql-connection-string>" --repository-path "/Root/Path/To/Check"
```

## PowerShell Helper Scripts

For convenience, PowerShell helper scripts are included in the root directory:

```powershell
# Run the subtree checker with the PowerShell script
./CheckSubtree.ps1 -indexPath "D:\path\to\index" -connectionString "Data Source=server;Initial Catalog=sensenet;Integrated Security=True" -repositoryPath "/Root/Content" -detailed $true -openReport
```

## Commands

The tool provides three main commands for managing LastActivityId:

### lastactivityid-get

Retrieves the current LastActivityId from a Lucene index.

```bash
dotnet run -- lastactivityid-get --path "<path-to-index>"
```

### lastactivityid-set

Sets a new LastActivityId value in an existing Lucene index. By default, this creates a backup of the index before making changes.

```bash
dotnet run -- lastactivityid-set --path "<path-to-index>" --id <new-value> [--backup false] [--backup-path "<custom-backup-location>"]
```

### lastactivityid-init

Initializes a LastActivityId in a Lucene index that doesn't have one yet. This is useful for integrating non-SenseNet indexes with SenseNet's activity tracking.

```bash
dotnet run -- lastactivityid-init --path "<path-to-index>" --id <initial-value> [--backup false] [--backup-path "<custom-backup-location>"]
```

### clean-orphaned

Clean up orphaned index entries that exist in the index but not in the database.

```bash 
dotnet run -- clean-orphaned --index-path "<path-to-index>" --connection-string "<sql-connection-string>" --repository-path "/Root/Path" [options]
```

Options:
- `--recursive`: Process all content items under the specified path (default: true)
- `--verbose`: Enable detailed logging of the cleanup process (default: false)
- `--dry-run`: Only show what would be deleted without making changes (default: true)
- `--backup`: Create a backup of the index before making changes (default: true)
- `--offline`: Confirm that the index is not in use and can be safely modified (required for actual cleanup)

## Options

### Common Options
- `--path`: (Required) Path to the Lucene index directory
- `--id`: (Required for set/init) The LastActivityId value to set
- `--backup`: (Optional) Create a backup of the index before making changes (default: true)
- `--backup-path`: (Optional) Custom path for storing backups. If not specified, backups will be stored in an 'IndexBackups' folder

### List Items Command Options
- `--index-path`: (Required) Path to the Lucene index directory
- `--repository-path`: (Required) Path in the content repository to list items from
- `--source`: (Required) Source to list items from: 'index', 'db', or 'both'
- `--recursive`: (Optional) Whether to list items recursively (default: true)
- `--depth`: (Optional) Limit listing to specified depth (1=direct children only, 0=all descendants)

## Building the Project

```bash
dotnet build
```

## Running the Application

```bash
dotnet run -- lastactivityid-get --path "<path-to-index>"
```

# Run the subtree checker test
```bash
dotnet run --project src/TestSubtreeChecker/TestSubtreeChecker.csproj
```

## Creating a Release

```bash
dotnet publish -c Release
```

The output will be in the `bin/Release/net8.0/publish` directory.

## Repository

This tool is available on GitHub: [VargaJoe/sensenet-index-tools](https://github.com/VargaJoe/sensenet-index-tools)

## Test Projects

The solution includes several test and diagnostic projects:

### TestDuplicatePaths
Tests handling of duplicate paths with different IDs in the Lucene index. This helps verify the ContentComparer's behavior when the index contains multiple entries for the same path.

```bash
# Run the duplicate paths test
dotnet run --project src/TestDuplicatePaths/TestDuplicatePaths.csproj
```

### TestIndexLoader
Loads and validates Lucene indexes using SenseNet's indexing engine. Useful for testing index compatibility and diagnosing loading issues.

```bash
# Test loading an index
dotnet run --project src/TestIndexLoader/TestIndexLoader.csproj -- "path/to/index"
```

### TestSubtreeChecker
Tests the enhanced search functionality in SubtreeIndexChecker with generated test data. Verifies different indexing patterns and search strategies.

```bash
# Run the subtree checker tests
dotnet run --project src/TestSubtreeChecker/TestSubtreeChecker.csproj
```

These test projects are valuable for:
- Regression testing after changes
- Debugging edge cases and indexing issues
- Verifying compatibility with different index structures
- Understanding how the tools handle various data patterns

## Project Structure