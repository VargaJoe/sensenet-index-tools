# User Story: Index Creation Foundation

## Story ID: US-011
## Title: As a system administrator, I want to create a new Lucene index from SenseNet content so that I can rebuild corrupted indexes or create indexes for new repositories.

## Description
Currently the tool can only work with existing indexes. Users need the ability to create a new index from scratch using SenseNet's content repository data, which is essential for disaster recovery and new deployments.

## Acceptance Criteria
- [ ] CLI command `create-index` with comprehensive options
- [ ] Database connection to SenseNet content repository
- [ ] Index directory creation and validation
- [ ] Basic content retrieval from database tables
- [ ] SenseNet-specific document structure creation
- [ ] Progress tracking for long-running operations
- [ ] Error handling for connection and permission issues
- [ ] Backup integration for existing indexes
- [ ] Cancellation support for interrupted operations

## Business Value
- Disaster recovery capability for corrupted indexes
- Support for new SenseNet repository deployments
- Index rebuilding without full system restoration
- Operational flexibility for maintenance scenarios
- Reduced downtime during index-related issues

## Technical Notes
- Use SenseNet.Search.Indexing.IndexDirectory for index creation
- Implement SenseNet.Search.Lucene29.Lucene29LocalIndexingEngine
- Query Nodes, Versions, and related tables from SenseNet database
- Handle SenseNet-specific field mappings and content types
- Integrate with existing backup and validation systems

## Status: Planned
## Estimated Effort: 1-2 weeks
## Priority: High
## Dependencies: SenseNet.ContentRepository package integration
