using System.CommandLine;
using System.Data.SqlClient;
using System.Text;
using Lucene.Net.Store;
using IOFile = System.IO.File;
using IODirectory = System.IO.Directory;

namespace SenseNetIndexTools
{
    public class SubtreeIndexChecker
    {
        private class CheckReport
        {
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string RepositoryPath { get; set; } = string.Empty;
            public bool Recursive { get; set; }
            public int DatabaseItemsCount { get; set; }
            public int IndexDocCount { get; set; }
            public int MatchedItemsCount { get; set; }
            public List<ContentComparer.ContentItem> MismatchedItems { get; set; } = new List<ContentComparer.ContentItem>();
            public List<ContentComparer.ContentItem> MatchedItems { get; set; } = new List<ContentComparer.ContentItem>();
            public Dictionary<string, int> ContentTypeStats { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> MismatchesByType { get; set; } = new Dictionary<string, int>();
            public string Summary { get; set; } = string.Empty;
            public string DetailedReport { get; set; } = string.Empty;
        }

        public static Command Create()
        {
            var command = new Command("check-subtree", "Check if content items from a database subtree exist in the index");

            var indexPathOption = new Option<string>(
                name: "--index-path", 
                description: "Path to the Lucene index directory");
            indexPathOption.IsRequired = true;

            var connectionStringOption = new Option<string>(
                name: "--connection-string",
                description: "SQL Connection string to the SenseNet database");
            connectionStringOption.IsRequired = true;

            var repositoryPathOption = new Option<string>(
                name: "--repository-path",
                description: "Path in the content repository to check (e.g., /Root/Sites/Default_Site)");
            repositoryPathOption.IsRequired = true;

            var outputOption = new Option<string?>(
                name: "--output",
                description: "Path to save the check report to a file");

            var recursiveOption = new Option<bool>(
                name: "--recursive",
                description: "Recursively check all content items under the specified path",
                getDefaultValue: () => true);

            var depthOption = new Option<int>(
                name: "--depth",
                description: "Limit checking to specified depth (1=direct children only, 0=all descendants)",
                getDefaultValue: () => 0);

            var reportFormatOption = new Option<string>(
                name: "--report-format",
                description: "Format of the report: 'default' (statistics only), 'detailed' (with content type breakdown), 'tree' (with hierarchical view), or 'full' (all information)",
                getDefaultValue: () => "default");
            reportFormatOption.FromAmong("default", "detailed", "tree", "full");

            command.AddOption(indexPathOption);
            command.AddOption(connectionStringOption);
            command.AddOption(repositoryPathOption);
            command.AddOption(outputOption);
            command.AddOption(recursiveOption);
            command.AddOption(depthOption);
            command.AddOption(reportFormatOption);

            command.SetHandler((string indexPath, string connectionString, string repositoryPath, 
                string? output, bool recursive, int depth, string reportFormat) =>
            {
                try
                {
                    if (!IODirectory.Exists(indexPath))
                    {
                        Console.Error.WriteLine($"Index directory not found: {indexPath}");
                        Environment.Exit(1);
                        return;
                    }

                    var report = new CheckReport
                    {
                        StartTime = DateTime.Now,
                        RepositoryPath = repositoryPath,
                        Recursive = recursive
                    };

                    // Use our established ContentComparer to get and compare items
                    var comparer = new ContentComparer();
                    var results = comparer.CompareContent(indexPath, connectionString, repositoryPath, recursive, depth);

                    // Process results for the report
                    ProcessResults(results, report, reportFormat != "default");

                    report.EndTime = DateTime.Now;
                    GenerateReport(report, output, reportFormat);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error checking subtree: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    Environment.Exit(1);
                }
            }, indexPathOption, connectionStringOption, repositoryPathOption, outputOption, recursiveOption, depthOption, reportFormatOption);

            return command;
        }

        private static void ProcessResults(List<ContentComparer.ContentItem> results, CheckReport report, bool detailed)
        {
            report.DatabaseItemsCount = results.Count(r => r.InDatabase);
            report.IndexDocCount = results.Count(r => r.InIndex);
            report.MatchedItemsCount = results.Count(r => r.InDatabase && r.InIndex && r.Status == "Match");
            
            // Separate matched and mismatched items
            report.MatchedItems = results.Where(r => r.Status == "Match").ToList();
            report.MismatchedItems = results.Where(r => r.Status != "Match").ToList();

            // Calculate content type statistics
            foreach (var result in results)
            {
                var type = result.NodeType ?? "Unknown";
                report.ContentTypeStats.TryGetValue(type, out int count);
                report.ContentTypeStats[type] = count + 1;

                if (result.Status != "Match")
                {
                    report.MismatchesByType.TryGetValue(type, out count);
                    report.MismatchesByType[type] = count + 1;
                }
            }
        }

