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
                        sb.AppendLine("| Status | DB NodeId | DB VerID | Index NodeId | Index VerID | Index VerTimestamp | Path |");
                        sb.AppendLine("|---------|-----------|----------|--------------|-------------|-------------------|------|");
                        
                        foreach (var item in typeGroup.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
                        {
                            var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                            var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                            var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                            var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                            var idxVerTimestamp = item.InIndex ? (item.IndexVersionTimestamp ?? "-") : "-";
                            sb.AppendLine($"| {item.Status} | {dbNodeId} | {dbVerID} | {idxNodeId} | {idxVerID} | {idxVerTimestamp} | {item.Path} |");
                        }
                        sb.AppendLine();
                    }
                }
                
                if (reportFormat == "full")
                {
                    // Full Item List (like in compare function)
                    sb.AppendLine("## Complete Item List");
                    sb.AppendLine("| Status | DB NodeId | DB VerID | Index NodeId | Index VerID | Index VerTimestamp | Path | Type |");
                    sb.AppendLine("|---------|-----------|----------|--------------|-------------|-------------------|------|------|");

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
                        var idxVerTimestamp = item.InIndex ? (item.IndexVersionTimestamp ?? "-") : "-";
                        sb.AppendLine($"| {item.Status} | {dbNodeId} | {dbVerID} | {idxNodeId} | {idxVerID} | {idxVerTimestamp} | {item.Path} | {item.NodeType} |");
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static string GenerateHtmlReport(CheckReport report, string reportFormat)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("    <title>Subtree Index Check Report</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Arial, sans-serif; margin: 40px; line-height: 1.6; color: #333; }");
            sb.AppendLine("        h1 { color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px; }");
            sb.AppendLine("        h2 { color: #34495e; border-bottom: 2px solid #ecf0f1; padding-bottom: 8px; margin-top: 30px; }");
            sb.AppendLine("        h3 { color: #7f8c8d; margin-top: 25px; }");
            sb.AppendLine("        .info-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 15px; margin: 20px 0; }");
            sb.AppendLine("        .info-item { background: #f8f9fa; padding: 15px; border-radius: 8px; border-left: 4px solid #3498db; }");
            sb.AppendLine("        .info-label { font-weight: bold; color: #2c3e50; }");
            sb.AppendLine("        .summary-stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 20px; margin: 20px 0; }");
            sb.AppendLine("        .stat-card { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; border-radius: 12px; text-align: center; box-shadow: 0 4px 6px rgba(0,0,0,0.1); }");
            sb.AppendLine("        .stat-number { font-size: 2em; font-weight: bold; margin-bottom: 5px; }");
            sb.AppendLine("        .stat-label { font-size: 0.9em; opacity: 0.9; }");
            sb.AppendLine("        table { width: 100%; border-collapse: collapse; margin: 20px 0; background: white; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
            sb.AppendLine("        th, td { padding: 12px; text-align: left; border-bottom: 1px solid #e9ecef; }");
            sb.AppendLine("        th { background: #f8f9fa; font-weight: 600; color: #495057; position: sticky; top: 0; }");
            sb.AppendLine("        tr:hover { background-color: #f8f9fa; }");
            sb.AppendLine("        .status-match { color: #28a745; font-weight: bold; }");
            sb.AppendLine("        .status-mismatch { color: #dc3545; font-weight: bold; }");
            sb.AppendLine("        .status-missing { color: #fd7e14; font-weight: bold; }");
            sb.AppendLine("        .match-rate-high { color: #28a745; font-weight: bold; }");
            sb.AppendLine("        .match-rate-medium { color: #ffc107; font-weight: bold; }");
            sb.AppendLine("        .match-rate-low { color: #dc3545; font-weight: bold; }");
            sb.AppendLine("        .path-cell { font-family: 'Courier New', monospace; font-size: 0.9em; }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            
            sb.AppendLine("    <h1>🔍 Subtree Index Check Report</h1>");
            
            sb.AppendLine("    <h2>📋 Check Information</h2>");
            sb.AppendLine("    <div class=\"info-grid\">");
            sb.AppendLine($"        <div class=\"info-item\"><span class=\"info-label\">Repository Path:</span><br>{report.RepositoryPath}</div>");
            sb.AppendLine($"        <div class=\"info-item\"><span class=\"info-label\">Recursive:</span><br>{report.Recursive}</div>");
            sb.AppendLine($"        <div class=\"info-item\"><span class=\"info-label\">Start Time:</span><br>{report.StartTime:yyyy-MM-dd HH:mm:ss}</div>");
            sb.AppendLine($"        <div class=\"info-item\"><span class=\"info-label\">End Time:</span><br>{report.EndTime:yyyy-MM-dd HH:mm:ss}</div>");
            sb.AppendLine($"        <div class=\"info-item\"><span class=\"info-label\">Duration:</span><br>{(report.EndTime - report.StartTime).TotalSeconds:F2} seconds</div>");
            sb.AppendLine("    </div>");
            
            sb.AppendLine("    <h2>📊 Summary</h2>");
            sb.AppendLine("    <div class=\"summary-stats\">");
            sb.AppendLine($"        <div class=\"stat-card\"><div class=\"stat-number\">{report.DatabaseItemsCount}</div><div class=\"stat-label\">Items in Database</div></div>");
            sb.AppendLine($"        <div class=\"stat-card\"><div class=\"stat-number\">{report.IndexDocCount}</div><div class=\"stat-label\">Items in Index</div></div>");
            sb.AppendLine($"        <div class=\"stat-card\"><div class=\"stat-number\">{report.MatchedItemsCount}</div><div class=\"stat-label\">Matched Items</div></div>");
            sb.AppendLine($"        <div class=\"stat-card\"><div class=\"stat-number\">{report.MismatchedItems.Count}</div><div class=\"stat-label\">Mismatched Items</div></div>");
            sb.AppendLine("    </div>");

            if (reportFormat != "default")
            {
                // Content Type Statistics
                sb.AppendLine("    <h2>📈 Content Type Statistics</h2>");
                sb.AppendLine("    <table>");
                sb.AppendLine("        <thead>");
                sb.AppendLine("            <tr><th>Type</th><th>Total Items</th><th>Mismatches</th><th>Match Rate</th></tr>");
                sb.AppendLine("        </thead>");
                sb.AppendLine("        <tbody>");
                foreach (var type in report.ContentTypeStats.Keys.OrderBy(k => k))
                {
                    report.MismatchesByType.TryGetValue(type, out int mismatches);
                    var total = report.ContentTypeStats[type];
                    var matchRate = (total - mismatches) * 100.0 / total;
                    var matchRateClass = matchRate >= 95 ? "match-rate-high" : matchRate >= 80 ? "match-rate-medium" : "match-rate-low";
                    sb.AppendLine($"            <tr><td>{type}</td><td>{total}</td><td>{mismatches}</td><td class=\"{matchRateClass}\">{matchRate:F1}%</td></tr>");
                }
                sb.AppendLine("        </tbody>");
                sb.AppendLine("    </table>");

                if (report.MismatchedItems.Any())
                {
                    var mismatchesByType = report.MismatchedItems
                        .GroupBy(x => x.NodeType)
                        .OrderByDescending(g => g.Count());
                    sb.AppendLine("    <h2>⚠️ Mismatches by Content Type</h2>");
                    foreach (var typeGroup in mismatchesByType)
                    {
                        sb.AppendLine($"    <h3>{typeGroup.Key} ({typeGroup.Count()} items)</h3>");
                        sb.AppendLine("    <table>");
                        sb.AppendLine("        <thead>");
                        sb.AppendLine("            <tr><th>Status</th><th>DB NodeId</th><th>DB VerID</th><th>Index NodeId</th><th>Index VerID</th><th>Index VerTimestamp</th><th>Path</th></tr>");
                        sb.AppendLine("        </thead>");
                        sb.AppendLine("        <tbody>");
                        
                        foreach (var item in typeGroup.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
                        {
                            var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                            var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                            var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                            var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                            var idxVerTimestamp = item.InIndex ? (item.IndexVersionTimestamp ?? "-") : "-";
                            var statusClass = item.Status == "Match" ? "status-match" : "status-mismatch";
                            sb.AppendLine($"            <tr><td class=\"{statusClass}\">{item.Status}</td><td>{dbNodeId}</td><td>{dbVerID}</td><td>{idxNodeId}</td><td>{idxVerID}</td><td>{idxVerTimestamp}</td><td class=\"path-cell\">{item.Path}</td></tr>");
                        }
                        sb.AppendLine("        </tbody>");
                        sb.AppendLine("    </table>");
                    }
                }
                
                if (reportFormat == "full")
                {
                    sb.AppendLine("    <h2>📝 Complete Item List</h2>");
                    sb.AppendLine("    <table>");
                    sb.AppendLine("        <thead>");
                    sb.AppendLine("            <tr><th>Status</th><th>DB NodeId</th><th>DB VerID</th><th>Index NodeId</th><th>Index VerID</th><th>Index VerTimestamp</th><th>Path</th><th>Type</th></tr>");
                    sb.AppendLine("        </thead>");
                    sb.AppendLine("        <tbody>");

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
                        var idxVerTimestamp = item.InIndex ? (item.IndexVersionTimestamp ?? "-") : "-";
                        var statusClass = item.Status == "Match" ? "status-match" : "status-mismatch";
                        sb.AppendLine($"            <tr><td class=\"{statusClass}\">{item.Status}</td><td>{dbNodeId}</td><td>{dbVerID}</td><td>{idxNodeId}</td><td>{idxVerID}</td><td>{idxVerTimestamp}</td><td class=\"path-cell\">{item.Path}</td><td>{item.NodeType}</td></tr>");
                    }
                    sb.AppendLine("        </tbody>");
                    sb.AppendLine("    </table>");
                }
            }

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            
            return sb.ToString();
        }
    }
}
