# SenseNet Index Maintenance Suite - Technical Documentation

## Overview

The SenseNet Index Maintenance Suite is a comprehensive toolkit for SenseNet developers and administrators to manage and maintain Lucene.NET indexes used by SenseNet. The toolkit provides functionality for LastActivityId management and index structure validation, which are critical for content synchronization and maintaining index health in SenseNet content repository systems.

## Repository

This project is maintained at: https://github.com/VargaJoe/sensenet-index-tools

## Technical Details

### LastActivityId in SenseNet

In SenseNet, the `LastActivityId` is a tracking number stored in the Lucene index's commit user data. It represents the most recent content repository activity that has been indexed, and is used to:

1. Track which content operations have been indexed
2. Determine which operations need to be processed during index rebuilding
3. Maintain consistency between the content repository and the search index

### Index Structure Validation

The toolkit includes comprehensive index validation capabilities that check:

1. **Basic Structure**
   - Directory existence and accessibility
   - Presence of required Lucene index files
   - Segments file validation
   - Lock status verification

2. **Document Integrity**
   - Validation of document structure
   - Required field presence checks
   - Verification of SenseNet-specific fields
   - Document sampling for integrity analysis

3. **Segment Health**
   - Corrupt segment detection
   - Multi-segment consistency checks
   - Segment reader validation

4. **File System**
   - Orphaned file detection
   - File pattern validation
   - Backup verification

### Implementation

The tool supports two main access methods:

#### Direct Lucene.NET Access
- Uses `Lucene.Net.Store.FSDirectory` to open the index 
- Uses `Lucene.Net.Index.DirectoryReader` to access index metadata
- Retrieves and modifies commit user data containing the LastActivityId
- Utilizes `IndexWriter` for committing changes to the index

#### SenseNet API Access
- Utilizes `IndexDirectory` and `Lucene29LocalIndexingEngine`
- Provides native SenseNet compatibility
- Supports advanced SenseNet-specific operations

### Internal Architecture

The application is structured as follows:

1. **Command-Line Interface Layer**
   - Processes command-line arguments
   - Validates input parameters
   - Dispatches to appropriate service methods

2. **Service Layer**
   - `IndexActivityService`: Core service for LastActivityId operations
   - `IndexValidator`: Comprehensive index validation service
   - Implementation of backup functionality

3. **Validation Components**
   - Structure validation
   - Document integrity checks
   - Segment analysis
   - File system verification

## Usage Guide

### Index Validation Command

The validate command performs comprehensive checks on your Lucene index. Since it's a read-only operation, it's completely safe to use on live indexes:

```powershell
# Basic validation
sn-index-maintenance-suite validate --path <index-path>

# Detailed validation with report
sn-index-maintenance-suite validate --path <index-path> --detailed --output report.md

# Custom sampling size
sn-index-maintenance-suite validate --path <index-path> --sample-size 100

# Detailed validation with optional backup
sn-index-maintenance-suite validate --path <index-path> --detailed --backup --backup-path "D:\Backups"

# Full validation of all documents
sn-index-maintenance-suite validate --path <index-path> --detailed --sample-size 0
```

### LastActivityId Commands

The suite provides three commands for managing LastActivityId:

1. **Get LastActivityId** (Safe for live indexes)
```powershell
# Basic read operation
sn-index-maintenance-suite lastactivityid-get --path "<path-to-index>"
```

2. **Set LastActivityId** (Requires --offline flag)
```powershell
# Set with default settings (creates backup)
sn-index-maintenance-suite lastactivityid-set --path "<path-to-index>" --id <value> --offline

# Set without backup
sn-index-maintenance-suite lastactivityid-set --path "<path-to-index>" --id <value> --offline --backup false

# Set with custom backup location
sn-index-maintenance-suite lastactivityid-set --path "<path-to-index>" --id <value> --offline --backup --backup-path "<custom-backup-location>"
```

3. **Initialize LastActivityId** (Requires --offline flag)
```powershell
# Initialize with default settings (creates backup)
sn-index-maintenance-suite lastactivityid-init --path "<path-to-index>" --id <value> --offline

# Initialize without backup
sn-index-maintenance-suite lastactivityid-init --path "<path-to-index>" --id <value> --offline --backup false

# Initialize with custom backup location
sn-index-maintenance-suite lastactivityid-init --path "<path-to-index>" --id <value> --offline --backup --backup-path "<custom-backup-location>"
```

### Subtree Checking Commands

Check specific paths in your index against the database:

```powershell
# Basic subtree check
sn-index-maintenance-suite subtree-check --path <index-path> --subtree "/Root/Sites/Default_Site"

# With database comparison
sn-index-maintenance-suite subtree-check --path <index-path> `
    --subtree "/Root/Sites/Default_Site" `
    --connection-string <connection-string>

# Detailed check with report
sn-index-maintenance-suite subtree-check --path <index-path> `
    --subtree "/Root/Sites/Default_Site" `
    --connection-string <connection-string> `
    --detailed `
    --output "report.md"
```

### Database Operations

Commands for database operations and comparisons:

