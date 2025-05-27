# SenseNet Index-Database Integrity Checker

This document provides guidance on using the `check-subtree` command from the SenseNet Index Maintenance Suite to verify synchronization between content in the SenseNet database and the Lucene search index.

When using the `check-subtree` command, you'll get output in two forms:
1. Console output - Always displayed, showing basic statistics and summary information
2. File output - Optional detailed report, controlled by `--output` and `--report-format` flags

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

## Output and Report Formats

The tool provides two types of output:

### Console Output (Always Shown)
Console output is always displayed, regardless of other options, and includes:
- Basic operation details (paths, options used)
- Summary statistics (totals, matches, mismatches)
- Quick overview of results:
  ```
  Subtree Check Summary:
  Items in Database: 150
  Items in Index: 148
  Matched Items: 148
  Mismatched Items: 2

  Mismatch Summary by Type: (shown if mismatches exist)
  Document: 1 mismatches
  Folder: 1 mismatches
  ```

### File Output (Optional)
To get detailed reports saved to a file, you must use **both**:
1. `--output` flag to specify the output file path
2. `--report-format` flag to specify the level of detail

The content of the file will vary based on the chosen report format:

#### 1. Default Format (--report-format default)
Basic report with summary information:
```markdown
# Subtree Index Check Report

## Check Information
- Repository Path: /Root/Content
- Recursive: true
- Start Time: 2023-11-15 10:30:15
- End Time: 2023-11-15 10:30:18
- Duration: 3.25 seconds

## Summary
- Items in Database: 150
- Items in Index: 148
- Matched Items: 148
- Mismatched Items: 2
```

#### 2. Detailed Format (--report-format detailed)
Everything in default format plus content type analysis:
```markdown
## Content Type Statistics
| Type | Total Items | Mismatches | Match Rate |
|------|-------------|------------|------------|
| Document | 75 | 1 | 98.7% |
| Folder | 45 | 1 | 97.8% |
| Image | 30 | 0 | 100.0% |

## Mismatches by Content Type
### Documents
| NodeId | Path | Content Type |
|--------|------|-------------|
| 12345 | /Root/Content/MyDoc.docx | Document |

### Folders
| NodeId | Path | Content Type |
|--------|------|-------------|
| 12346 | /Root/Content/MyFolder | Folder |
```

#### 3. Tree Format (--report-format tree)
Adds hierarchical content structure to detailed format:
```markdown
## Content Tree
/Root
└── Content/
    ├── Folder1/ ✓
    │   ├── Document1.docx ✓
    │   └── Image1.jpg ✓
    └── Folder2/ ✗ (missing from index)
        └── Document2.docx ✗ (missing from index)
```

#### 4. Full Format (--report-format full)
Most comprehensive report, includes everything in detailed format plus:
```markdown
## Complete Item List
| Status | DB NodeId | DB VerID | Index NodeId | Index VerID | Path | Type |
|--------|-----------|----------|--------------|-------------|------|------|
| ✓ | 12345 | 1 | 12345 | 1 | /Root/Content/Doc1.docx | Document |
| ✗ | 12346 | 1 | - | - | /Root/Content/Doc2.docx | Document |
| ✓ | 12347 | 2 | 12347 | 2 | /Root/Content/Image1.jpg | Image |
```

### Important Notes About Report Outputs
1. Console output is always shown and cannot be disabled
2. File output requires **both** flags:
   - `--output` to specify where to save the report
   - `--report-format` to specify the level of detail
3. Reports provide insights into:
   - Content types with high mismatch rates
   - Version state analysis (published vs. draft)
   - Path patterns where content is missing
   - Match rates by content type

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
