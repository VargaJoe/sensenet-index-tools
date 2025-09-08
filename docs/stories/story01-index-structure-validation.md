# User Story: Index Structure Validation

## Story ID: US-019
## Title: As a system administrator, I want comprehensive index structure validation so that I can identify and diagnose integrity issues in Lucene indexes.

## Description
Lucene indexes can become corrupted or have structural issues that affect performance and reliability. Users need comprehensive validation tools to detect various types of index problems including missing fields, corrupt segments, and document integrity issues.

## Acceptance Criteria
- [x] CLI command `validate` for comprehensive index structure validation
- [x] Basic structure validation (directory existence, segments file, lock status)
- [x] Document integrity validation with content type analysis
- [x] SenseNet-specific field validation (Id, VersionId, Path, timestamps)
- [x] Detailed and summary validation modes
- [x] Content type tracking for missing field analysis
- [x] Sample-based validation for large indexes (configurable sample size)
- [x] Commit document detection and validation
- [x] Comprehensive reporting with validation statistics
- [x] Support for custom required fields specification

## Business Value
- Early detection of index corruption and integrity issues
- Detailed analysis of index health and structure
- Content type-specific problem identification
- Proactive maintenance to prevent index failures
- Better understanding of index composition and quality

## Technical Notes
- Implements ValidateCommand.cs with comprehensive validation logic
- Uses Lucene.NET IndexReader for direct index access
- Supports both basic and detailed validation modes
- Configurable sampling for performance with large indexes
- Content type analysis for systematic problem identification
- Extensive error categorization and reporting

## Status: Completed
## Completed Date: 2025-05-23
## Commit Reference: d046195
## Dependencies: US-000 (LastActivityId Management)
