# User Story: Database-Index Synchronization Checking

## Story ID: US-020
## Title: As a system administrator, I want to compare database content with index content so that I can identify synchronization issues and missing items.

## Description
SenseNet indexes must stay synchronized with the content repository database. Users need tools to compare database content with index content, identify mismatches, and generate detailed reports about synchronization status.

## Acceptance Criteria
- [x] CLI command `check-subtree` for database-index comparison
- [x] Support for specific path-based comparisons with depth control
- [x] Multiple search strategies (case-sensitive, case-insensitive, InFolder+Name, InTree)
- [x] Direct scan fallback for comprehensive coverage
- [x] Detailed mismatch detection and reporting
- [x] Content type distribution analysis
- [x] Version state tracking (published vs draft content)
- [x] Multiple report formats (default, detailed, tree, full)
- [x] Markdown report generation with statistics
- [x] Proper handling of large indexes with paging
- [x] Enhanced item detection with multiple matching strategies

## Business Value
- Critical tool for detecting database-index synchronization issues
- Identification of missing or orphaned content
- Content type-specific analysis for targeted troubleshooting
- Version-aware comparison for complex content scenarios
- Essential for maintaining index integrity and search accuracy

## Technical Notes
- Implements SubtreeIndexChecker.cs with comprehensive comparison logic
- Uses ContentComparer.cs for structured item comparison
- Multiple search strategies for robust item detection
- SQL queries with proper joins for accurate database content retrieval
- Lucene.NET queries with various search patterns
- Statistical analysis and detailed reporting capabilities

## Status: Completed
## Completed Date: 2025-05-26
## Commit Reference: 64ecb93
## Dependencies: US-000 (LastActivityId Management), US-019 (Index Validation)
