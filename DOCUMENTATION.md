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

The validate command performs comprehensive checks on your Lucene index:

```powershell
# Basic validation
sn-index-maintenance-suite validate --path <index-path>

# Detailed validation with report
sn-index-maintenance-suite validate --path <index-path> --detailed --output report.md

# Validation without backup
sn-index-maintenance-suite validate --path <index-path> --backup false
```

#### Validation Options

- `--path`: Required. Path to the Lucene index directory
- `--detailed`: Optional. Enables comprehensive validation checks
- `--output`: Optional. Path to save the validation report
- `--backup`: Optional. Create a backup before validation (default: true)
- `--backup-path`: Optional. Custom path for backups

#### Validation Report Sections

The validation report includes the following sections:

1. **Summary**
   ```markdown
   # SenseNet Index Validation Report
   Generated: [Timestamp]
   
   ## Summary
   - Errors: [count]
   - Warnings: [count]
   - Info: [count]
   ```

2. **Basic Structure**
   ```markdown
   [Info] Index directory structure verified
   Details: Directory contains X files
   
   [Info] Segments file found
   Details: Current segments file: segments.gen
   ```

3. **Lock Status**
   ```markdown
   [Info/Warning] Index lock status
   Details: The index is [not] currently locked
   ```

4. **Document Integrity**
   ```markdown
   [Info] Document integrity check
   Details: Sampled X documents, Y have required fields
   
   [Warning] Document integrity issues (if any)
   Details: Found X document(s) with issues out of Y sampled
   ```

5. **Field Structure**
   ```markdown
   [Info] Index field structure
   Details: Index contains X unique field names
   
   [Warning] Missing SenseNet-specific fields (if any)
   Details: Missing fields: [field list]
   ```

6. **Commit Data**
   ```markdown
   [Info] Commit user data
   Details: Contains X entries, LastActivityId = Y
   ```

7. **Segment Health**
   ```markdown
   [Info] Segment structure information
   Details: Index contains X segments
   
   [Error] Corrupt segments (if any)
   Details: Found X corrupted segment(s)
   ```

8. **File System**
   ```markdown
   [Warning] Orphaned files (if any)
   Details: Files that don't match known patterns: [file list]
   ```

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