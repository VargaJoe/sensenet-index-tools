using System.Text.Json;
using WebApp.Models;

namespace WebApp.Services;

public class ReportStorageService
{
    private readonly ILogger<ReportStorageService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly string _dataDirectory;
    private readonly string _reportsFilePath;
    private readonly string _reportsDataDirectory;

    public ReportStorageService(ILogger<ReportStorageService> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
        _dataDirectory = Path.Combine(_environment.ContentRootPath, "Data");
        _reportsFilePath = Path.Combine(_dataDirectory, "reports.json");
        _reportsDataDirectory = Path.Combine(_dataDirectory, "Reports");
        
        EnsureDirectoriesExist();
    }

    private void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(_dataDirectory);
        Directory.CreateDirectory(_reportsDataDirectory);
    }

    public async Task<string> SaveReportAsync(StoredReport report)
    {
        try
        {
            // Generate auto name if not provided
            if (string.IsNullOrWhiteSpace(report.Name))
            {
                report.Name = GenerateReportName(report);
            }

            // Save report content to file if it's large
            if (report.Content.Length > 50000) // 50KB threshold
            {
                var fileName = $"{report.Id}.{report.Format}";
                var filePath = Path.Combine(_reportsDataDirectory, fileName);
                await File.WriteAllTextAsync(filePath, report.Content);
                
                report.FilePath = filePath;
                report.FileSizeBytes = new FileInfo(filePath).Length;
                
                // Clear content from memory to save space
                report.Content = $"[Content stored in file: {fileName}]";
                
                _logger.LogInformation("Saved large report content to file: {FilePath}", filePath);
            }
            else
            {
                report.FileSizeBytes = System.Text.Encoding.UTF8.GetByteCount(report.Content);
            }

            // Load existing reports
            var reports = await LoadReportsAsync();
            
            // Add or update report
            var existingIndex = reports.FindIndex(r => r.Id == report.Id);
            if (existingIndex >= 0)
            {
                reports[existingIndex] = report;
                _logger.LogInformation("Updated existing report: {ReportId}", report.Id);
            }
            else
            {
                reports.Add(report);
                _logger.LogInformation("Added new report: {ReportId}", report.Id);
            }

            // Save reports index
            await SaveReportsAsync(reports);
            
            return report.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save report: {ReportId}", report.Id);
            throw;
        }
    }

    public async Task<List<StoredReport>> GetReportsAsync(ReportSearchFilter? filter = null)
    {
        try
        {
            var reports = await LoadReportsAsync();
            
            if (filter != null)
            {
                reports = ApplyFilter(reports, filter);
            }
            
            return reports.OrderByDescending(r => r.CreatedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load reports");
            return new List<StoredReport>();
        }
    }

    public async Task<StoredReport?> GetReportAsync(string reportId)
    {
        try
        {
            var reports = await LoadReportsAsync();
            var report = reports.FirstOrDefault(r => r.Id == reportId);
            
            if (report != null)
            {
                // Update view statistics
                report.LastViewedAt = DateTime.Now;
                report.ViewCount++;
                await SaveReportsAsync(reports);
                
                // Load content from file if needed
                if (report.FilePath != null && File.Exists(report.FilePath))
                {
                    report.Content = await File.ReadAllTextAsync(report.FilePath);
                }
                
                _logger.LogInformation("Retrieved report: {ReportId}", reportId);
            }
            
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get report: {ReportId}", reportId);
            return null;
        }
    }

    public async Task<bool> DeleteReportAsync(string reportId)
    {
        try
        {
            var reports = await LoadReportsAsync();
            var report = reports.FirstOrDefault(r => r.Id == reportId);
            
            if (report == null)
            {
                return false;
            }

            // Delete file if exists
            if (report.FilePath != null && File.Exists(report.FilePath))
            {
                File.Delete(report.FilePath);
                _logger.LogInformation("Deleted report file: {FilePath}", report.FilePath);
            }

            // Remove from index
            reports.RemoveAll(r => r.Id == reportId);
            await SaveReportsAsync(reports);
            
            _logger.LogInformation("Deleted report: {ReportId}", reportId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete report: {ReportId}", reportId);
            return false;
        }
    }

    public async Task<bool> ToggleFavoriteAsync(string reportId)
    {
        try
        {
            var reports = await LoadReportsAsync();
            var report = reports.FirstOrDefault(r => r.Id == reportId);
            
            if (report == null)
            {
                return false;
            }

            report.IsFavorite = !report.IsFavorite;
            await SaveReportsAsync(reports);
            
            _logger.LogInformation("Toggled favorite for report: {ReportId} -> {IsFavorite}", reportId, report.IsFavorite);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle favorite for report: {ReportId}", reportId);
            return false;
        }
    }

    public async Task<Dictionary<string, int>> GetReportStatisticsAsync()
    {
        try
        {
            var reports = await LoadReportsAsync();
            
            return new Dictionary<string, int>
            {
                ["Total"] = reports.Count,
                ["ValidationReports"] = reports.Count(r => r.ReportType == "validation"),
                ["SubtreeReports"] = reports.Count(r => r.ReportType == "subtree-check"),
                ["ContentListingReports"] = reports.Count(r => r.ReportType == "content-listing"),
                ["Favorites"] = reports.Count(r => r.IsFavorite),
                ["LastWeek"] = reports.Count(r => r.CreatedAt >= DateTime.Now.AddDays(-7)),
                ["LastMonth"] = reports.Count(r => r.CreatedAt >= DateTime.Now.AddDays(-30))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get report statistics");
            return new Dictionary<string, int>();
        }
    }

    private async Task<List<StoredReport>> LoadReportsAsync()
    {
        if (!File.Exists(_reportsFilePath))
        {
            return new List<StoredReport>();
        }

        var json = await File.ReadAllTextAsync(_reportsFilePath);
        return JsonSerializer.Deserialize<List<StoredReport>>(json) ?? new List<StoredReport>();
    }

    private async Task SaveReportsAsync(List<StoredReport> reports)
    {
        var json = JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_reportsFilePath, json);
    }

    private List<StoredReport> ApplyFilter(List<StoredReport> reports, ReportSearchFilter filter)
    {
        var query = reports.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            query = query.Where(r => r.Name.Contains(filter.SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                                   (r.Description != null && r.Description.Contains(filter.SearchTerm, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(filter.ReportType))
        {
            query = query.Where(r => r.ReportType == filter.ReportType);
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(r => r.CreatedAt >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(r => r.CreatedAt <= filter.ToDate.Value);
        }

        if (filter.Success.HasValue)
        {
            query = query.Where(r => r.Success == filter.Success.Value);
        }

        if (filter.FavoritesOnly == true)
        {
            query = query.Where(r => r.IsFavorite);
        }

        if (!string.IsNullOrWhiteSpace(filter.ConfigurationId))
        {
            query = query.Where(r => r.ConfigurationId == filter.ConfigurationId);
        }

        // Apply sorting
        query = filter.SortBy.ToLower() switch
        {
            "name" => filter.SortDescending ? query.OrderByDescending(r => r.Name) : query.OrderBy(r => r.Name),
            "duration" => filter.SortDescending ? query.OrderByDescending(r => r.Duration) : query.OrderBy(r => r.Duration),
            "size" => filter.SortDescending ? query.OrderByDescending(r => r.FileSizeBytes) : query.OrderBy(r => r.FileSizeBytes),
            _ => filter.SortDescending ? query.OrderByDescending(r => r.CreatedAt) : query.OrderBy(r => r.CreatedAt)
        };

        return query.ToList();
    }

    private string GenerateReportName(StoredReport report)
    {
        var typePrefix = report.ReportType switch
        {
            "validation" => "Validation",
            "subtree-check" => "Subtree Check",
            "content-listing" => "Content Listing",
            _ => "Report"
        };

        return $"{typePrefix} - {report.CreatedAt:yyyy-MM-dd HH:mm}";
    }
}
