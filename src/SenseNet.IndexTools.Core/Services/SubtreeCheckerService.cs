namespace SenseNet.IndexTools.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Data.SqlClient;
    using System.Text;
    using System.Threading.Tasks;
    using System.Linq;
    using Microsoft.Extensions.Logging;
    using Lucene.Net.Search;
    using Lucene.Net.Store;
    using SenseNet.IndexTools.Core.Models;

    /// <summary>
    /// Service for checking if content items from a database subtree exist in the index
    /// </summary>
    public class SubtreeCheckerService
    {
        private readonly ILogger<SubtreeCheckerService> _logger;

        public SubtreeCheckerService(ILogger<SubtreeCheckerService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Represents the result of a subtree check operation
        /// </summary>
        public class SubtreeCheckResult
        {
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string RepositoryPath { get; set; } = string.Empty;
            public bool Recursive { get; set; }
            public int DatabaseItemsCount { get; set; }
            public int IndexDocCount { get; set; }
            public int MatchedItemsCount { get; set; }
            public List<ContentItem> MismatchedItems { get; set; } = new List<ContentItem>();
            public List<ContentItem> MatchedItems { get; set; } = new List<ContentItem>();
            public Dictionary<string, int> ContentTypeStats { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> MismatchesByType { get; set; } = new Dictionary<string, int>();
            public string Summary { get; set; } = string.Empty;
            public string DetailedReport { get; set; } = string.Empty;
        }

        /// <summary>
        /// Checks if content items from a database subtree exist in the index
        /// </summary>
        /// <param name="indexPath">Path to the Lucene index directory</param>
        /// <param name="connectionString">SQL connection string to the SenseNet database</param>
        /// <param name="repositoryPath">Path in the content repository to check</param>
        /// <param name="recursive">Whether to check recursively</param>
        /// <param name="detailed">Whether to include detailed information in the report</param>
        /// <returns>Check result</returns>
        public async Task<SubtreeCheckResult> CheckSubtreeAsync(
            string indexPath,
            string connectionString,
            string repositoryPath,
            bool recursive = true,
            bool detailed = false)
        {
            _logger.LogInformation("Checking subtree {Path} (Recursive: {Recursive})", repositoryPath, recursive);
            
            var result = new SubtreeCheckResult
            {
                StartTime = DateTime.Now,
                RepositoryPath = repositoryPath,
                Recursive = recursive
            };

            try
            {
                // Step 1: Get items from database
                var items = await GetItemsFromDatabaseAsync(connectionString, repositoryPath, recursive);
                result.DatabaseItemsCount = items.Count;
                _logger.LogInformation("Found {Count} items in database", items.Count);

                // Step 2: Check items in index
                await CheckItemsInIndexAsync(indexPath, items);
                
                // Step 3: Calculate statistics
                CalculateStatistics(result, items);
                
                // Step 4: Generate report
                GenerateReport(result, items, detailed);
                
                result.EndTime = DateTime.Now;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking subtree: {Message}", ex.Message);
                result.EndTime = DateTime.Now;
                result.Summary = $"Error checking subtree: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Gets items from the database
        /// </summary>
        private async Task<List<ContentItem>> GetItemsFromDatabaseAsync(
            string connectionString, 
            string repositoryPath, 
            bool recursive)
        {
            // This would be replaced with actual database querying logic
            await Task.Delay(100); // Simulate async work
            
            var items = new List<ContentItem>();
            
            // Mock data for example
            items.Add(new ContentItem
            {
                NodeId = 1,
                Path = "/Root/Example/Item1",
                NodeType = "File",
                InDatabase = true,
                VersionId = 1
            });
            
            return items;
        }

        /// <summary>
        /// Checks if items exist in the index
        /// </summary>
        private async Task CheckItemsInIndexAsync(string indexPath, List<ContentItem> items)
        {
            // This would be replaced with actual index checking logic
            await Task.Delay(100); // Simulate async work
            
            var random = new Random();
            foreach (var item in items)
            {
                // Simulate some items missing from index
                item.InIndex = random.Next(10) > 1;
                if (item.InIndex)
                {
                    item.IndexNodeId = item.NodeId.ToString();
                    item.IndexVersionId = item.VersionId.ToString();
                }
            }
        }

        /// <summary>
        /// Calculates statistics based on the check results
        /// </summary>
        private void CalculateStatistics(SubtreeCheckResult result, List<ContentItem> items)
        {
            // Count matched items
            result.MatchedItemsCount = items.Count(i => i.InDatabase && i.InIndex);
            _logger.LogInformation("Matched items: {Count}", result.MatchedItemsCount);
            
            // Separate matched and mismatched items
            result.MatchedItems = items.Where(i => i.InDatabase && i.InIndex).ToList();
            result.MismatchedItems = items.Where(i => i.InDatabase && !i.InIndex).ToList();
            
            // Count by content type
            foreach (var item in items)
            {
                var contentType = item.NodeType ?? "Unknown";
                
                if (!result.ContentTypeStats.ContainsKey(contentType))
                    result.ContentTypeStats[contentType] = 0;
                result.ContentTypeStats[contentType]++;
                
                if (item.InDatabase && !item.InIndex)
                {
                    if (!result.MismatchesByType.ContainsKey(contentType))
                        result.MismatchesByType[contentType] = 0;
                    result.MismatchesByType[contentType]++;
                }
            }
            
            // Calculate index doc count (could be different from DB count)
            result.IndexDocCount = result.MatchedItemsCount;
        }

        /// <summary>
        /// Generates a report based on the check results
        /// </summary>
        private void GenerateReport(SubtreeCheckResult result, List<ContentItem> items, bool detailed)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("## Subtree Check Report");
            sb.AppendLine();
            sb.AppendLine($"- **Repository Path**: {result.RepositoryPath}");
            sb.AppendLine($"- **Start Time**: {result.StartTime}");
            sb.AppendLine($"- **Duration**: {(result.EndTime - result.StartTime).TotalSeconds:F2} seconds");
            sb.AppendLine($"- **Recursive**: {result.Recursive}");
            sb.AppendLine();
            sb.AppendLine("### Summary");
            sb.AppendLine();
            sb.AppendLine($"- **Database Items**: {result.DatabaseItemsCount}");
            sb.AppendLine($"- **Index Documents**: {result.IndexDocCount}");
            sb.AppendLine($"- **Matched Items**: {result.MatchedItemsCount}");
            sb.AppendLine($"- **Mismatched Items**: {result.MismatchedItems.Count}");
            
            if (result.MismatchedItems.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### Mismatches by Content Type");
                sb.AppendLine();
                sb.AppendLine("| Content Type | Count |");
                sb.AppendLine("|-------------|-------|");
                
                foreach (var mismatch in result.MismatchesByType.OrderByDescending(m => m.Value))
                {
                    sb.AppendLine($"| {mismatch.Key} | {mismatch.Value} |");
                }
            }
            
            result.Summary = sb.ToString();
            
            if (detailed && result.MismatchedItems.Count > 0)
            {
                var detailedSb = new StringBuilder(result.Summary);
                
                detailedSb.AppendLine();
                detailedSb.AppendLine("### Mismatched Items");
                detailedSb.AppendLine();
                detailedSb.AppendLine("| NodeId | Path | Content Type |");
                detailedSb.AppendLine("|--------|------|-------------|");
                
                foreach (var item in result.MismatchedItems.OrderBy(i => i.Path))
                {
                    detailedSb.AppendLine($"| {item.NodeId} | {item.Path} | {item.NodeType} |");
                }
                
                result.DetailedReport = detailedSb.ToString();
            }
            else
            {
                result.DetailedReport = result.Summary;
            }
        }
    }
}
