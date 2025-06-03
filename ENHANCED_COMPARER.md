# Enhanced Content Comparison

The `ContentComparer` class has been significantly improved to better handle the comparison between database items and index entries in SenseNet. This document outlines the key improvements and how they improve the reliability and accuracy of content comparison operations.

## Key Improvements

### Multi-Version Support

The comparison engine now properly handles multiple versions of the same document with the same path:

- Previously: Single document per path was stored, overwriting other versions
- Now: Multiple documents with the same path but different NodeIds are properly tracked and matched

### Enhanced Path Normalization

Path normalization has been improved to ensure consistent matching between database and index paths:

- Case-insensitive path comparison
- Trailing slash normalization
- Path format standardization (backslash to forward slash conversion)
- Special handling for content type paths

### Better Matching Logic

The matching algorithm has been enhanced with a multi-stage approach:

1. First attempts to match by both path and NodeId (exact match)
2. Falls back to path-only matching if NodeId doesn't match
3. Finally tries to match by NodeId and VersionId regardless of path (detects renamed items)

### Improved Orphaned Entry Detection

Orphaned entries are now more accurately identified:

- Items are only marked as "Index only" when all matching attempts fail
- Paths are examined for similarity to detect minor path differences
- NodeId-based matching can identify renamed items in the database

### Detailed Logging

Enhanced logging provides better visibility into the matching process:

- Path normalization details
- Multi-version document detection
- Match type information (exact match, path-only, or ID-only)
- Potential match suggestions for unmatched items

## Technical Details

The core improvements are implemented through:

- Dictionary structure changed from `Dictionary<string, ContentItem>` to `Dictionary<string, Dictionary<string, ContentItem>>` to support multiple documents per path
- Enhanced grouping key in LINQ queries to include NodeId/IndexNodeId alongside path and type
- More sophisticated path normalization with better handling of special cases
- Three-phase matching algorithm to maximize the chance of finding corresponding items

## Benefits

These improvements provide several key benefits:

1. More accurate orphaned entry identification
2. Better detection of renamed items
3. Proper handling of version history
4. Reduced false positives when cleaning up orphaned entries
5. More detailed diagnostic information

The enhanced comparer is used by both the `compare` and `clean-orphaned` commands to ensure consistent and reliable results.
