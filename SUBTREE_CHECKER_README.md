# SenseNet Index-Database Integrity Checker

This document provides guidance on using the `check-subtree` command from the SenseNet Index Maintenance Suite to verify synchronization between content in the SenseNet database and the Lucene search index.

## Important Update - May 2025

The subtree checker has been significantly improved with a fix for a critical issue that was causing content items to be incorrectly reported as missing from the index. The tool now uses multiple enhanced search strategies and thoroughly validates search results to ensure accurate reporting. For technical details, see [INDEX_CHECKER_FIX.md](INDEX_CHECKER_FIX.md).

## Quick Start

### Basic Command

Run a basic check to verify if content items in a specific subtree exist in the index:

```bash
dotnet run -- check-subtree --index-path "D:\path\to\lucene\index" --connection-string "Data Source=server;Initial Catalog=sensenet;Integrated Security=True" --repository-path "/Root/Content/Path"
```

### Using the PowerShell Script

For convenience, you can use the included PowerShell script:

```powershell
./CheckSubtree.ps1 -indexPath "D:\path\to\lucene\index" -connectionString "Data Source=server;Initial Catalog=sensenet;Integrated Security=True" -repositoryPath "/Root/Content/Path"
```

## Command Options

| Option | Description |
|--------|-------------|
| `--index-path` | **(Required)** Path to the Lucene index directory |
| `--connection-string` | **(Required)** SQL Connection string to the SenseNet database |
| `--repository-path` | **(Required)** Path in the content repository to check |
| `--recursive` | Check all content items under the specified path (default: true) |
| `--output` | Path to save the check report to a file |
| `--detailed` | Generate a detailed report with comprehensive information (default: false) |

## Common Scenarios

### 1. Quick Content Validation

Verify if specific content is properly indexed:

```powershell
./CheckSubtree.ps1 -indexPath "D:\path\to\index" -connectionString "..." -repositoryPath "/Root/Sites/Default_Site/MyImportantDocument" -recursive $false
```

### 2. Full Site Validation

Check an entire site structure:

```powershell
./CheckSubtree.ps1 -indexPath "D:\path\to\index" -connectionString "..." -repositoryPath "/Root/Sites/Default_Site" -outputPath "site_validation_report.md"
```

### 3. Scheduled Validation

Create a scheduled task to regularly validate your index:

```powershell
# Create a scheduled task that runs weekly
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-File D:\path\to\CheckSubtree.ps1 -indexPath '...' -connectionString '...' -repositoryPath '/Root' -outputPath 'D:\reports\weekly_validation.md'"
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At "2:00 AM"
Register-ScheduledTask -Action $action -Trigger $trigger -TaskName "SenseNet Weekly Index Validation" -Description "Validates SenseNet index against database content"
```

## Connection String Formats

### SQL Server with Windows Authentication
```
Data Source=myserver;Initial Catalog=sensenet;Integrated Security=True
```

### SQL Server with SQL Authentication
```
Data Source=myserver;Initial Catalog=sensenet;User ID=username;Password=password
```

## Analyzing Results

The generated report includes:

1. **Summary Statistics**: Overall counts and matching percentages
2. **Content Type Distribution**: Breakdown of content by type and mismatch rates for each type
3. **Mismatched Items List**: Details of all items that exist in the database but not in the index, including version state
4. **Performance Metrics**: Time taken for the check operation

The detailed report provides insights into patterns of mismatches, such as:
- Content types with high mismatch rates (indicating possible indexing configuration issues)
- Version state analysis (published vs. draft versions missing from the index)
- Path patterns where content is consistently missing

## Fixing Mismatches

When mismatches are found:

1. For individual items: Use the SenseNet admin UI to reindex specific content
2. For subtrees: Use the SenseNet `Indexing` admin page to reindex the affected subtree
3. For system-wide issues: Consider a full index rebuild

## Advanced Configuration

For larger repositories, optimize performance by:

1. Limiting scope with specific repository paths
2. Running checks during off-hours
3. Dividing large repositories into multiple checks

## Troubleshooting

### Common Issues

1. **Timeout errors**: For large subtrees, try checking smaller sections individually
2. **Permission errors**: Ensure the account running the check has appropriate database and file system permissions
3. **High mismatches**: If over 10% of content is missing, consider a full reindex

For detailed technical information, refer to the main [DOCUMENTATION.md](DOCUMENTATION.md) file.
