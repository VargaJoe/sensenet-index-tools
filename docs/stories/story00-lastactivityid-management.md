# User Story: LastActivityId Management (Foundation)

## Story ID: US-000
## Title: As a system administrator, I want to manage LastActivityId values in SenseNet indexes so that I can maintain proper activity tracking and synchronization.

## Description
SenseNet indexes require proper LastActivityId tracking for synchronization with the content repository. Users need the ability to read, set, and initialize LastActivityId values in Lucene indexes for maintenance and recovery scenarios.

## Acceptance Criteria
- [x] CLI command `lastactivityid-get` to retrieve current LastActivityId from index
- [x] CLI command `lastactivityid-set` to update LastActivityId in existing index
- [x] CLI command `lastactivityid-init` to initialize LastActivityId in non-SenseNet indexes
- [x] Automatic backup creation before making changes (configurable)
- [x] Custom backup path support to prevent interference with live indexes
- [x] Validation that target is a valid Lucene index
- [x] Support for both SenseNet API and direct Lucene methods
- [x] Proper error handling for invalid indexes or values
- [x] Safety checks with --offline flag for write operations

## Business Value
- Essential foundation for SenseNet index maintenance
- Disaster recovery capability for corrupted activity tracking
- Integration support for non-SenseNet indexes into SenseNet ecosystem
- Safe maintenance operations with automatic backups
- Operational flexibility for various maintenance scenarios

## Technical Notes
- Uses SenseNet.Search.Lucene29.Lucene29LocalIndexingEngine for SenseNet API access
- Falls back to direct Lucene.NET operations when SenseNet API unavailable
- Implements IndexDirectory wrapper for SenseNet compatibility
- Custom backup system with configurable paths
- Comprehensive validation and error handling

## Status: Completed (Foundation)
## Completed Date: 2025-05-08
## Commit Reference: 6c0704c
## Dependencies: None (Foundation feature)
