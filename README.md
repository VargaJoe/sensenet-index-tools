# SenseNet Index Maintenance Suite

A comprehensive toolkit for managing and maintaining SenseNet Lucene.NET indexes. This suite currently includes tools for managing the LastActivityId value in SenseNet indexes and other index maintenance capabilities.

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

# Working with live production indexes
dotnet run -- lastactivityid-get --path "<path-to-index>" --live-index true
dotnet run -- list-index --index-path "<path-to-index>" --repository-path "/Root" --live-index true
dotnet run -- validate --path "<path-to-index>" --live-index true

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

The suite provides several commands for managing SenseNet indexes:

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

## Running the Tests

```bash
# Run the subtree checker test
dotnet run --project src/TestSubtreeChecker/TestSubtreeChecker.csproj
```

## Creating a Release

```bash
dotnet publish -c Release
```

The output will be in the `bin/Release/net8.0/publish` directory.

## Repository

This tool is available on GitHub: [VargaJoe/sensenet-index-tools](https://github.com/VargaJoe/sensenet-index-tools)

## New Features

### Live Index Flag
Added the `--live-index` flag for all operations to ensure safety when working with production indexes. When this flag is set, write operations will be blocked to prevent accidental modifications to live indexes.

### Optional Backups for Read-Only Operations
Backup creation is now disabled by default for read-only operations and enabled by default for write operations. You can still request backups for any operation with `--backup true`.

### Enhanced Paging
Index operations now properly support large indexes by implementing efficient paging, removing the previous 10,000 document limit.

### Clean Orphaned Entries
New `clean-orphaned` command for cleaning up index entries that exist in the index but not in the database.

### Enhanced Content Comparison
Significantly improved content comparison logic with better handling of multiple versions, renamed items, and path normalization. See [Enhanced Comparer Documentation](ENHANCED_COMPARER.md) for details.