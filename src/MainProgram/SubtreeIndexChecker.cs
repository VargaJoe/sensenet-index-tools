using System.CommandLine;
using System.CommandLine.Invocation;
using System.Data.SqlClient;
using System.Text;
using Lucene.Net.Store;
using IOFile = System.IO.File;
using IODirectory = System.IO.Directory;

namespace SenseNetIndexTools
{
    public class SubtreeIndexChecker
    {
        private class BranchStats 
        {
            public int Total { get; set; }
            public int Matches { get; set; }
        }

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
                getDefaultValue: () => 0);            var reportFormatOption = new Option<string>(
                name: "--report-format",
                description: "Format of the report: 'default' (statistics only), 'detailed' (with content type breakdown), 'tree' (with hierarchical view), or 'full' (all information)",
                getDefaultValue: () => "default");
            reportFormatOption.FromAmong("default", "detailed", "tree", "full");

            var formatOption = new Option<string>(
                name: "--format",
                description: "Format of the report: 'md' (Markdown) or 'html' (HTML format suitable for web viewing)",
                getDefaultValue: () => "md");
            formatOption.FromAmong("md", "html");

            var verboseOption = new Option<bool>(
                name: "--verbose",
                description: "Enable verbose logging",
                getDefaultValue: () => false);

            command.AddOption(indexPathOption);
            command.AddOption(connectionStringOption);
            command.AddOption(repositoryPathOption);
            command.AddOption(outputOption);
            command.AddOption(recursiveOption);
            command.AddOption(depthOption);
            command.AddOption(reportFormatOption);
            command.AddOption(formatOption);
            command.AddOption(verboseOption);

