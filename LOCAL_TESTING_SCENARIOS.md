# SenseNet Index Maintenance Suite - Testing Scenarios
> ⚠️ WARNING: This file may contains real testing scenarios that should not be committed to GitHub.
> It contains connection strings and paths that should remain private.

## Test Environment Settings

```powershell
$BackupPath = "./IndexBackups"
$TestDb = ""
$TestIndex = ""
$RepositoryPath = "/Root"
```

## LastActivityId Operations

### 1. Get LastActivityId (Safe for Live Index)
```powershell
# Basic read (safe for live indexes by default)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-get --path $TestIndex
```

### 2. Set LastActivityId (Non-Live Index Only)
```powershell
# Set with default settings (creates backup)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-set --path $TestIndex --id 123456 --offline

# Set without backup
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-set --path $TestIndex --id 123456 --offline --backup false

# Set with custom backup location
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-set --path $TestIndex --id 123456 --offline --backup --backup-path $BackupPath

# Attempt without offline flag (should fail)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-set --path $TestIndex --id 123456
```

### 3. Initialize LastActivityId (Non-Live Index Only)
```powershell
# Initialize with default settings (creates backup)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-init --path $TestIndex --id 1 --offline

# Initialize without backup
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-init --path $TestIndex --id 1 --offline --backup false

# Initialize with custom backup location
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-init --path $TestIndex --id 1 --offline --backup --backup-path $BackupPath

# Attempt without offline flag (should fail)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-init --path $TestIndex --id 1
```

## Index Validation

### 1. Basic Validation
```powershell
# Quick validation (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate --path $TestIndex

# Validation with report
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate --path $TestIndex --output "validation-report.md"
```

### 2. Detailed Validation
```powershell
# Detailed check with all options
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate --path $TestIndex --detailed --sample-size 100 --output "validation-report.md"

# Full validation of all documents
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate --path $TestIndex --detailed --sample-size 0
```

### 3. With Optional Backup
```powershell
# Validate with optional backup (not needed by default since validation is read-only)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate --path $TestIndex --backup

# Custom backup location if backup is requested
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate --path $TestIndex --backup --backup-path $BackupPath
```

## Content Listing Operations

### 1. List Index Content
```powershell
# Basic index listing (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj list-index --index-path $TestIndex --repository-path $RepositoryPath

# List with recursive option
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj list-index --index-path $TestIndex --repository-path $RepositoryPath --recursive true

# List with depth limit (direct children only)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj list-index --index-path $TestIndex --repository-path $RepositoryPath --depth 1
```

### 2. List Database Content
```powershell
# Basic database listing
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj list-db --connection-string $TestDb --repository-path $RepositoryPath

# List with recursive option
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj list-db --connection-string $TestDb --repository-path $RepositoryPath --recursive true

# Detailed listing
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj list-db --connection-string $TestDb --repository-path $RepositoryPath --detailed

# List with custom ordering
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj list-db --connection-string $TestDb --repository-path $RepositoryPath --order-by "type"
```

### 3. Compare Content
```powershell
# Basic comparison (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj compare --index-path $TestIndex --connection-string $TestDb --repository-path $RepositoryPath

# Detailed comparison with report
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj compare --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --recursive true `
    --output "comparison-report.md"

# Compare specific depth with sorting
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj compare --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --depth 1 `
    --order-by "path"
```

## Subtree Operations

### Understanding Output Types
The `check-subtree` command provides two types of output:
1. Console output (always shown)
2. File output (optional, requires both `--output` and `--report-format` flags)

### Example Scenarios

#### 1. Console-Only Output
Test the basic functionality with console output:
```powershell
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath
```
Expected console output:
```
Subtree Check Summary:
Items in Database: 25
Items in Index: 23
Matched Items: 23
Mismatched Items: 2

Mismatch Summary by Type:
Document: 2 mismatches
```

#### 2. Basic File Output
Generate a basic report file:
```powershell
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --output "subtree-default.md" `
    --report-format default
```
Verify the report contains:
- Basic statistics
- Operation timing information
- Simple mismatch counts

#### 3. Detailed Analysis
Test detailed reporting with content type breakdown:
```powershell
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --output "subtree-detailed.md" `
    --report-format detailed
```
Check for:
- Content type statistics table
- Detailed mismatch information
- Status indicators showing specific mismatch types:
  - [✓] for matches
  - [✗ DB Only] for database-only items
  - [✗ Index Only] for index-only items
  - [✗ ID Mismatch] for ID mismatches
  - [✗ Version Mismatch] for version mismatches

#### 4. Hierarchical View
Test tree format visualization:
```powershell
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --output "subtree-tree.md" `
    --report-format tree
```
Verify:
- Proper tree structure with indentation
- Status indicators with specific error types
- Folder statistics showing match rates
- Branch totals and success rates

#### 5. Full Analysis with HTML Output
Test comprehensive reporting:
```powershell
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --output "subtree-full.html" `
    --report-format full `
    --format html
