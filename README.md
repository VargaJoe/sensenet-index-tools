# SenseNet Index Maintenance Suite

A comprehensive toolkit for managing and maintaining SenseNet Lucene.NET indexes. This suite currently includes tools for managing the LastActivityId value in SenseNet indexes, with plans to expand with more index maintenance capabilities.

## Repository

This project is maintained at: https://github.com/VargaJoe/sensenet-index-tools

## Requirements

- .NET 7.0 or higher
- Compatible with SenseNet Lucene.NET indexes

## Usage

```bash
# Get the current LastActivityId value
dotnet run -- get --path "<path-to-index>"

# Set a new LastActivityId value
dotnet run -- set --path "<path-to-index>" --id <new-value>

# Initialize LastActivityId in a non-SenseNet index
dotnet run -- init --path "<path-to-index>" --id <initial-value>
```

## Commands

The tool provides three main commands:

### get

Retrieves the current LastActivityId from a Lucene index.

```bash
dotnet run -- get --path "<path-to-index>"
```

### set

Sets a new LastActivityId value in an existing Lucene index. By default, this creates a backup of the index before making changes.

```bash
dotnet run -- set --path "<path-to-index>" --id <new-value> [--backup false]
```

### init

Initializes a LastActivityId in a Lucene index that doesn't have one yet. This is useful for integrating non-SenseNet indexes with SenseNet's activity tracking.

```bash
dotnet run -- init --path "<path-to-index>" --id <initial-value> [--backup false]
```

## Options

- `--path`: (Required) Path to the Lucene index directory
- `--id`: (Required for set/init) The LastActivityId value to set
- `--backup`: (Optional) Create a backup of the index before making changes (default: true)

## Building the Project

```bash
dotnet build
```

## Running the Application

```bash
dotnet run -- get --path "<path-to-index>"
```

## Creating a Release

```bash
dotnet publish -c Release
```

The output will be in the `bin/Release/net7.0/publish` directory.

## Repository

This tool is available on GitHub: [VargaJoe/sensenet-index-tools](https://github.com/VargaJoe/sensenet-index-tools)