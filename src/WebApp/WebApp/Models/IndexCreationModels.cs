using System.ComponentModel.DataAnnotations;

namespace WebApp.Models;

public class IndexCreationOptions
{
    [Required(ErrorMessage = "Connection string is required")]
    public string ConnectionString { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Repository path is required")]
    public string RepositoryPath { get; set; } = "/Root";
    
    public string? OutputPath { get; set; }
    public bool Recursive { get; set; } = true;
    
    [Range(1, 1000, ErrorMessage = "Batch size must be between 1 and 1000")]
    public int BatchSize { get; set; } = 100;
    
    [Range(0, int.MaxValue, ErrorMessage = "Max items must be 0 or greater")]
    public int MaxItems { get; set; } = 0;
    
    public bool ForceReindex { get; set; } = false;
    public string ReportFormat { get; set; } = "summary";
    public string OutputFormat { get; set; } = "md";
    
    // Configuration tracking for better report naming
    public string? ConfigurationId { get; set; }
    public string? ConfigurationName { get; set; }
}

public class IndexCreationServiceResult
{
    public string RepositoryPath { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ReportContent { get; set; }
    public string? ReportPath { get; set; }
    public string? IndexPath { get; set; }
    public int ProcessedItemCount { get; set; }
    public IndexCreationOptions? Options { get; set; }
    public string? ConfigurationId { get; set; }
    public string? ConfigurationName { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}