        private static void GenerateReport(CheckReport report, string? outputPath, string reportFormat)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Subtree Index Check Report");
            sb.AppendLine();
            sb.AppendLine("## Check Information");
            sb.AppendLine($"- Repository Path: {report.RepositoryPath}");
            sb.AppendLine($"- Recursive: {report.Recursive}");
            sb.AppendLine($"- Start Time: {report.StartTime}");
            sb.AppendLine($"- End Time: {report.EndTime}");
            sb.AppendLine($"- Duration: {(report.EndTime - report.StartTime).TotalSeconds:F2} seconds");
            sb.AppendLine();
            
            // Summary section
            sb.AppendLine("## Summary");
            sb.AppendLine($"- Items in Database: {report.DatabaseItemsCount}");
            sb.AppendLine($"- Items in Index: {report.IndexDocCount}");
            sb.AppendLine($"- Matched Items: {report.MatchedItemsCount}");
            sb.AppendLine($"- Mismatched Items: {report.MismatchedItems.Count}");
            sb.AppendLine();

            if (reportFormat != "default")
            {
                // Content Type Statistics
                sb.AppendLine("## Content Type Statistics");
                sb.AppendLine("| Type | Total Items | Mismatches | Match Rate |");
                sb.AppendLine("|------|-------------|------------|------------|");
                foreach (var type in report.ContentTypeStats.Keys.OrderBy(k => k))
                {
                    report.MismatchesByType.TryGetValue(type, out int mismatches);
                    var total = report.ContentTypeStats[type];
                    var matchRate = ((total - mismatches) * 100.0 / total).ToString("F1");
                    sb.AppendLine($"| {type} | {total} | {mismatches} | {matchRate}% |");
                }
                sb.AppendLine();

                if (report.MismatchedItems.Any())
                {
                    // Group mismatches by type for better organization
                    var mismatchesByType = report.MismatchedItems
                        .GroupBy(x => x.NodeType)
                        .OrderByDescending(g => g.Count());

                    sb.AppendLine("## Mismatches by Content Type");
                    foreach (var typeGroup in mismatchesByType)
                    {
                        sb.AppendLine($"### {typeGroup.Key}");
                        sb.AppendLine("| Status | DB NodeId | DB VerID | Index NodeId | Index VerID | Path |");
                        sb.AppendLine("|---------|-----------|----------|--------------|-------------|------|");
                        
                        foreach (var item in typeGroup.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
                        {
                            var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                            var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                            var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                            var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                            sb.AppendLine($"| {item.Status} | {dbNodeId} | {dbVerID} | {idxNodeId} | {idxVerID} | {item.Path} |");
                        }
                        sb.AppendLine();
                    }
                }

                if (reportFormat == "full")
                {
                    // Full Item List (like in compare function)
                    sb.AppendLine("## Complete Item List");
                    sb.AppendLine("| Status | DB NodeId | DB VerID | Index NodeId | Index VerID | Path | Type |");
                    sb.AppendLine("|---------|-----------|----------|--------------|-------------|------|------|");

                    // Get all items (both matched and mismatched)
                    var allItems = report.MismatchedItems.ToList();
                    if (report.MatchedItems != null)
                    {
                        allItems.AddRange(report.MatchedItems);
                    }

                    foreach (var item in allItems.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
                    {
                        var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                        var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                        var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                        var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                        sb.AppendLine($"| {item.Status} | {dbNodeId} | {dbVerID} | {idxNodeId} | {idxVerID} | {item.Path} | {item.NodeType} |");
                    }
                    sb.AppendLine();
                }
            }

            // Always show summary on console
            Console.WriteLine($"\nSubtree Check Summary:");
            Console.WriteLine($"Items in Database: {report.DatabaseItemsCount}");
            Console.WriteLine($"Items in Index: {report.IndexDocCount}");
            Console.WriteLine($"Matched Items: {report.MatchedItemsCount}");
            Console.WriteLine($"Mismatched Items: {report.MismatchedItems.Count}");

            if (report.MismatchedItems.Any())
            {
                Console.WriteLine("\nMismatch Summary by Type:");
                foreach (var type in report.MismatchesByType.OrderByDescending(x => x.Value))
                {
                    Console.WriteLine($"{type.Key}: {type.Value} mismatches");
                }
            }

            // Save report if output path is specified
            if (!string.IsNullOrEmpty(outputPath))
            {
                File.WriteAllText(outputPath, sb.ToString());
                Console.WriteLine($"\nDetailed report saved to: {outputPath}");
            }
        }
    }
}
