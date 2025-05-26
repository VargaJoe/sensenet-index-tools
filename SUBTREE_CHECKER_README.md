# SenseNet Index-Database Integrity Checker

This document provides guidance on using the `check-subtree` command from the SenseNet Index Maintenance Suite to verify synchronization between content in the SenseNet database and the Lucene search index.

## Quick Start

### Basic Command

Run a basic check to verify if content items in a specific subtree exist in the index:

```bash
dotnet run -- check-subtree --index-path "D:\path\to\lucene\index" --connection-string "Data Source=server;Initial Catalog=sensenet;Integrated Security=True" --repository-path "/Root/Content/Path" --report-format default
```

### Using the PowerShell Script

For convenience, you can use the included PowerShell script:

```powershell
./CheckSubtree.ps1 -indexPath "D:\path\to\lucene\index" -connectionString "Data Source=server;Initial Catalog=sensenet;Integrated Security=True" -repositoryPath "/Root/Content/Path" -reportFormat "default"
```

## Command Options

| Option | Description |
|--------|-------------|
| `--index-path` | **(Required)** Path to the Lucene index directory |
| `--connection-string` | **(Required)** SQL Connection string to the SenseNet database |
| `--repository-path` | **(Required)** Path in the content repository to check |
| `--recursive` | Check all content items under the specified path (default: true) |
| `--depth` | Limit checking to specified depth (1=direct children only, 0=all descendants) |
| `--output` | Path to save the check report to a file |
| `--report-format` | Format of the report: 'default', 'detailed', 'tree', or 'full' (default: 'default') |

## Report Formats

The tool supports different report formats through the `--report-format` option:

### 1. Default Format (--report-format default)
- Basic summary statistics
- Overall counts and matching percentages
- Quick overview of mismatches by content type

### 2. Detailed Format (--report-format detailed)
- Everything in default format
- Content type distribution with match rates
- Detailed breakdown of mismatches by content type
- Performance metrics and timing information

### 3. Tree Format (--report-format tree)
- Everything in detailed format
- Hierarchical view of content structure
- Clear visualization of where mismatches occur in the content tree

### 4. Full Format (--report-format full)
- Everything in detailed format
- Complete item-by-item comparison
- All matches and mismatches listed
- Comprehensive content type analysis
- All available metadata and statistics

The report provides insights such as:
- Content types with high mismatch rates (indicating possible indexing configuration issues)
- Version state analysis (published vs. draft versions missing from the index)
- Path patterns where content is consistently missing
- Match rates for each content type

## Common Scenarios

### 1. Quick Content Validation

Verify if specific content is properly indexed:

```powershell
./CheckSubtree.ps1 -indexPath "D:\path\to\index" -connectionString "..." -repositoryPath "/Root/Sites/Default_Site/MyImportantDocument" -recursive $false -reportFormat "default"
```

### 2. Full Site Validation with Details

Check an entire site structure with detailed reporting:

```powershell
./CheckSubtree.ps1 -indexPath "D:\path\to\index" -connectionString "..." -repositoryPath "/Root/Sites/Default_Site" -reportFormat "full" -outputPath "site_validation_report.md"
```

### 3. Limited Depth Check

Check only immediate children of a path:

```powershell
./CheckSubtree.ps1 -indexPath "D:\path\to\index" -connectionString "..." -repositoryPath "/Root/Content" -depth 1 -reportFormat "detailed"
```

### 4. Scheduled Validation

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
Data Source=myserver;Initial Catalog=sensenet;User ID=username;Password=password;TrustServerCertificate=True
```

## Analyzing Results

The report includes different levels of detail based on the chosen format:

1. **Summary Statistics**
   - Total items in database and index
   - Number of matches and mismatches
   - Match percentage by content type

2. **Content Type Analysis** (detailed and full formats)
   - Distribution of content by type
   - Mismatch rates per content type
   - Type-specific patterns

3. **Detailed Mismatches** (detailed and full formats)
   - Full path information
   - Node IDs and Version IDs
   - Content types
   - Mismatch reasons

4. **Complete Item List** (full format only)
   - All items from both database and index
   - Side-by-side comparison
   - Full metadata for each item

## Troubleshooting

### Common Issues

1. **Timeout errors**: For large subtrees, try:
   - Checking smaller sections individually
   - Using the `--depth` parameter to limit scope
   - Running during off-peak hours

2. **Permission errors**: Ensure the account has:
   - Read access to the index directory
   - Query permissions on the database
   - Sufficient SQL timeout settings

3. **High mismatches**: If over 10% of content is missing:
   - Check index rebuild dates
   - Verify content publication status
   - Consider a full reindex

For detailed technical information about the index structure and validation process, refer to the main [DOCUMENTATION.md](DOCUMENTATION.md) file.
