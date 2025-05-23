# Pull Request: Enhanced Subtree Index Checking

## Description

This PR significantly improves the `check-subtree` command's ability to detect items in the SenseNet Lucene index by implementing multiple search strategies and adding detailed content type analysis. It addresses the issue where items that should be found in the index were being reported as missing because of the limited search strategy.

## Changes Made

### 1. Enhanced Index Search

- Implemented a multi-strategy approach to find items in the index:
  - Primary ID-based search (VersionId and NodeId)
  - Path-based search (exact and lowercase)
  - Hierarchical relationship search (InTree and InFolder fields)
  - Name-based search as a last resort

### 2. Improved Database Query

- Updated SQL query to focus on the latest versions of content (published or draft)
- Added version state information (Published, Draft, Historical)
- Improved filtering to reduce unnecessary processing

### 3. Enhanced Reports

- Added content type distribution statistics
- Included version state in reports for better analysis
- Improved Markdown formatting for better readability
- Added mismatch percentage calculations by content type

### 4. PowerShell Helper

- Updated PowerShell script with more options and better user experience
- Added color-coded output for better readability
- Added option to automatically open reports

### 5. Testing

- Added test project (TestSubtreeChecker) to validate the enhanced search functionality
- Created test cases for each search strategy

### 6. Documentation

- Added ENHANCED_ITEM_DETECTION.md document explaining the improved algorithm
- Updated DOCUMENTATION.md with details about the new features
- Updated SUBTREE_CHECKER_README.md with new command examples
- Updated FUTURE_PLAN.md to reflect completed items

## How to Test

1. Run the test project:
   ```
   dotnet run --project src/TestSubtreeChecker/TestSubtreeChecker.csproj
   ```

2. Run a real-world test with a SenseNet database and index:
   ```
   ./CheckSubtree.ps1 -indexPath "<path-to-index>" -connectionString "<connection-string>" -repositoryPath "/Root/Content/Path" -detailed $true
   ```

3. Compare results with the previous version by checking if items previously reported as missing are now found correctly.

## Performance Considerations

The enhanced search strategy performs more checks when items aren't found by their primary identifiers, which could slightly increase processing time for items that are missing from the index. However, this is offset by the improved accuracy and the resulting reduction in false negatives.

## Additional Notes

This enhancement was inspired by examining the SenseNet/sn-search-lucene29 repository's IndexIntegrityChecker implementation, which uses multiple strategies to check for items in the index.
