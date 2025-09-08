using Microsoft.Extensions.Logging;
using SenseNetIndexTools;
using WebApp.Models;

namespace WebApp.Services;

public class SubtreeCheckingService
{
    private readonly ILogger<SubtreeCheckingService> _logger;
    private readonly ReportStorageService _reportStorage;

    public SubtreeCheckingService(ILogger<SubtreeCheckingService> logger, ReportStorageService reportStorage)
    {
        _logger = logger;
        _reportStorage = reportStorage;
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

            var subtreeResult = new SubtreeCheckResult
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

            // Save report to storage
            await SaveReportAsync(subtreeResult, options);

            return subtreeResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during subtree check");
            
            var failedResult = new SubtreeCheckResult
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

            // Save failed report too
            await SaveReportAsync(failedResult, options);

            return failedResult;
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

            // Use the shared SubtreeIndexChecker for proper detailed report generation (same as CLI)
            _logger.LogInformation("Executing subtree check using SubtreeIndexChecker (same as CLI)...");
            
            var checker = new SubtreeIndexChecker();
            var reportContent = await Task.Run(() => 
                // Generate report using the shared logic from Core library
                checker.GenerateSubtreeReport(options.IndexPath, options.ConnectionString, options.RepositoryPath, 
                    options.Recursive, options.Depth, options.ReportFormat, options.Format));

            // Get statistics by running a quick comparison for display
            var comparer = new ContentComparer();
            var items = await Task.Run(() => 
                comparer.CompareContent(options.IndexPath, options.ConnectionString, options.RepositoryPath, 
                    options.Recursive, options.Depth));

            var itemsInDatabase = items.Count(item => item.InDatabase);
            var itemsInIndex = items.Count(item => item.InIndex);
            var matchedItems = items.Count(item => item.Status == "Match");
            var mismatchedItems = items.Count(item => item.Status != "Match");

            // Save report to file if requested
            string? reportPath = null;
            if (!string.IsNullOrWhiteSpace(options.OutputPath))
            {
                reportPath = options.OutputPath;
                var directory = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
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

    private async Task SaveReportAsync(SubtreeCheckResult result, SubtreeCheckOptions options)
    {
        if (string.IsNullOrWhiteSpace(result.ReportContent))
            return;

        try
        {
            var storedReport = new StoredReport
            {
                Name = GenerateSubtreeReportName(result.IndexPath, result.RepositoryPath, options),
                Description = $"Subtree check for {Path.GetFileName(result.RepositoryPath)} vs {Path.GetFileName(result.IndexPath)} - {options.ReportFormat} format",
                ReportType = "subtree-check",
                CreatedAt = result.StartTime,
                Format = options.Format,
                Content = result.ReportContent,
                Duration = result.Duration,
                Success = result.Success,
                ErrorMessage = result.Success ? null : result.Message,
                Statistics = new Dictionary<string, object>
                {
                    ["DbItems"] = result.ItemsInDatabase,
                    ["IndexItems"] = result.ItemsInIndex,
                    ["Matches"] = result.MatchedItems,
                    ["Mismatches"] = result.MismatchedItems
                },
                Parameters = new Dictionary<string, object>
                {
                    ["IndexPath"] = result.IndexPath,
                    ["ConnectionString"] = "***", // Hide sensitive data
                    ["RepositoryPath"] = result.RepositoryPath,
                    ["ReportFormat"] = options.ReportFormat,
                    ["Recursive"] = options.Recursive,
                    ["Depth"] = options.Depth
                }
            };

            await _reportStorage.SaveReportAsync(storedReport);
            _logger.LogInformation("Saved subtree check report to storage: {ReportId}", storedReport.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save subtree check report to storage");
        }
    }

    private string GenerateSubtreeReportName(string indexPath, string repositoryPath, SubtreeCheckOptions options)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var reportType = options.ReportFormat switch
        {
            "summary" => "Summary",
            "detailed" => "Detailed", 
            "full" => "Full",
            _ => "Check"
        };
        
        // Use configuration name when available for better readability
        if (!string.IsNullOrEmpty(options.ConfigurationName))
        {
            return $"Subtree ({reportType}) - {options.ConfigurationName} - {timestamp}";
        }
        
        // Fallback to path-based naming when no configuration is used
        var indexName = Path.GetFileName(indexPath.TrimEnd('\\', '/'));
        var repoName = Path.GetFileName(repositoryPath.TrimEnd('\\', '/'));
        return $"Subtree ({reportType}) - {indexName} vs {repoName} - {timestamp}";
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
    
    // Configuration tracking for better report naming
    public string? ConfigurationId { get; set; }
    public string? ConfigurationName { get; set; }
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
