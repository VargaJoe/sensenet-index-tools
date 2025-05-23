# SenseNet Index Maintenance Suite

A comprehensive toolkit for managing and maintaining SenseNet Lucene.NET indexes. This suite includes tools for managing the LastActivityId value in SenseNet indexes, validating index integrity, and checking content synchronization between the database and index.

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

# Validate index structure and integrity
dotnet run -- validate --path "<path-to-index>" --detailed

# Check if database content exists in the index for a subtree
dotnet run -- check-subtree --index-path "<path-to-index>" --connection-string "<sql-connection-string>" --repository-path "/Root/Path/To/Check"

# Check specific path without recursion and save detailed report
dotnet run -- check-subtree --index-path "<path-to-index>" --connection-string "<sql-connection-string>" --repository-path "/Root/Path/To/Check" --recursive false --detailed --output "report.md"
```
dotnet run -- lastactivityid-set --path "<path-to-index>" --id <new-value> --backup-path "D:\Backups\LuceneIndices"
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

## Options

- `--path`: (Required) Path to the Lucene index directory
- `--id`: (Required for set/init) The LastActivityId value to set
- `--backup`: (Optional) Create a backup of the index before making changes (default: true)
- `--backup-path`: (Optional) Custom path for storing backups. If not specified, backups will be stored in an 'IndexBackups' folder at the same level as the index parent folder

## Building the Project

```bash
dotnet build
```

## Running the Application

```bash
dotnet run -- lastactivityid-get --path "<path-to-index>"
```

## Creating a Release

```bash
dotnet publish -c Release
```

The output will be in the `bin/Release/net8.0/publish` directory.

## Repository

This tool is available on GitHub: [VargaJoe/sensenet-index-tools](https://github.com/VargaJoe/sensenet-index-tools)