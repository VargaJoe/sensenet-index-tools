using System.ComponentModel.DataAnnotations;

namespace WebApp.Models;

public class StoredReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Required]
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    [Required]
    public string ReportType { get; set; } = string.Empty; // "validation", "subtree-check", "content-listing"
    
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    [Required]
    public string Format { get; set; } = "html"; // "html" or "md"
    
    [Required]
    public string Content { get; set; } = string.Empty;
    
    public string? FilePath { get; set; } // Optional file path if saved as file
    
    // Execution details
    public TimeSpan? Duration { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    
    // Statistics (varies by report type)
    public Dictionary<string, object> Statistics { get; set; } = new();
    
    // Parameters used to generate the report
    public Dictionary<string, object> Parameters { get; set; } = new();
    
    // Configuration used (if any)
    public string? ConfigurationId { get; set; }
    public string? ConfigurationName { get; set; }
    
    // File management
    public long? FileSizeBytes { get; set; }
    public bool IsFavorite { get; set; } = false;
    public DateTime? LastViewedAt { get; set; }
    public int ViewCount { get; set; } = 0;
    
    // Helper properties
    public string FormattedDuration => Duration?.ToString(@"mm\:ss") ?? "N/A";
    public string FormattedSize => FileSizeBytes.HasValue ? FormatFileSize(FileSizeBytes.Value) : "N/A";
    public string FormattedCreatedAt => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1} MB";
        return $"{bytes / (1024 * 1024 * 1024):F1} GB";
    }
}

public class ReportSearchFilter
{
    public string? SearchTerm { get; set; }
    public string? ReportType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public bool? Success { get; set; }
    public bool? FavoritesOnly { get; set; }
    public string? ConfigurationId { get; set; }
    public string SortBy { get; set; } = "CreatedAt"; // CreatedAt, Name, Duration, Size
    public bool SortDescending { get; set; } = true;
    public int PageSize { get; set; } = 20;
    public int Page { get; set; } = 1;
}
