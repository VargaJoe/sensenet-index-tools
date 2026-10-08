# Implementation Tasks

## Completed Stories

### Foundation CLI Features (US-000 to US-005) - May-June 2025
- [x] **US-000**: LastActivityId Management (Foundation) - Get/set/init LastActivityId in indexes
- [x] **US-001**: Index Structure Validation - Comprehensive index integrity checking
- [x] **US-002**: Database-Index Synchronization Checking - Compare database with index content
- [x] **US-003**: Content Listing and Database Operations - List content from index and database
- [x] **US-004**: Content Comparison Engine - Robust comparison logic between database and index
- [x] **US-005**: Orphaned Index Entry Cleanup - Remove stale index entries

### Enhancement Features (US-006 to US-008) - June 2025
- [x] **US-006**: Enhanced HTML Report Generation - Modern styling, interactive features, responsive design
- [x] **US-007**: Case-Sensitivity and Path Normalization - Consistent path handling, case-insensitive comparisons
- [x] **US-008**: Advanced Timestamp Comparison - Precise bigint timestamp handling, database accuracy

### Web Application Features (US-009 to US-015) - August 2025
- [ ] **US-009**: Web Application Implementation - Main operations implemented; content listing remains incomplete
- [x] **US-010**: CLI Parameter Compatibility - Backward compatibility for existing scripts
- [x] **US-011**: Shared Core Library Implementation - Code reuse between CLI and web
- [x] **US-012**: Database Last Activity ID Comparison - Index vs database synchronization checking
- [x] **US-013**: Configuration Management System - Saved configurations, reusable settings
- [x] **US-014**: Report Storage and History - Automatic report saving, historical access
- [ ] **US-015**: Web Content Listing Operation - CLI listing works; web page is a placeholder

## In Progress Stories

### Web Application Refinement
**Goal:** Complete remaining web application features and polish.

#### Tasks
- [ ] Implement Content Listing operation page
- [ ] UI/UX polish and error handling
- [ ] (Optional) Add authentication

## Planned Stories

### Index Creation from Scratch (US-016 to US-023)
**Goal:** Implement comprehensive index creation capabilities using SenseNet's own indexer.

#### High Priority (Core Functionality)
- [ ] **US-016**: Index Creation Foundation - Basic index creation from SenseNet content (1-2 weeks)
- [ ] **US-017**: Content Type and Field Mapping - SenseNet-specific field mapping (1 week)
- [ ] **US-018**: Batch Processing and Performance - Efficient processing for large repositories (1 week)
- [ ] **US-021**: Index Validation and Verification - Post-creation validation (0.5 week)
- [ ] **US-022**: Error Handling and Recovery - Robust error handling and recovery (0.5-1 week)

#### Medium Priority (Enhanced Features)
- [ ] **US-019**: Incremental Index Creation - Builder functionality still planned; REST RebuildIndex requests are a separate implemented operation
- [ ] **US-020**: Web UI for Index Creation - Browser interface for index creation (0.5-1 week)
- [ ] **US-023**: Documentation and Training - Comprehensive documentation (0.5 week)

### Future Enhancement Stories
_(Additional features for future consideration)_

## Kubernetes Deployment Support (US-024) - September 2025
**Goal:** Enable the index tools to work with SenseNet deployments in Kubernetes environments.

#### Current status (2026-10-08 stabilization)
- [x] Shared CLI auto-copy input for every index-based command.
- [x] Direct C# kubectl with real deployment selector, ready-pod selection and explicit container support.
- [x] Windows destination handling through child-process working directory, with no global cwd changes.
- [x] Unique partial copies and Lucene readability validation.
- [x] Local-only repair behavior, with no automatic upload or pod restart.
- [x] Synthetic/mock regression coverage; Windows/Linux .NET 8 CI configured.
- [ ] Validate this revised implementation against a real cluster, SQL database and SenseNet repository API.
- [ ] Establish consistent source snapshot procedure; live cp alone does not prove snapshot consistency.

## Stabilization acceptance
- See `test/IndexTools.Tests` for the current automated regression suite.
- Existing create-index PR #7 is experimental: native field mapping, scope/limits, overwrite safeguards and full index compatibility must be completed before production use.
- Historical checked tasks are implementation records, not proof of current production readiness.
