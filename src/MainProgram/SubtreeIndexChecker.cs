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
            public List<ContentItem> MismatchedItems { get; set; } = new List<ContentItem>();
            public List<ContentItem> MatchedItems { get; set; } = new List<ContentItem>();
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
                description: "Level of detail for the report: 'summary', 'detailed', or 'full' (default: 'summary')",
                getDefaultValue: () => "summary");
            reportFormatOption.FromAmong("summary", "detailed", "full");

            var formatOption = new Option<string>(
                name: "--format",
                description: "Output format: 'md' (Markdown) or 'html' (HTML format suitable for web viewing)",
                getDefaultValue: () => "md");
            formatOption.FromAmong("md", "html");

            command.AddOption(indexPathOption);
            command.AddOption(connectionStringOption);
            command.AddOption(repositoryPathOption);
            command.AddOption(outputOption);
            command.AddOption(recursiveOption);
            command.AddOption(depthOption);
            command.AddOption(reportFormatOption);
            command.AddOption(formatOption);

            command.SetHandler((string indexPath, string connectionString, string repositoryPath, 
                string? output, bool recursive, int depth, string reportFormat, string format) =>
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
                    GenerateReport(report, output, reportFormat, format);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error checking subtree: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    Environment.Exit(1);
                }
            }, indexPathOption, connectionStringOption, repositoryPathOption, outputOption, recursiveOption, depthOption, reportFormatOption, formatOption);

            return command;
        }

        private static void ProcessResults(List<ContentItem> results, CheckReport report, bool detailed)
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

        private static void GenerateReport(CheckReport report, string? outputPath, string reportFormat, string format)
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
                IOFile.WriteAllText(outputPath, reportContent);
                Console.WriteLine($"\nDetailed report saved to: {outputPath}");
            }
        }

        private static string GenerateMarkdownReport(CheckReport report, string reportFormat)
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
                        sb.AppendLine("| Status | DB NodeId | DB VerID | DB Timestamp | DB VerTimestamp | Index NodeId | Index VerID | Index Timestamp | Index VerTimestamp | Path |");
                        sb.AppendLine("|---------|-----------|----------|--------------|-----------------|--------------|-------------|-----------------|-------------------|------|");
                        
                        foreach (var item in typeGroup.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
                        {
                            var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                            var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                            var dbTimestamp = item.InDatabase ? item.TimestampNumeric.ToString() : "-";
                            var dbVerTimestamp = item.InDatabase ? item.VersionTimestampNumeric.ToString() : "-";
                            var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                            var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                            var idxTimestamp = item.InIndex ? item.IndexTimestamp : "-";
                            var idxVerTimestamp = item.InIndex ? item.IndexVersionTimestamp : "-";
                            sb.AppendLine($"| {item.Status} | {dbNodeId} | {dbVerID} | {dbTimestamp} | {dbVerTimestamp} | {idxNodeId} | {idxVerID} | {idxTimestamp} | {idxVerTimestamp} | {item.Path} |");
                        }
                        sb.AppendLine();
                    }
                }
                
                if (reportFormat == "full")
                {
                    // Full Item List (like in compare function)
                    sb.AppendLine("## Complete Item List");
                    sb.AppendLine("| Status | DB NodeId | DB VerID | DB Timestamp | DB VerTimestamp | Index NodeId | Index VerID | Index Timestamp | Index VerTimestamp | Path | Type |");
                    sb.AppendLine("|---------|-----------|----------|--------------|-----------------|--------------|-------------|-----------------|-------------------|------|------|");

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
                        var dbTimestamp = item.InDatabase ? item.TimestampNumeric.ToString() : "-";
                        var dbVerTimestamp = item.InDatabase ? item.VersionTimestampNumeric.ToString() : "-";
                        var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                        var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                        var idxTimestamp = item.InIndex ? item.IndexTimestamp : "-";
                        var idxVerTimestamp = item.InIndex ? item.IndexVersionTimestamp : "-";
                        sb.AppendLine($"| {item.Status} | {dbNodeId} | {dbVerID} | {dbTimestamp} | {dbVerTimestamp} | {idxNodeId} | {idxVerID} | {idxTimestamp} | {idxVerTimestamp} | {item.Path} | {item.NodeType} |");
                    }
                    sb.AppendLine();
                }
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
                    var matchRate = (total - mismatches) * 100.0 / total;
                    sb.AppendLine("<tr>");
                    sb.AppendLine($"<td>{type}</td>");
                    sb.AppendLine($"<td>{total:N0}</td>");
                    sb.AppendLine($"<td>{mismatches:N0}</td>");
                    sb.AppendLine($"<td>{matchRate:F1}%</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</tbody>");
                sb.AppendLine("</table>");
                sb.AppendLine("</div>");

                if (report.MismatchedItems.Any())
                {
                    var mismatchesByType = report.MismatchedItems
                        .GroupBy(x => x.NodeType)
                        .OrderByDescending(g => g.Count());
                    sb.AppendLine("<div class=\"section\">");
                    sb.AppendLine("<h2>Mismatches by Content Type</h2>");
                    foreach (var typeGroup in mismatchesByType)
                    {
                        sb.AppendLine($"<h3>{typeGroup.Key} ({typeGroup.Count()} items)</h3>");
                        sb.AppendLine("<table>");
                        sb.AppendLine("<thead>");
                        sb.AppendLine("<tr><th>Status</th><th>DB NodeId</th><th>DB VerID</th><th>DB Timestamp</th><th>DB VerTimestamp</th><th>Index NodeId</th><th>Index VerID</th><th>Index Timestamp</th><th>Index VerTimestamp</th><th>Path</th></tr>");
                        sb.AppendLine("</thead>");
                        sb.AppendLine("<tbody>");
                        
                        foreach (var item in typeGroup.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
                        {
                            var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                            var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                            var dbTimestamp = item.InDatabase ? item.TimestampNumeric.ToString() : "-";
                            var dbVerTimestamp = item.InDatabase ? item.VersionTimestampNumeric.ToString() : "-";
                            var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                            var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                            var idxTimestamp = item.InIndex ? item.IndexTimestamp : "-";
                            var idxVerTimestamp = item.InIndex ? item.IndexVersionTimestamp : "-";
                            sb.AppendLine($"<tr><td>{item.Status}</td><td>{dbNodeId}</td><td>{dbVerID}</td><td>{dbTimestamp}</td><td>{dbVerTimestamp}</td><td>{idxNodeId}</td><td>{idxVerID}</td><td>{idxTimestamp}</td><td>{idxVerTimestamp}</td><td>{item.Path}</td></tr>");
                        }
                        sb.AppendLine("</tbody>");
                        sb.AppendLine("</table>");
                    }
                    sb.AppendLine("</div>");
                }
                
                if (reportFormat == "full")
                {
                    sb.AppendLine("<div class=\"section\">");
                    sb.AppendLine("<h2>Complete Item List</h2>");
                    sb.AppendLine("<table>");
                    sb.AppendLine("<thead>");
                    sb.AppendLine("<tr><th>Status</th><th>DB NodeId</th><th>DB VerID</th><th>DB Timestamp</th><th>DB VerTimestamp</th><th>Index NodeId</th><th>Index VerID</th><th>Index Timestamp</th><th>Index VerTimestamp</th><th>Path</th><th>Type</th></tr>");
                    sb.AppendLine("</thead>");
                    sb.AppendLine("<tbody>");

                    var allItems = report.MismatchedItems.ToList();
                    if (report.MatchedItems != null)
                    {
                        allItems.AddRange(report.MatchedItems);
                    }

                    foreach (var item in allItems.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
                    {
                        var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                        var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                        var dbTimestamp = item.InDatabase ? item.TimestampNumeric.ToString() : "-";
                        var dbVerTimestamp = item.InDatabase ? item.VersionTimestampNumeric.ToString() : "-";
                        var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                        var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                        var idxTimestamp = item.InIndex ? item.IndexTimestamp : "-";
                        var idxVerTimestamp = item.InIndex ? item.IndexVersionTimestamp : "-";
                        sb.AppendLine($"<tr><td>{item.Status}</td><td>{dbNodeId}</td><td>{dbVerID}</td><td>{dbTimestamp}</td><td>{dbVerTimestamp}</td><td>{idxNodeId}</td><td>{idxVerID}</td><td>{idxTimestamp}</td><td>{idxVerTimestamp}</td><td>{item.Path}</td><td>{item.NodeType}</td></tr>");
                    }
                    sb.AppendLine("</tbody>");
                    sb.AppendLine("</table>");
                    sb.AppendLine("</div>");
                }
            }

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            
            return sb.ToString();
        }
    }
}