```powershell
# List database content
sn-index-maintenance-suite database-list --connection-string <connection-string>
sn-index-maintenance-suite database-list --connection-string <connection-string> --path "/Root/Sites"

# Compare database with index
sn-index-maintenance-suite compare --path <index-path> --connection-string <connection-string>
sn-index-maintenance-suite compare --path <index-path> --connection-string <connection-string> --detailed
```

## Safety Features

### Live Index Protection

The toolkit assumes all indexes are potentially live (connected to a running SenseNet application) by default. This ensures safety when working with production indexes:

1. **Read-Only Operations** (Always Safe)
   - `validate`: Comprehensive index validation including detailed checks
   - `lastactivityid-get`: Reading LastActivityId
   - `list-index`: Content listing from index (read-only)
   - `list-db`: Content listing from database (read-only)
   - `compare`: Database-Index comparison (read-only)

2. **Write Operations** (Require --offline Flag)
   - `lastactivityid-set`: Modifying LastActivityId
   - `lastactivityid-init`: Initializing new LastActivityId
   
### Content Listing Safety

The content listing commands (`list-index`, `list-db`, and `compare`) are designed to be completely safe for use with live production systems:

1. **Index Listing (`list-index`)**
   - Uses read-only IndexReader.Open(directory, true)
   - No write operations or index modifications
   - Safe for concurrent index access
   - Supports recursive listing and depth filtering
   - Memory-efficient with large result sets

2. **Database Listing (`list-db`)**
   - Uses read-only SELECT queries only
   - No database modifications
   - Uses prepared statements for security
   - Efficient query optimization
   - Support for large result sets

3. **Compare Operation (`compare`)**
   - Combines safe read operations from both sources
   - No modifications to index or database
   - Memory-efficient result processing
   - Supports detailed difference reporting
   - Safe for production analysis
 
Example usage with live systems:
```powershell
# List items from a live index (always safe)
sn-index-maintenance-suite list-index --index-path <index-path> --repository-path "/Root"

# Compare live index with database (always safe)
sn-index-maintenance-suite compare --path <index-path> --connection-string <conn-string>
```

## Safety Features

1. **Default Live Index Protection**
   - All indexes are treated as potentially live by default
   - Write operations are blocked without explicit --offline flag
   - Read operations are optimized for safety with live indexes

2. **Backup Management**
   - Automatic backup creation for write operations
   - Only available when using --offline flag
   - Configurable backup paths
   - Timestamp-based backup naming

### Validation Options

- `--path`: Required. Path to the Lucene index directory
- `--detailed`: Optional. Performs additional in-depth checks including segments and orphaned files. Default: false
- `--output`: Optional. Path to save the validation report in Markdown format
- `--backup`: Optional. Creates a backup before validation. Not needed by default since validation is read-only. Default: false
- `--backup-path`: Optional. Custom path for storing the backup (only used if --backup is true)
- `--sample-size`: Optional. Number of documents to sample for validation (0 for full validation). Default: 10
- `--required-fields`: Optional. JSON array of required fields. Overrides default SenseNet fields.

#### Validation Coverage

The validation process includes:

1. **Basic Structure Validation**
   - Directory existence and accessibility
   - Presence of required Lucene index files
   - Segments file validation
   - Lock status verification

2. **Document Integrity**
   - Required field presence (Id, VersionId, etc.)
   - Field value validation
   - Document structure consistency
   - Configurable sampling strategy
   - Content type analysis for documents with issues

3. **Segment Health** (with --detailed)
   - Corrupt segment detection
   - Multi-segment consistency
   - Segment reader validation
   - Segment statistics

4. **File System Analysis** (with --detailed)
   - Orphaned file detection
   - File pattern validation
   - Index file consistency

#### Validation Report

When using the `--output` option, the tool generates a detailed Markdown report containing:

1. **Summary Section**
   - Total errors and warnings
   - Sampling strategy used
   - Index structure overview

2. **Field Analysis**
   - Complete list of index fields
   - Required field status
   - Field presence statistics

3. **Document Analysis**
   - Document integrity results
   - Content type breakdown for issues
   - Sampling coverage details

4. **Detailed Checks** (with --detailed)
   - Segment analysis results
   - File system consistency
   - Orphaned file detection results

### Customizing Validation

#### Document Sampling Configuration

Control how many documents are validated:

```powershell
# Full validation of all documents
sn-index-maintenance-suite validate --path <index-path> --sample-size 0

# Increase sample size for more thorough sampling
sn-index-maintenance-suite validate --path <index-path> --sample-size 100
```

#### Field Name Mapping

If your index uses different field names, you can map them:

```powershell
# Map standard field names to custom ones
sn-index-maintenance-suite validate --path <index-path> --field-mapping '{
    "Id": "Id",
    "VersionId": "DocumentVersion",
    "Path": "DocumentPath"
}'
```

#### Required Fields

By default, the following fields are validated:
- `Id` (or custom mapped name)
- `VersionId`
- `NodeTimestamp`
- `VersionTimestamp`
- `Path`
- `Version`
- `IsLastPublic`
- `IsLastDraft`

### Performance Considerations

1. **Sampling Strategy**
   - Default sampling checks 10 documents
   - Full validation can be slow on large indexes
   - Sampling interval is calculated to spread checks across the index

