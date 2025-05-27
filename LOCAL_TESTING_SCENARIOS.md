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
```powershell
# Only shows console output with basic statistics
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath
```
Expected console output:
```
Starting subtree index check:
  Index path: C:\path\to\index
  Repository path: /Root/Content
  Recursive: True
  Report will be saved to: None

Subtree Check Summary:
Items in Database: 150
Items in Index: 148
Matched Items: 148
Mismatched Items: 2
```

#### 2. Basic File Output
```powershell
# Basic report saved to file (default format)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --output "subtree-basic.md" `
    --report-format default
```

#### 3. Detailed Analysis
```powershell
# Detailed report with content type statistics
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --output "subtree-detailed.md" `
    --report-format detailed
```

#### 4. Hierarchical View
```powershell
# Tree format showing content structure
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --output "subtree-tree.md" `
    --report-format tree
```

#### 5. Full Analysis
```powershell
# Most comprehensive report with all available information
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree `
    --index-path $TestIndex `
    --connection-string $TestDb `
    --repository-path $RepositoryPath `
    --output "subtree-full.md" `
    --report-format full
```

### Important Notes
1. Using `--report-format` without `--output` will only affect console verbosity
2. Using `--output` without `--report-format` will save a basic report
3. For detailed reports, always use both flags together

### 3. Limited Scope Check
```powershell
# Non-recursive check (direct children only)
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree --index-path $TestIndex --connection-string $TestDb --repository-path $RepositoryPath --recursive false

# Check with depth limit
dotnet run --project src/MainProgram/sn-index-maintenance-suite.csproj check-subtree --index-path $TestIndex --connection-string $TestDb --repository-path $RepositoryPath --depth 1
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
