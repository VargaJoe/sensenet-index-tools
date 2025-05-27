using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging;
using IODirectory = System.IO.Directory;

namespace SenseNet.IndexTools.Core.Services
{
    /// <summary>
    /// Service for listing items from a SenseNet index
    /// </summary>
    public class IndexListerService
    {
        private readonly ILogger<IndexListerService> _logger;

        public IndexListerService(ILogger<IndexListerService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Represents an item in the index
        /// </summary>
        public class IndexItem
        {
            public string Id { get; set; } = string.Empty;
            public string VersionId { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
        }

        /// <summary>
        /// Result of an index listing operation
        /// </summary>
        public class IndexListResult
        {
            public string IndexPath { get; set; } = string.Empty;
            public string RepositoryPath { get; set; } = string.Empty;
            public bool Recursive { get; set; }
            public int Depth { get; set; }
            public DateTime StartTime { get; set; } = DateTime.Now;
            public DateTime EndTime { get; set; } = DateTime.Now;
            public int TotalDocuments { get; set; }
            public List<IndexItem> Items { get; set; } = new List<IndexItem>();
            public List<string> Warnings { get; set; } = new List<string>();
            public List<string> Errors { get; set; } = new List<string>();
        }

        /// <summary>
        /// Lists items from a SenseNet index matching the specified criteria
        /// </summary>
        /// <param name="indexPath">Path to the Lucene index directory</param>
        /// <param name="repositoryPath">Path in the content repository to list from</param>
        /// <param name="recursive">Whether to list items recursively</param>
        /// <param name="depth">Depth limit (0=all descendants, 1=direct children only)</param>
        /// <returns>List of items found in the index</returns>
        public async Task<IndexListResult> ListIndexItemsAsync(string indexPath, string repositoryPath, bool recursive, int depth = 0)
        {
            var result = new IndexListResult
            {
                IndexPath = indexPath,
                RepositoryPath = repositoryPath,
                Recursive = recursive,
                Depth = depth,
                StartTime = DateTime.Now
            };

            if (!IODirectory.Exists(indexPath))
            {
                result.Errors.Add($"Index directory not found: {indexPath}");
                result.EndTime = DateTime.Now;
                return result;
            }

            try
            {
                using var directory = FSDirectory.Open(new DirectoryInfo(indexPath));
                using var reader = IndexReader.Open(directory, true);
                using var searcher = new IndexSearcher(reader);

                _logger.LogInformation("Opening index with {Count} total documents", reader.MaxDoc());
                _logger.LogInformation("Searching for path: {Path} (Recursive: {Recursive}, Depth: {Depth})", 
                    repositoryPath, recursive, depth);

                result.TotalDocuments = reader.MaxDoc();

                // Convert path to lowercase for SenseNet indexes which store paths in lowercase
                var normalizedPath = repositoryPath.ToLowerInvariant();

                Query query;
                if (!recursive)
                {
                    // Direct match only - exact path
                    query = new TermQuery(new Term("Path", normalizedPath));
                }
                else
                {
                    var boolQuery = new BooleanQuery();
                    
                    // Root path match
                    boolQuery.Add(new TermQuery(new Term("Path", normalizedPath)), BooleanClause.Occur.SHOULD);

                    // Child path matches
                    var childQuery = new PrefixQuery(new Term("Path", normalizedPath.TrimEnd('/') + "/"));
                    boolQuery.Add(childQuery, BooleanClause.Occur.SHOULD);

                    query = boolQuery;
                }

                int pathSegmentCount = normalizedPath.Split('/').Length - 1;
                
                // Implement paging for large indexes
                const int PageSize = 10000; // Process 10000 documents at a time
                int totalProcessed = 0;
                
                // Create a collector to find all matching documents
                var initialCollector = TopScoreDocCollector.Create(1000000, true); // Use a large limit
                searcher.Search(query, initialCollector);
                var topDocs = initialCollector.TopDocs();
                int totalHits = topDocs.TotalHits;
                
                _logger.LogInformation("Found {Count} items in index matching the query...", totalHits);
                
                // Process in batches to avoid memory issues
                while (totalProcessed < totalHits)
                {
                    var collector = TopScoreDocCollector.Create(1000000, true); // Use a large limit
                    searcher.Search(query, collector);
                    var searchHits = collector.TopDocs(totalProcessed, Math.Min(PageSize, totalHits - totalProcessed)).ScoreDocs;
                    
                    if (searchHits.Length == 0) break; // No more results
                    
                    foreach (var hitDoc in searchHits)
                    {
                        var doc = searcher.Doc(hitDoc.Doc);
                        var docPath = doc.Get("Path") ?? "?";
                    
                        // Skip if we're filtering by depth and this item exceeds our depth
                        if (depth > 0 && recursive)
                        {
                            // Count path segments to determine depth
                            int docPathSegments = docPath.Split('/').Length - 1;
                            if (docPathSegments > pathSegmentCount + depth)
                                continue;
                        }
                        
                        // Get values with fallbacks for different field names
                        var id = doc.Get("Id") ?? doc.Get("NodeId") ?? "?";
                        var versionId = doc.Get("VersionId") ?? doc.Get("Version_") ?? "?";
                        var type = doc.Get("Type") ?? doc.Get("NodeType") ?? "?";
                          
                        result.Items.Add(new IndexItem 
                        { 
                            Id = id,
                            VersionId = versionId,
                            Path = docPath,
                            Type = type
                        });
                    }
                    
                    totalProcessed += searchHits.Length;
                }

                // Sort the items by path (case-insensitive)
                result.Items = result.Items.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToList();

                if (result.Items.Count == 0)
                {
                    // If no results, add some general index stats and suggestions
                    result.Warnings.Add("Could not find any items with the specified path.");
                    result.Warnings.Add($"Total documents in index: {reader.MaxDoc()}");
                    result.Warnings.Add("Suggestions:");
                    result.Warnings.Add("1. Check if the path is correct");
                    result.Warnings.Add("2. Try searching with a parent path");
                    result.Warnings.Add("3. The index might use different field names for paths");
                    
                    // Show a sample document to help diagnose field names
                    if (reader.MaxDoc() > 0)
                    {
                        result.Warnings.Add("Sample document fields for reference:");
                        var sampleDoc = reader.Document(0);
                        foreach (var field in sampleDoc.GetFields())
                        {
                            result.Warnings.Add($"  - {field.Name()}: {field.StringValue()}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading index: {Message}", ex.Message);
                result.Errors.Add($"Error reading index: {ex.Message}");
                result.Errors.Add(ex.StackTrace ?? string.Empty);
            }

            result.EndTime = DateTime.Now;
            return result;
        }
    }
}