            command.SetHandler((InvocationContext context) =>
            {
                string indexPathValue = context.ParseResult.GetValueForOption(indexPathOption)
                    ?? throw new ArgumentNullException("indexPath");
                string connectionStringValue = context.ParseResult.GetValueForOption(connectionStringOption)
                    ?? throw new ArgumentNullException("connectionString");
                string repositoryPathValue = context.ParseResult.GetValueForOption(repositoryPathOption)
                    ?? throw new ArgumentNullException("repositoryPath");
                string? outputValue = context.ParseResult.GetValueForOption(outputOption);
                bool recursiveValue = context.ParseResult.GetValueForOption(recursiveOption);
                int depthValue = context.ParseResult.GetValueForOption(depthOption);
                string reportFormatValue = context.ParseResult.GetValueForOption(reportFormatOption)
                    ?? throw new ArgumentNullException("reportFormat");
                string formatValue = context.ParseResult.GetValueForOption(formatOption)
                    ?? throw new ArgumentNullException("format");
                bool verboseValue = context.ParseResult.GetValueForOption(verboseOption);

                try
                {
                    if (!IODirectory.Exists(indexPathValue))
                    {
                        Console.Error.WriteLine($"Index directory not found: {indexPathValue}");
                        Environment.Exit(1);
                        return Task.CompletedTask;
                    }

                    var report = new CheckReport
                    {
                        StartTime = DateTime.Now,
                        RepositoryPath = repositoryPathValue,
                        Recursive = recursiveValue
                    };

                    // Set verbose logging
                    ContentComparer.VerboseLogging = verboseValue;

                    // Use our established ContentComparer to get and compare items
                    var comparer = new ContentComparer();
                    var results = comparer.CompareContent(indexPathValue, connectionStringValue, repositoryPathValue, recursiveValue, depthValue);

                    // Process results for the report
                    ProcessResults(results, report, reportFormatValue != "default");
                    report.EndTime = DateTime.Now;
                    GenerateReport(report, outputValue, reportFormatValue, formatValue);

                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error checking subtree: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    Environment.Exit(1);
                    return Task.CompletedTask;
                }
            });

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
        }        private static void GenerateReport(CheckReport report, string? outputPath, string reportFormat, string format)
        {
            string reportContent;
            
            if (format.ToLower() == "html")
            {
                reportContent = GenerateHtmlReport(report, reportFormat);
            }
            else
            {
                reportContent = GenerateMarkdownReport(report, reportFormat);
            }

            // Always show summary on console            Console.WriteLine($"\nSubtree Check Summary:");
            Console.WriteLine($"Items in Database: {report.DatabaseItemsCount}");
            Console.WriteLine($"Items in Index: {report.IndexDocCount}");
            Console.WriteLine($"Matched Items: {report.MatchedItemsCount}");
            Console.WriteLine($"Mismatched Items: {report.MismatchedItems.Count}");
            var timestampMismatchCount = report.MismatchedItems.Count(i => i.Status == "Timestamp mismatch");
            if (timestampMismatchCount > 0)
            {
                Console.WriteLine($"  - Timestamp Mismatches: {timestampMismatchCount}");
            }

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
                File.WriteAllText(outputPath, reportContent);
                Console.WriteLine($"\nDetailed report saved to: {outputPath}");
            }
        }

        private static string GenerateMarkdownReport(CheckReport report, string reportFormat)
        {
            var sb = new StringBuilder();
            // Report header
            sb.AppendLine("# Subtree Index Check Report");
            sb.AppendLine();
            sb.AppendLine("## Check Information");
            sb.AppendLine($"- Repository Path: {report.RepositoryPath}");
            sb.AppendLine($"- Recursive: {report.Recursive}");
            sb.AppendLine($"- Start Time: {report.StartTime}");
            sb.AppendLine($"- End Time: {report.EndTime}");
            sb.AppendLine($"- Duration: {(report.EndTime - report.StartTime).TotalSeconds:F2} seconds");
            sb.AppendLine();            // Summary section
            sb.AppendLine("## Summary");
            sb.AppendLine($"- Items in Database: {report.DatabaseItemsCount}");
            sb.AppendLine($"- Items in Index: {report.IndexDocCount}");
            sb.AppendLine($"- Matched Items: {report.MatchedItemsCount}");
            sb.AppendLine($"- Mismatched Items: {report.MismatchedItems.Count}");
            var timestampMismatchCount = report.MismatchedItems.Count(i => i.Status == "Timestamp mismatch");
            if (timestampMismatchCount > 0)
            {
                sb.AppendLine($"  - Timestamp Mismatches: {timestampMismatchCount}");
            }
            sb.AppendLine();

            // Content type distribution
            if (reportFormat != "default" && report.ContentTypeStats.Any())
            {
                sb.AppendLine("## Content Type Statistics");
                sb.AppendLine();
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
            }
            
            // Mismatches by content type
            if (reportFormat != "default" && report.MismatchedItems.Any())
            {
                var mismatchesByType = report.MismatchedItems
                    .GroupBy(x => x.NodeType)
                    .OrderByDescending(g => g.Count());

                sb.AppendLine("## Mismatches by Content Type");
                sb.AppendLine();
                
                foreach (var typeGroup in mismatchesByType)
                {
                    sb.AppendLine($"### {typeGroup.Key}");
                    sb.AppendLine();
                    sb.AppendLine("| Status | DB NodeId | DB VerID | DB Timestamp | Index NodeId | Index VerID | Index Timestamp | Path |");
                    sb.AppendLine("|---------|-----------|----------|--------------|--------------|-------------|----------------|------|");
                    
                    foreach (var item in typeGroup.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
                    {
                        var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                        var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                        var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                        var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                        var statusInfo = FormatStatusWithDetail(item.Status, item.Status);
                        var dbTimestamp = item.InDatabase && item.Timestamp.HasValue ? item.Timestamp.Value.ToString("u") : "-";
                    var idxTimestamp = item.InIndex ? item.IndexTimestamp ?? "-" : "-";
                    sb.AppendLine($"| {statusInfo} | {dbNodeId} | {dbVerID} | {dbTimestamp} | {idxNodeId} | {idxVerID} | {idxTimestamp} | {item.Path} |");
                    }
                    sb.AppendLine();
                }
            }
            
            // Complete Item List
            if (reportFormat == "full")
            {
                sb.AppendLine("## Complete Item List");
                sb.AppendLine();
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
                    var statusInfo = FormatStatusWithDetail(item.Status, item.Status);
                    sb.AppendLine($"| {statusInfo} | {dbNodeId} | {dbVerID} | {idxNodeId} | {idxVerID} | {item.Path} | {item.NodeType} |");
                }
                sb.AppendLine();
            }
      
            // Tree format (hierarchical view)
            if (reportFormat == "tree")
            {
                sb.AppendLine("## Content Tree");
                sb.AppendLine();
                
                // Group items by path hierarchy
                var allItems = new List<ContentComparer.ContentItem>();
                if (report.MatchedItems != null)
                {
                    allItems.AddRange(report.MatchedItems);
                }
                allItems.AddRange(report.MismatchedItems);

                // Sort items by path for tree organization
                var sortedItems = allItems.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();
                
                // Create branch statistics
                var branchStats = new Dictionary<string, BranchStats>();
                foreach (var item in sortedItems)
                {
                    var path = item.Path;
                    while (!string.IsNullOrEmpty(path))
                    {
                        if (!branchStats.ContainsKey(path))
                        {
                            branchStats[path] = new BranchStats();
                        }
                        var stats = branchStats[path];
                        stats.Total++;
                        if (item.Status == "Match")
                        {
                            stats.Matches++;
                        }
                        
                        var lastSlash = path.LastIndexOf('/');
                        if (lastSlash <= 0) break;
                        path = path.Substring(0, lastSlash);
                    }
                }

                // Root node
                var rootPath = report.RepositoryPath;
                var rootItem = sortedItems.FirstOrDefault(i => i.Path == rootPath);
                var rootStatusInfo = rootItem != null 
                    ? FormatStatusWithDetail(rootItem.Status, rootItem.Status)
                    : "";
                var rootStats = branchStats.TryGetValue(rootPath, out var rs) ? rs : new BranchStats();
                var rootMatchRate = rootStats.Total > 0 ? (rootStats.Matches * 100.0 / rootStats.Total) : 100.0;
                sb.AppendLine($"{rootPath}/ {rootStatusInfo} ({rootMatchRate:F1}% match, {rootStats.Total} items)");

                // Process remaining items
                var currentLevel = new List<bool>();
                for (int i = 0; i < sortedItems.Count; i++)
                {
                    var item = sortedItems[i];
                    if (item.Path == rootPath) continue;

                    // Get relative path
                    var relativePath = item.Path;
                    if (relativePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = relativePath.Substring(rootPath.Length).TrimStart('/');
                    }
                    
                    var pathParts = relativePath.Split('/');
                    var itemName = pathParts.Last();
                    var level = pathParts.Length;
                    
                    // Adjust level trackers
                    while (currentLevel.Count > level) currentLevel.RemoveAt(currentLevel.Count - 1);
                    while (currentLevel.Count < level) currentLevel.Add(false);
                    
                    // Check if this is the last item at current level
                    var isLast = (i == sortedItems.Count - 1) || 
                        !sortedItems[i + 1].Path.StartsWith(item.Path.Substring(0, item.Path.LastIndexOf('/')));
                    currentLevel[level - 1] = !isLast;
                    
                    // Build line prefix
                    var linePrefix = string.Empty;
                    for (int j = 0; j < level - 1; j++)
                    {
                        linePrefix += currentLevel[j] ? "│   " : "    ";
                    }
                    linePrefix += isLast ? "└── " : "├── ";
                    
                    // Add item details
                    var isFolder = sortedItems.Any(x => x != item && x.Path.StartsWith(item.Path + "/"));
                    var displayName = isFolder ? itemName + "/" : itemName;
                    var statusInfo = FormatStatusWithDetail(item.Status, item.Status);
                    
                    // Add folder statistics
                    var stats = branchStats.TryGetValue(item.Path, out var itemStats) ? itemStats : new BranchStats();
                    var statsInfo = isFolder ? $" ({stats.Matches}/{stats.Total} match)" : "";
                    
                    sb.AppendLine($"{linePrefix}{displayName} {statusInfo}{statsInfo}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string GenerateHtmlReport(CheckReport report, string reportFormat)
        {
            var sb = new StringBuilder();
            
            // Start HTML document
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.AppendLine("<title>SenseNet Index Check Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(@"
                body { 
                    font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif; 
                    line-height: 1.6; 
                    max-width: 1200px; 
                    margin: 0 auto; 
                    padding: 20px;
                    color: #333;
                }
                h1, h2 { 
                    border-bottom: 1px solid #eee; 
                    padding-bottom: 0.3em; 
                    margin-top: 1.5em;
                }
                table { 
                    border-collapse: collapse; 
                    width: 100%; 
                    margin: 1em 0; 
                }
                th, td { 
                    padding: 12px; 
                    text-align: left; 
                    border-bottom: 1px solid #ddd; 
                }
                th { 
                    background: #f8f9fa;
                    font-weight: 600; 
                }
                .summary-section {
                    background: #f8f9fa;
                    padding: 20px;
                    border-radius: 4px;
                    margin-bottom: 20px;
                }
                .stats-container {
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
                    gap: 20px;
                    margin-top: 20px;
                }
                .stat-card {
                    background: white;
                    padding: 15px;
                    border-radius: 4px;
                    box-shadow: 0 1px 3px rgba(0,0,0,0.1);
                }
                .stat-title {
                    color: #666;
                    font-size: 14px;
                }
                .stat-value {
                    font-size: 24px;
                    font-weight: bold;
                    margin-top: 5px;
                }
                footer {
                    margin-top: 30px;
                    padding-top: 10px;
                    border-top: 1px solid #eee;
                    font-size: 14px;
                    color: #777;
                }
                .tree-view {
                    font-family: monospace;
                    white-space: pre;
                    margin: 20px 0;
                    line-height: 1.5;
                    background-color: #f8f9fa;
                    padding: 20px;
                    border-radius: 4px;
                    overflow-x: auto;
                }
                .tree-item {
                    margin: 0;
                    padding: 0;
                }
                .tree-line {
                    color: #666;
                }
                .tree-folder {
                    color: #0366d6;
                    font-weight: bold;
                }
                .tree-file {
                    color: #24292e;
                }
                .tree-status-match {
                    color: #28a745;
                    font-weight: bold;
                }
                .tree-status-mismatch {
                    color: #dc3545;
                    font-weight: bold;
                }
                .tree-status-info {
                    color: #666;
                    font-style: italic;
                }
                .tree-stats {
                    color: #666;
                    font-size: 0.9em;
                }
            ");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            
            // Report header
            sb.AppendLine("<header>");
            sb.AppendLine("<h1>SenseNet Index Check Report</h1>");
            sb.AppendLine("<div class=\"report-meta\">");
            sb.AppendLine($"<p><strong>Repository Path:</strong> {report.RepositoryPath}</p>");
            sb.AppendLine($"<p><strong>Recursive:</strong> {(report.Recursive ? "Yes" : "No")}</p>");
            sb.AppendLine($"<p><strong>Start Time:</strong> {report.StartTime}</p>");
            sb.AppendLine($"<p><strong>End Time:</strong> {report.EndTime}</p>");
            sb.AppendLine($"<p><strong>Duration:</strong> {(report.EndTime - report.StartTime).TotalSeconds:F2} seconds</p>");
            sb.AppendLine("</div>");
            sb.AppendLine("</header>");
            
            // Summary section with statistics
            sb.AppendLine("<div class=\"summary-section\">");
            sb.AppendLine("<h2>Summary</h2>");
            sb.AppendLine("<div class=\"stats-container\">");
            
            sb.AppendLine("<div class=\"stat-card\">");
            sb.AppendLine("<div class=\"stat-title\">Items in Database</div>");
            sb.AppendLine($"<div class=\"stat-value\">{report.DatabaseItemsCount:N0}</div>");
            sb.AppendLine("</div>");
            
            sb.AppendLine("<div class=\"stat-card\">");
            sb.AppendLine("<div class=\"stat-title\">Items in Index</div>");
            sb.AppendLine($"<div class=\"stat-value\">{report.IndexDocCount:N0}</div>");
            sb.AppendLine("</div>");
            
            sb.AppendLine("<div class=\"stat-card\">");
            sb.AppendLine("<div class=\"stat-title\">Matched Items</div>");
            sb.AppendLine($"<div class=\"stat-value\">{report.MatchedItemsCount:N0}</div>");
            sb.AppendLine("</div>");
              sb.AppendLine("<div class=\"stat-card\">");
            sb.AppendLine("<div class=\"stat-title\">Mismatched Items</div>");
            sb.AppendLine($"<div class=\"stat-value\">{report.MismatchedItems.Count:N0}</div>");
            sb.AppendLine("</div>");

            var timestampMismatchCount = report.MismatchedItems.Count(i => i.Status == "Timestamp mismatch");
            if (timestampMismatchCount > 0)
            {
                sb.AppendLine("<div class=\"stat-card\">");
                sb.AppendLine("<div class=\"stat-title\">Timestamp Mismatches</div>");
                sb.AppendLine($"<div class=\"stat-value\">{timestampMismatchCount:N0}</div>");
                sb.AppendLine("</div>");
            }
            
            sb.AppendLine("</div>"); // End of stats-container
            sb.AppendLine("</div>"); // End of summary-section
            
            // Content type distribution
            if (reportFormat != "default" && report.ContentTypeStats.Any())
            {
                sb.AppendLine("<div class=\"section\">");
                sb.AppendLine("<h2>Content Type Distribution</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<thead>");
                sb.AppendLine("<tr>");
                sb.AppendLine("<th>Content Type</th>");
                sb.AppendLine("<th>Total Items</th>");
                sb.AppendLine("<th>Mismatches</th>");
                sb.AppendLine("<th>Match Rate</th>");
                sb.AppendLine("</tr>");
                sb.AppendLine("</thead>");
                sb.AppendLine("<tbody>");
                
                foreach (var type in report.ContentTypeStats.Keys.OrderBy(k => k))
                {
                    report.MismatchesByType.TryGetValue(type, out int mismatches);
                    var total = report.ContentTypeStats[type];
                    var matchRate = ((total - mismatches) * 100.0 / total).ToString("F1");
                    sb.AppendLine("<tr>");
                    sb.AppendLine($"<td>{type}</td>");
                    sb.AppendLine($"<td>{total:N0}</td>");
                    sb.AppendLine($"<td>{mismatches}</td>");
                    sb.AppendLine($"<td>{matchRate}%</td>");
                    sb.AppendLine("</tr>");
                }
                
                sb.AppendLine("</tbody>");
                sb.AppendLine("</table>");
                sb.AppendLine("</div>");
            }
            
            // Mismatches by content type
            if (reportFormat != "default" && report.MismatchedItems.Any())
            {
                var mismatchesByType = report.MismatchedItems
                    .GroupBy(x => x.NodeType)
                    .OrderByDescending(g => g.Count());

                sb.AppendLine("<div class=\"section\">");
                sb.AppendLine("<h2>Mismatches by Content Type</h2>");
                
                foreach (var typeGroup in mismatchesByType)
                {
                    sb.AppendLine($"<h3>{typeGroup.Key}</h3>");
                    sb.AppendLine("<table>");
                    sb.AppendLine("<thead>");
                    sb.AppendLine("<tr>");                    sb.AppendLine("<th>Status</th>");
                    sb.AppendLine("<th>DB NodeId</th>");
                    sb.AppendLine("<th>DB VerID</th>");
                    sb.AppendLine("<th>DB Timestamp</th>");
                    sb.AppendLine("<th>Index NodeId</th>");
                    sb.AppendLine("<th>Index VerID</th>");
                    sb.AppendLine("<th>Index Timestamp</th>");
                    sb.AppendLine("<th>Path</th>");
                    sb.AppendLine("</tr>");
                    sb.AppendLine("</thead>");
                    sb.AppendLine("<tbody>");
                    
                    foreach (var item in typeGroup.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
                    {
                        var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                        var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                        var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                        var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                        var statusClass = item.Status == "Match" ? "tree-status-match" : "tree-status-mismatch";
                        var statusInfo = FormatStatusWithDetail(item.Status, item.Status);
                        sb.AppendLine("<tr>");
                        sb.AppendLine($"<td class=\"{statusClass}\">{statusInfo}</td>");                        sb.AppendLine($"<td>{dbNodeId}</td>");
                        sb.AppendLine($"<td>{dbVerID}</td>");
                        var dbTimestamp = item.InDatabase && item.Timestamp.HasValue ? item.Timestamp.Value.ToString("u") : "-";
                        var idxTimestamp = item.InIndex ? item.IndexTimestamp ?? "-" : "-";
                        sb.AppendLine($"<td>{dbTimestamp}</td>");
                        sb.AppendLine($"<td>{idxNodeId}</td>");
                        sb.AppendLine($"<td>{idxVerID}</td>");
                        sb.AppendLine($"<td>{idxTimestamp}</td>");
                        sb.AppendLine($"<td>{item.Path}</td>");
                        sb.AppendLine("</tr>");
                    }
                    
                    sb.AppendLine("</tbody>");
                    sb.AppendLine("</table>");
                }
                
                sb.AppendLine("</div>");
            }
            
            // Complete Item List
            if (reportFormat == "full")
            {
                sb.AppendLine("<div class=\"section\">");
                sb.AppendLine("<h2>Complete Item List</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<thead>");
                sb.AppendLine("<tr>");                sb.AppendLine("<th>Status</th>");
                sb.AppendLine("<th>DB NodeId</th>");
                sb.AppendLine("<th>DB VerID</th>");
                sb.AppendLine("<th>DB Timestamp</th>");
                sb.AppendLine("<th>Index NodeId</th>");
                sb.AppendLine("<th>Index VerID</th>");
                sb.AppendLine("<th>Index Timestamp</th>");
                sb.AppendLine("<th>Path</th>");
                sb.AppendLine("<th>Type</th>");
                sb.AppendLine("</tr>");
                sb.AppendLine("</thead>");
                sb.AppendLine("<tbody>");
                
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
                    var statusClass = item.Status == "Match" ? "tree-status-match" : "tree-status-mismatch";
                    var statusInfo = FormatStatusWithDetail(item.Status, item.Status);
                    sb.AppendLine("<tr>");
                    sb.AppendLine($"<td class=\"{statusClass}\">{statusInfo}</td>");                    sb.AppendLine($"<td>{dbNodeId}</td>");
                    sb.AppendLine($"<td>{dbVerID}</td>");
                    var dbTimestamp = item.InDatabase && item.Timestamp.HasValue ? item.Timestamp.Value.ToString("u") : "-";
                    var idxTimestamp = item.InIndex ? item.IndexTimestamp ?? "-" : "-";
                    sb.AppendLine($"<td>{dbTimestamp}</td>");
                    sb.AppendLine($"<td>{idxNodeId}</td>");
                    sb.AppendLine($"<td>{idxVerID}</td>");
                    sb.AppendLine($"<td>{idxTimestamp}</td>");
                    sb.AppendLine($"<td>{item.Path}</td>");
                    sb.AppendLine($"<td>{item.NodeType}</td>");
                    sb.AppendLine("</tr>");
                }
                
                sb.AppendLine("</tbody>");
                sb.AppendLine("</table>");
                sb.AppendLine("</div>");
            }
            
            // Tree format (hierarchical view)
            if (reportFormat == "tree")
            {
                sb.AppendLine("<div class=\"section\">");
                sb.AppendLine("<h2>Content Tree</h2>");
                sb.AppendLine("<div class=\"tree-view\">");
                
                // Group items and create branch statistics (same as markdown version)
                var allItems = new List<ContentComparer.ContentItem>();
                if (report.MatchedItems != null)
                {
                    allItems.AddRange(report.MatchedItems);
                }
                allItems.AddRange(report.MismatchedItems);
                
                var sortedItems = allItems.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();
                var branchStats = new Dictionary<string, BranchStats>();
                foreach (var item in sortedItems)
                {
                    var path = item.Path;
                    while (!string.IsNullOrEmpty(path))
                    {
                        if (!branchStats.ContainsKey(path))
                        {
                            branchStats[path] = new BranchStats();
                        }
                        var stats = branchStats[path];
                        stats.Total++;
                        if (item.Status == "Match")
                        {
                            stats.Matches++;
                        }
                        
                        var lastSlash = path.LastIndexOf('/');
                        if (lastSlash <= 0) break;
                        path = path.Substring(0, lastSlash);
                    }
                }
                
                // Root node
                var rootPath = report.RepositoryPath;
                var rootItem = sortedItems.FirstOrDefault(i => i.Path == rootPath);
                var rootStatusClass = rootItem?.Status == "Match" ? "tree-status-match" : "tree-status-mismatch";
                var rootStatusInfo = rootItem != null 
                    ? $"<span class=\"{rootStatusClass}\">{FormatStatusWithDetail(rootItem.Status, rootItem.Status)}</span>"
                    : "";
                var rootStats = branchStats.TryGetValue(rootPath, out var rs) ? rs : new BranchStats();
                var rootMatchRate = rootStats.Total > 0 ? (rootStats.Matches * 100.0 / rootStats.Total) : 100.0;
                
                sb.AppendLine($"<div class=\"tree-item tree-folder\">");
                sb.AppendLine($"  {rootPath}/ {rootStatusInfo}");
                sb.AppendLine($"  <span class=\"tree-stats\">({rootMatchRate:F1}% match, {rootStats.Total} items)</span>");
                sb.AppendLine("</div>");

                // Process remaining items
                var currentLevel = new List<bool>();
                for (int i = 0; i < sortedItems.Count; i++)
                {
                    var item = sortedItems[i];
                    if (item.Path == rootPath) continue;

                    var relativePath = item.Path;
                    if (relativePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = relativePath.Substring(rootPath.Length).TrimStart('/');
                    }
                    
                    var pathParts = relativePath.Split('/');
                    var level = pathParts.Length;
                    
                    while (currentLevel.Count > level) currentLevel.RemoveAt(currentLevel.Count - 1);
                    while (currentLevel.Count < level) currentLevel.Add(false);
                    
                    var isLast = (i == sortedItems.Count - 1) || 
                        !sortedItems[i + 1].Path.StartsWith(item.Path.Substring(0, item.Path.LastIndexOf('/')));
                    currentLevel[level - 1] = !isLast;
                    
                    var html = "<div class=\"tree-item\">";
                    for (int j = 0; j < level - 1; j++)
                    {
                        html += currentLevel[j] ? "<span class=\"tree-line\">│   </span>" : "    ";
                    }
                    html += isLast ? "<span class=\"tree-line\">└── </span>" : "<span class=\"tree-line\">├── </span>";
                    
                    var itemName = pathParts.Last();
                    var isFolder = sortedItems.Any(x => x != item && x.Path.StartsWith(item.Path + "/"));
                    var nameClass = isFolder ? "tree-folder" : "tree-file";
                    var displayName = isFolder ? itemName + "/" : itemName;
                    
                    html += $"<span class=\"{nameClass}\">{displayName}</span>";
                    
                    var statusClass = item.Status == "Match" ? "tree-status-match" : "tree-status-mismatch";
                    var statusInfo = FormatStatusWithDetail(item.Status, item.Status);
                    html += $" <span class=\"{statusClass}\">{statusInfo}</span>";

                    // Add stats for folders
                    if (isFolder)
                    {
                        var stats = branchStats.TryGetValue(item.Path, out var itemStats) ? itemStats : new BranchStats();
                        var matchRate = stats.Total > 0 ? (stats.Matches * 100.0 / stats.Total) : 100.0;
                        html += $" <span class=\"tree-stats\">({matchRate:F1}% match, {stats.Total} items)</span>";
                    }
                    
                    html += "</div>";
                    sb.AppendLine(html);
                }
                
                sb.AppendLine("</div>"); // End of tree-view
                sb.AppendLine("</div>"); // End of section
            }
            
            // Footer
            sb.AppendLine("<footer>");
            sb.AppendLine($"<p>Generated by SenseNet Index Maintenance Suite on {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
            sb.AppendLine("</footer>");

            // Close HTML document
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        // Helper method to format status with details
        private static string FormatStatusWithDetail(string status, string detailedStatus)
        {
            var icon = status == "Match" ? "✓" : "✗";
            return status == "Match" ? $"[{icon}]" : $"[{icon} {detailedStatus}]";
        }
    }
}