2. **Backup Impact**
   - Backup is enabled by default but can be disabled
   - Consider disk space when validating large indexes

3. **Memory Usage**
   - Document sampling is designed to be memory-efficient
   - Field analysis loads only metadata, not content

## Technical Dependencies

- **.NET 8.0+**: Runtime environment
- **Lucene.NET**: For index access and manipulation
- **System.CommandLine**: For processing command-line arguments
- **SenseNet.Search.Lucene29**: For SenseNet-specific operations

## Important Considerations

### Index Locking

When setting or initializing a LastActivityId, the tool briefly acquires an exclusive lock on the Lucene index. This means:
- The index cannot be used by SenseNet or other processes during the operation
- Operations are performed as quickly as possible to minimize downtime
- Best practice is to perform operations during maintenance windows

### Backup Process

Before modifying an index, the tool creates a backup by:
1. Creating a timestamped copy of the entire index directory
2. Verifying the integrity of the backup
3. Only proceeding with modifications if the backup is successful

### Error Recovery

If an error occurs during operations:
1. The operation is abandoned
2. No partial changes are committed
3. The original index remains unchanged 
4. The backup can be manually restored if needed

### Validation Best Practices

1. Always run validation with backup enabled in production
2. Use detailed validation during maintenance windows
3. Save validation reports for tracking index health over time
4. Address warnings promptly to prevent index corruption
5. Regularly validate indices as part of maintenance routines

#### Common Validation Scenarios

1. **Quick Health Check**
   ```powershell
   sn-index-maintenance-suite validate --path <index-path>
   ```
   Performs basic validation with default sampling, suitable for routine health checks.

2. **Full Index Validation**
   ```powershell
   sn-index-maintenance-suite validate --path <index-path> --detailed --sample-size 0
   ```
   Comprehensive validation of all documents and index structures.

3. **Custom Field Requirements**
   ```powershell
   sn-index-maintenance-suite validate --path <index-path> --required-fields '["Id","Path","Name","Type"]'
   ```
   Validates specific fields important to your application.

4. **CI/CD Integration**
   ```powershell
   sn-index-maintenance-suite validate --path <index-path> --backup false --output validation.md
   ```
   Suitable for automated validation in pipelines, with report generation.

#### Error Reporting

The validation tool reports issues with different severity levels:

- **Error**: Critical issues that indicate index corruption or structural problems
- **Warning**: Potential issues that might affect index functionality
- **Info**: Informational messages about index structure and validation process

Example output:
```
[Error] Segment appears to be corrupted
[Warning] Missing required fields: Id, Path
[Info] Found 1000 documents in the index

Validation completed with 1 error and 2 warnings.
```

## Live Index Mode and Backup Management

### Live Index Flag

The toolkit now includes a `--live-index` flag for all operations. When this flag is set to `true`, the toolkit enforces read-only operations on the index to ensure safety for production indexes.

```powershell
# Safely read from a live index
sn-index-maintenance-suite lastactivityid-get --path <index-path> --live-index true

# Compare content with a live index
sn-index-maintenance-suite compare --index-path <index-path> --connection-string <conn-string> --repository-path "/Root" --live-index true
```

When using the `--live-index` flag:
1. Write operations like `lastactivityid-set` and `lastactivityid-init` will be blocked
2. A warning will be displayed when running detailed validation operations
3. The system is protected from accidental modifications to live production indexes

### Backup Management

Backup behavior has been improved to be more intelligent about when backups are needed:

1. **Write operations** (lastactivityid-set, lastactivityid-init):
   - Backups remain enabled by default (`--backup true`)
   - This protects against data loss during modifications

2. **Read-only operations** (lastactivityid-get, list-index, compare, validate):
   - Backups are now disabled by default (`--backup false`)
   - Can be explicitly enabled if desired with `--backup true`

```powershell
# Read operation without backup (default)
sn-index-maintenance-suite validate --path <index-path>

# Read operation with explicit backup
sn-index-maintenance-suite list-index --index-path <index-path> --repository-path "/Root" --backup true
```

3. **Detailed validation** with `--live-index`:
   - Shows a warning if running detailed validation on a live index without backup
   - Recommends creating a backup even though the operation is read-only

## Usage Examples

### Working with Live Indexes

```powershell
# Check LastActivityId on a live production index
sn-index-maintenance-suite lastactivityid-get --path <index-path> --live-index true

# List index contents on a live index
sn-index-maintenance-suite list-index --index-path <index-path> --repository-path "/Root" --live-index true

# Validate a live index (basic validation, no backup)
sn-index-maintenance-suite validate --path <index-path> --live-index true 

# Detailed validation on a live index with backup
sn-index-maintenance-suite validate --path <index-path> --detailed --backup true --live-index true
```

### Modifying Indexes (Non-Live Only)

```powershell
# Set LastActivityId (only works on non-live indexes)
sn-index-maintenance-suite lastactivityid-set --path <index-path> --id 123456

# Initialize a new index (only works on non-live indexes)
sn-index-maintenance-suite lastactivityid-init --path <index-path> --id 100000
```