```
Check for:
- Complete item list with status details
- Color-coded status indicators in HTML
- Tree visualization with expandable folders
- Detailed statistics for each content type

### HTML Output Testing

When testing HTML output, verify:
1. **Color Coding**
   - Green for matches [✓]
   - Red for mismatches [✗]
   - Proper styling for different status types

2. **Tree View**
   - Proper indentation with line guides
   - Folder indicators with forward slashes
   - Match rate percentages for folders
   - Total item counts in branches

3. **Tables**
   - Proper alignment of columns
   - Clear status indicators
   - Readable formatting of IDs and paths
   - Sortable columns (if implemented)

### Important Notes

1. **Status Types**
   - All mismatches should show specific error type
   - Status format should be consistent across all views
   - Both icon and text should be visible ([✗ DB Only], etc.)

2. **Testing Different Depths**
   ```powershell
   # Test direct children only
   dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
       --index-path $TestIndex `
       --connection-string $TestDb `
       --repository-path $RepositoryPath `
       --depth 1 `
       --report-format tree

   # Test full recursion
   dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
       --index-path $TestIndex `
       --connection-string $TestDb `
       --repository-path $RepositoryPath `
       --recursive `
       --report-format tree
   ```

3. **Edge Cases**
   - Empty folders
   - Deep hierarchies
   - Special characters in names
   - Very large subtrees
   - Mixed content types
```

## Recovery Scenarios

### 1. Before Making Changes
```powershell
# Get current LastActivityId (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-get --path $TestIndex

# Create manual backup of index folder
$BackupTimestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$BackupName = "backup_${BackupTimestamp}"
Copy-Item -Path $TestIndex -Destination (Join-Path $BackupPath $BackupName) -Recurse

# Validate backup integrity (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate --path (Join-Path $BackupPath $BackupName) --detailed

# ⚠️ IMPORTANT: Stop all applications using the index before proceeding with modifications
```

### 2. Index Recovery Process
```powershell
# Initialize LastActivityId (requires --offline flag)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-init --path $TestIndex --id 1 --offline

# Verify LastActivityId after initialization (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-get --path $TestIndex

# Full validation (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate --path $TestIndex --detailed

# Compare with database (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj compare --path $TestIndex --connection-string $TestDb

# If needed, set to last known good activity ID (requires --offline flag)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-set --path $TestIndex --id $LastGoodActivityId --offline
```

### 3. Emergency Recovery
```powershell
# For severely corrupted indexes:

# 1. Get current state if possible (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-get --path $TestIndex

# 2. Create emergency backup
$EmergencyBackupPath = "${BackupPath}_emergency_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
Copy-Item -Path $TestIndex -Destination $EmergencyBackupPath -Recurse

# 3. ⚠️ IMPORTANT: Stop all applications using the index

# 4. Initialize fresh index (requires --offline flag)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj lastactivityid-init --path $TestIndex --id 1 --offline

# 5. Verify initialization (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate --path $TestIndex --detailed

# 6. Resume applications and let them rebuild the index
```

## Performance Testing

### 1. Large Index Operations
```powershell
# Validate with increased sample size
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate --path $TestIndex --detailed --sample-size 1000

# Deep subtree check (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --repository-path "/Root" `
    --connection-string $TestDb `
    --report-format detailed
```

### 2. Live Index Performance
```powershell
# High sample count validation (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj validate --path $TestIndex --detailed --sample-size 500

# Deep comparison with database (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj compare `
    --index-path $TestIndex `
    --repository-path $RepositoryPath `
    --connection-string $TestDb `
    --recursive true
```

## Cleanup Operations

### 1. Preview Orphaned Entries (Safe for Live Indexes)
```powershell
# Basic check to preview orphaned entries (safe for live indexes)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj clean-orphaned `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath

# Check specific path with detailed output
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj clean-orphaned `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --verbose

# Check direct children only
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj clean-orphaned `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --recursive false
```

### 2. Cleanup Operations (Non-Live Index Only)
```powershell
# Clean up with default safety measures (requires --offline flag)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj clean-orphaned `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --dry-run false `
    --offline

# Clean up without creating backup (not recommended, requires --offline flag)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj clean-orphaned `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --dry-run false `
    --backup false `
    --offline

# Clean up with custom backup location (requires --offline flag)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj clean-orphaned `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --dry-run false `
    --backup-path $BackupPath `
    --offline

# Attempt without offline flag (should fail)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj clean-orphaned `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --dry-run false
```

### 3. Test Recovery From Backup
```powershell
# Create a backup before cleanup
$CleanupBackupPath = "${BackupPath}_pre_cleanup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
Copy-Item -Path $TestIndex -Destination $CleanupBackupPath -Recurse

# Perform cleanup (requires --offline flag)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj clean-orphaned `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --dry-run false `
    --offline

# Verify results
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj compare `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath

# If needed, restore from backup
Remove-Item -Path $TestIndex -Recurse
Copy-Item -Path $CleanupBackupPath -Destination $TestIndex -Recurse
```
