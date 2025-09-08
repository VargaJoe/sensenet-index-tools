# User Story: Content Listing and Database Operations

## Story ID: US-021
## Title: As a system administrator, I want to list content from both index and database so that I can inspect data structure and verify content organization.

## Description
Users need the ability to list and inspect content from both Lucene indexes and SenseNet databases to understand data structure, verify content organization, and perform targeted analysis of specific content areas.

## Acceptance Criteria
- [x] CLI command `list-index` for listing content from Lucene index
- [x] CLI command `list-db` for listing content from SenseNet database
- [x] Path-based filtering with automatic case handling
- [x] Depth control for listing (direct children vs all descendants)
- [x] Consistent behavior between index and database listing
- [x] Support for both Id and NodeId field compatibility
- [x] Sorted results by path for better readability
- [x] Root item inclusion with proper depth handling
- [x] Case-insensitive path ordering for consistent results
- [x] Proper handling of large content sets with performance optimization

## Business Value
- Detailed inspection capabilities for content structure analysis
- Verification of content organization and hierarchy
- Comparison baseline for database-index synchronization
- Troubleshooting support for specific content areas
- Understanding of content distribution patterns

## Technical Notes
- Implements IndexLister.cs for Lucene index content listing
- Implements DatabaseLister.cs for database content retrieval
- Consistent API design between index and database operations
- Optimized queries for performance with large content sets
- Proper path normalization and case handling
- Support for various field name conventions (Id/NodeId compatibility)

## Status: Completed
## Completed Date: 2025-05-26
## Commit Reference: 64ecb93 (part of database synchronization feature)
## Dependencies: US-000 (LastActivityId Management)
