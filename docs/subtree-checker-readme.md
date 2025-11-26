# SenseNet Index-Database Integrity Checker

This document provides guidance on using the `check-subtree` command from the SenseNet Index Maintenance Suite to verify synchronization between content in the SenseNet database and the Lucene search index.

When using the `check-subtree` command, you'll get output in two forms:
1. Console output - Always displayed, showing basic statistics and summary information
2. File output - Optional detailed report, controlled by `--output` and `--report-format` flags

## Quick Start

### Basic Command

Run a basic check to verify if content items in a specific subtree exist in the index:

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
| `--format` | Format of the output file: 'md' (Markdown) or 'html' (HTML) (default: 'md') |

## Output and Report Formats

The tool provides two types of output:

### Console Output (Always Shown)
Console output is always displayed and includes:
```
Subtree Check Summary:
Items in Database: 150
Items in Index: 148
Matched Items: 148
Mismatched Items: 2

Mismatch Summary by Type:
Document: 1 mismatches
Folder: 1 mismatches
```

### File Output (Optional)
To get detailed reports saved to a file, you must use **both**:
1. `--output` flag to specify the output file path
2. `--report-format` flag to specify the level of detail

Available report formats:

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
Everything in default format plus content type analysis and detailed status information:
```markdown
## Content Type Statistics
| Type | Total Items | Mismatches | Match Rate |
|------|-------------|------------|------------|
| Document | 75 | 1 | 98.7% |
| Folder | 45 | 1 | 97.8% |
| Image | 30 | 0 | 100.0% |

## Mismatches by Content Type
### Documents
| Status | DB NodeId | DB VerID | Index NodeId | Index VerID | Path |
|--------|-----------|----------|--------------|-------------|------|
| [✗ DB Only] | 12345 | 1 | - | - | /Root/Content/MyDoc.docx |
```

#### 3. Tree Format (--report-format tree)
Hierarchical view with enhanced status visualization:
```markdown
## Content Tree
/Root/Content/ [✓] (95% match, 150 items)
├── Folder1/ [✓] (100% match, 10 items)
│   ├── Document1.docx [✓]
│   └── Image1.jpg [✓]
└── Folder2/ [✗ Index Only] (50% match, 2 items)
    ├── Document2.docx [✗ DB Only]
    └── Document3.docx [✗ ID Mismatch]
```

Status indicators show:
- `[✓]` - Item matches in both database and index
- `[✗ DB Only]` - Item exists only in database
- `[✗ Index Only]` - Item exists only in index
- `[✗ ID Mismatch]` - Item exists in both but with different IDs
- `[✗ Version Mismatch]` - Item exists in both but with different versions

Folder statistics show:
- Match percentage for the folder and its descendants
- Total number of items in the subtree
- Visual tree structure with proper indentation

#### 4. Full Format (--report-format full)
Everything from detailed format plus complete item list:
```markdown
## Complete Item List
| Status | DB NodeId | DB VerID | Index NodeId | Index VerID | Path | Type |
|--------|-----------|----------|--------------|-------------|------|------|
| [✓] | 12345 | 1 | 12345 | 1 | /Root/Content/Doc1.docx | Document |
| [✗ DB Only] | 12346 | 1 | - | - | /Root/Content/Doc2.docx | Document |
| [✗ Version Mismatch] | 12347 | 2 | 12347 | 1 | /Root/Content/Doc3.docx | Document |
```

### HTML Output (--format html)
When using HTML format, the report includes:
- Color-coded status indicators (green for matches, red for mismatches)
- Interactive tree view with expandable folders
- Styled tables with sorting capability
- Branch statistics in a readable format
- Proper indentation and visual hierarchy

## Common Scenarios

### 1. Quick Content Validation
```powershell
./CheckSubtree.ps1 -indexPath "D:\path\to\index" -connectionString "..." -repositoryPath "/Root/Sites/Default_Site/MyDocument" -recursive $false -reportFormat "default"
```

### 2. Full Site Validation with Details
```powershell
./CheckSubtree.ps1 -indexPath "D:\path\to\index" -connectionString "..." -repositoryPath "/Root/Sites/Default_Site" -reportFormat "full" -outputPath "site_validation.html" -format "html"
```

### 3. Tree View for Visual Analysis
```powershell
./CheckSubtree.ps1 -indexPath "D:\path\to\index" -connectionString "..." -repositoryPath "/Root/Content" -reportFormat "tree" -outputPath "content_tree.md"
```

## Testing Different Depths

You can control the depth of checking with two parameters:
1. `--recursive` - Check all content items under the specified path (default: true)
2. `--depth` - Limit checking to specified depth (1=direct children only, 0=all descendants)

### Example Depth Control Scenarios

1. Check only direct children:
```powershell
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --depth 1 `
    --report-format tree `
    --output "direct-children.md"
```

2. Check all descendants (default):
```powershell
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --recursive `
    --report-format tree `
    --output "all-descendants.md"
```

3. Check limited depth with detailed report:
```powershell
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --depth 2 `
    --report-format detailed `
    --output "depth-2-detailed.md"
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

3. **Detailed Mismatches**
   - Full path information
   - Node IDs and Version IDs
   - Content types
   - Specific mismatch type (DB Only, Index Only, ID Mismatch, Version Mismatch)

4. **Tree View Analysis**
   - Hierarchical content structure
   - Visual status indicators
   - Branch statistics (match rates, total items)
   - Easy identification of problem areas

## Troubleshooting

### Common Issues

1. **High mismatches**: If over 10% of content is missing:
   - Check status types ([✗ DB Only], [✗ Index Only], etc.) to identify patterns
   - Look for common parent folders with low match rates
   - Consider focusing on specific content types showing high mismatch rates

2. **Understanding Status Types**:
   - `[✗ DB Only]`: Content exists in database but not in index (may need reindexing)
   - `[✗ Index Only]`: Content exists in index but not in database (may be orphaned)
   - `[✗ ID Mismatch]`: Same content with different IDs (possible content restore issue)
   - `[✗ Version Mismatch]`: Different versions in DB and index (possible sync issue)

## Scheduling Index Checks

While the tool doesn't include built-in scheduling, you can set up periodic checks using your preferred task scheduler:

### Windows Task Scheduler

Create a scheduled task to run index validation:

```batch
REM validate.bat
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate ^
    --path "%INDEX_PATH%" ^
    --detailed ^
    --output "validation_%date:~-4,4%%date:~-10,2%%date:~-7,2%.md"
```

### Linux Cron

Add a cron entry to run periodic checks:

```bash
# /etc/cron.d/index-validation
0 2 * * * youruser dotnet run --project /path/to/sn-index-maintenance-suite.csproj validate --path "$INDEX_PATH" --detailed --output "validation_$(date +\%Y\%m\%d).md"
```

### CI/CD Pipeline Integration

For automated environments, include validation in your CI/CD pipeline:

```yaml
steps:
  - name: Validate Index
    run: |
      dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate \
        --path "${{ env.INDEX_PATH }}" \
        --detailed \
        --output validation-report.md
```

The validation command is designed to be automation-friendly:
- Exits with non-zero code on validation failures
- Generates structured reports
- Safe to run on live indexes (read-only)
- Configurable validation depth and sampling
