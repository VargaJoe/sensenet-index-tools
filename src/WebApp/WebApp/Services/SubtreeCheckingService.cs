using Microsoft.Extensions.Logging;
using SenseNetIndexTools;

namespace WebApp.Services;

public class SubtreeCheckingService
{
    private readonly ILogger<SubtreeCheckingService> _logger;

    public SubtreeCheckingService(ILogger<SubtreeCheckingService> logger)
    {
        _logger = logger;
    }

    public bool ValidatePaths(string indexPath, string connectionString, string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(indexPath))
            return false;
        
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;
        
        if (string.IsNullOrWhiteSpace(repositoryPath))
            return false;

        // Basic path validation
        if (!Directory.Exists(indexPath))
            return false;

        return true;
    }

    public async Task<SubtreeCheckResult> CheckSubtreeAsync(SubtreeCheckOptions options)
    {
        _logger.LogInformation("Starting subtree check for path: {RepositoryPath}", options.RepositoryPath);
        _logger.LogInformation("RECEIVED CONNECTION STRING: '{ConnectionString}'", options.ConnectionString);
        _logger.LogInformation("RECEIVED INDEX PATH: '{IndexPath}'", options.IndexPath);
        
        var startTime = DateTime.Now;

        try
        {
            // For now, we'll simulate the subtree check operation
            // In the real implementation, this would call the CLI command or shared logic
            var result = await ExecuteSubtreeCheckAsync(options);
            
            var endTime = DateTime.Now;
            var duration = endTime - startTime;

            _logger.LogInformation("Subtree check completed successfully in {Duration}", duration);

            return new SubtreeCheckResult
            {
                IndexPath = options.IndexPath,
                ConnectionString = options.ConnectionString,
                RepositoryPath = options.RepositoryPath,
                StartTime = startTime,
                EndTime = endTime,
                Duration = duration,
                Success = true,
                Message = "Subtree check completed successfully",
                ReportContent = result.ReportContent,
                ReportPath = result.ReportPath,
                Options = options,
                ItemsInDatabase = result.ItemsInDatabase,
                ItemsInIndex = result.ItemsInIndex,
                MatchedItems = result.MatchedItems,
                MismatchedItems = result.MismatchedItems
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during subtree check");
            
            return new SubtreeCheckResult
            {
                IndexPath = options.IndexPath,
                ConnectionString = options.ConnectionString,
                RepositoryPath = options.RepositoryPath,
                StartTime = startTime,
                EndTime = DateTime.Now,
                Duration = DateTime.Now - startTime,
                Success = false,
                Message = $"Subtree check failed: {ex.Message}",
                Options = options
            };
        }
    }

    private async Task<SubtreeCheckExecutionResult> ExecuteSubtreeCheckAsync(SubtreeCheckOptions options)
    {
        _logger.LogInformation("Executing subtree check with real logic");
        _logger.LogInformation("Connection String: {ConnectionString}", options.ConnectionString);
        _logger.LogInformation("Index Path: {IndexPath}", options.IndexPath);
        _logger.LogInformation("Repository Path: {RepositoryPath}", options.RepositoryPath);
        
        try
        {
            // Verify this is a valid Lucene index
            if (!IndexUtilities.IsValidLuceneIndex(options.IndexPath))
            {
                throw new InvalidOperationException($"The directory does not appear to be a valid Lucene index: {options.IndexPath}");
            }

            // Execute the real subtree check using the shared library (same as CLI)
            _logger.LogInformation("Executing subtree check using ContentComparer (same as CLI)...");
            var comparer = new ContentComparer();
            var items = await Task.Run(() => 
                comparer.CompareContent(options.IndexPath, options.ConnectionString, options.RepositoryPath, 
                    options.Recursive, options.Depth));

            // Separate database and index items for statistics
            var dbItems = items.Where(item => item.InDatabase).ToList();
            var indexItems = items.Where(item => item.InIndex).ToList();

            // Calculate statistics
            var itemsInDatabase = dbItems.Count;
            var itemsInIndex = indexItems.Count;
            var matchedPaths = dbItems.Select(item => item.Path).Intersect(indexItems.Select(item => item.Path)).ToList();
            var matchedItems = matchedPaths.Count;
            var mismatchedItems = (itemsInDatabase + itemsInIndex) - (2 * matchedItems);

            // Generate report
            string reportContent;
            if (options.Format.ToLower() == "html")
            {
                reportContent = GenerateHtmlReport(options, itemsInDatabase, itemsInIndex, matchedItems, 
                    mismatchedItems, dbItems, indexItems, matchedPaths);
            }
            else
            {
                reportContent = GenerateMarkdownReport(options, itemsInDatabase, itemsInIndex, matchedItems, 
                    mismatchedItems, dbItems, indexItems, matchedPaths);
            }

            string? reportPath = null;
            if (!string.IsNullOrWhiteSpace(options.OutputPath))
            {
                reportPath = options.OutputPath;
                await File.WriteAllTextAsync(reportPath, reportContent);
            }

            return new SubtreeCheckExecutionResult
            {
                ReportContent = reportContent,
                ReportPath = reportPath,
                ItemsInDatabase = itemsInDatabase,
                ItemsInIndex = itemsInIndex,
                MatchedItems = matchedItems,
                MismatchedItems = mismatchedItems
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during real subtree check execution");
            throw;
        }
    }

    private string GenerateHtmlReport(SubtreeCheckOptions options, int itemsInDatabase, int itemsInIndex, 
        int matchedItems, int mismatchedItems, List<ContentItem> dbItems, List<ContentItem> indexItems, List<string> matchedPaths)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var dbOnlyItems = dbItems.Where(item => !matchedPaths.Contains(item.Path)).ToList();
        var indexOnlyItems = indexItems.Where(item => !matchedPaths.Contains(item.Path)).ToList();
        
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>Subtree Check Report</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 40px; background: #fafbfc; }}
        .header {{ border-bottom: 2px solid #eee; padding-bottom: 20px; margin-bottom: 30px; background: white; padding: 30px; border-radius: 8px; }}
        .summary {{ background: #f8f9fa; padding: 20px; border-radius: 8px; margin-bottom: 30px; border-left: 4px solid #0366d6; }}
        .stats {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 20px; margin: 20px 0; }}
        .stat-card {{ background: white; padding: 15px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .stat-title {{ color: #666; font-size: 14px; margin-bottom: 5px; }}
        .stat-value {{ font-size: 24px; font-weight: bold; color: #333; }}
        .success {{ color: #28a745; }}
        .error {{ color: #dc3545; }}
        table {{ width: 100%; border-collapse: collapse; margin-top: 20px; background: white; border-radius: 8px; overflow: hidden; }}
        th, td {{ padding: 12px; text-align: left; border-bottom: 1px solid #ddd; }}
        th {{ background: #f8f9fa; font-weight: 600; color: #586069; }}
        tr:hover {{ background-color: #f6f8fa; }}
        .section {{ background: white; padding: 30px; margin-bottom: 20px; border-radius: 8px; }}
        h2 {{ margin-top: 0; color: #24292e; }}
    </style>
</head>
<body>
    <div class=""header"">
        <h1>Subtree Check Report</h1>
        <p><strong>Repository Path:</strong> {options.RepositoryPath}</p>
        <p><strong>Index Path:</strong> {options.IndexPath}</p>
        <p><strong>Generated:</strong> {timestamp}</p>
        <p><strong>Report Format:</strong> {options.ReportFormat}</p>
        <p><strong>Recursive:</strong> {(options.Recursive ? "Yes" : "No")}</p>
        {(options.Depth > 0 ? $"<p><strong>Depth Limit:</strong> {options.Depth}</p>" : "")}
    </div>
    
    <div class=""summary"">
        <h2>Summary</h2>
        <div class=""stats"">
            <div class=""stat-card"">
                <div class=""stat-title"">Items in Database</div>
                <div class=""stat-value"">{itemsInDatabase}</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-title"">Items in Index</div>
                <div class=""stat-value"">{itemsInIndex}</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-title"">Matched Items</div>
                <div class=""stat-value success"">{matchedItems}</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-title"">Mismatched Items</div>
                <div class=""stat-value error"">{mismatchedItems}</div>
            </div>
        </div>
        <p><strong>Sync Status:</strong> {(mismatchedItems == 0 ? "<span class='success'>✓ In Sync</span>" : "<span class='error'>⚠ Out of Sync</span>")}</p>
    </div>

    {(dbOnlyItems.Any() ? $@"
    <div class=""section"">
        <h2>Items Only in Database ({dbOnlyItems.Count})</h2>
        <table>
            <thead>
                <tr><th>Path</th><th>Node Type</th><th>ID</th></tr>
            </thead>
            <tbody>
                {string.Join("", dbOnlyItems.Take(100).Select(item => $"<tr><td>{System.Net.WebUtility.HtmlEncode(item.Path)}</td><td>{System.Net.WebUtility.HtmlEncode(item.NodeType ?? "")}</td><td>{item.NodeId}</td></tr>"))}
                {(dbOnlyItems.Count > 100 ? $"<tr><td colspan='3'><em>... and {dbOnlyItems.Count - 100} more items</em></td></tr>" : "")}
            </tbody>
        </table>
    </div>" : "")}

    {(indexOnlyItems.Any() ? $@"
    <div class=""section"">
        <h2>Items Only in Index ({indexOnlyItems.Count})</h2>
        <table>
            <thead>
                <tr><th>Path</th><th>Node Type</th><th>ID</th></tr>
            </thead>
            <tbody>
                {string.Join("", indexOnlyItems.Take(100).Select(item => $"<tr><td>{System.Net.WebUtility.HtmlEncode(item.Path)}</td><td>{System.Net.WebUtility.HtmlEncode(item.NodeType ?? "")}</td><td>{item.NodeId}</td></tr>"))}
                {(indexOnlyItems.Count > 100 ? $"<tr><td colspan='3'><em>... and {indexOnlyItems.Count - 100} more items</em></td></tr>" : "")}
            </tbody>
        </table>
    </div>" : "")}
    
    <footer style='margin-top:2em;font-size:13px;color:#888;text-align:center;'>Generated by SenseNet Index Tools - {timestamp}</footer>
</body>
</html>";
    }

    private string GenerateMarkdownReport(SubtreeCheckOptions options, int itemsInDatabase, int itemsInIndex, 
        int matchedItems, int mismatchedItems, List<ContentItem> dbItems, List<ContentItem> indexItems, List<string> matchedPaths)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var dbOnlyItems = dbItems.Where(item => !matchedPaths.Contains(item.Path)).ToList();
        var indexOnlyItems = indexItems.Where(item => !matchedPaths.Contains(item.Path)).ToList();
        
        var report = $@"# Subtree Check Report

## Summary
- **Repository Path**: {options.RepositoryPath}
- **Index Path**: {options.IndexPath}
- **Generated**: {timestamp}
- **Report Format**: {options.ReportFormat}
- **Recursive**: {(options.Recursive ? "Yes" : "No")}
{(options.Depth > 0 ? $"- **Depth Limit**: {options.Depth}" : "")}

## Statistics
- **Items in Database**: {itemsInDatabase}
- **Items in Index**: {itemsInIndex}
- **Matched Items**: {matchedItems}
- **Mismatched Items**: {mismatchedItems}
- **Sync Status**: {(mismatchedItems == 0 ? "✓ In Sync" : "⚠ Out of Sync")}

";

        if (dbOnlyItems.Any())
        {
            report += $@"## Items Only in Database ({dbOnlyItems.Count})

| Path | Node Type | ID |
|------|-----------|----| 
";
            foreach (var item in dbOnlyItems.Take(100))
            {
                report += $"| {item.Path} | {item.NodeType ?? ""} | {item.NodeId} |\n";
            }
            if (dbOnlyItems.Count > 100)
            {
                report += $"| ... and {dbOnlyItems.Count - 100} more items | | |\n";
            }
            report += "\n";
        }

        if (indexOnlyItems.Any())
        {
            report += $@"## Items Only in Index ({indexOnlyItems.Count})

| Path | Node Type | ID |
|------|-----------|----| 
";
            foreach (var item in indexOnlyItems.Take(100))
            {
                report += $"| {item.Path} | {item.NodeType ?? ""} | {item.NodeId} |\n";
            }
            if (indexOnlyItems.Count > 100)
            {
                report += $"| ... and {indexOnlyItems.Count - 100} more items | | |\n";
            }
            report += "\n";
        }

        report += $"\n---\n*Generated by SenseNet Index Tools - {timestamp}*\n";
        return report;
    }
}

// Data models for Subtree Checking
public class SubtreeCheckOptions
{
    public string IndexPath { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string RepositoryPath { get; set; } = "/Root";
    public bool Recursive { get; set; } = true;
    public int Depth { get; set; } = 0; // 0 = no limit
    public string ReportFormat { get; set; } = "summary"; // summary, detailed, full, tree
    public string Format { get; set; } = "md"; // md, html
    public string? OutputPath { get; set; }
}

public class SubtreeCheckResult
{
    public string IndexPath { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string RepositoryPath { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ReportContent { get; set; }
    public string? ReportPath { get; set; }
    public SubtreeCheckOptions? Options { get; set; }
    
    // Statistics
    public int ItemsInDatabase { get; set; }
    public int ItemsInIndex { get; set; }
    public int MatchedItems { get; set; }
    public int MismatchedItems { get; set; }
}

internal class SubtreeCheckExecutionResult
{
    public string? ReportContent { get; set; }
    public string? ReportPath { get; set; }
    public int ItemsInDatabase { get; set; }
    public int ItemsInIndex { get; set; }
    public int MatchedItems { get; set; }
    public int MismatchedItems { get; set; }
}
