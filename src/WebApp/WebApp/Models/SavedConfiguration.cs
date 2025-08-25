using System.ComponentModel.DataAnnotations;

namespace WebApp.Models;

/// <summary>
/// Represents a saved configuration that can be reused across different operations
/// </summary>
public class SavedConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required(ErrorMessage = "Configuration name is required")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string? Description { get; set; }
    
    /// <summary>
    /// Path to the Lucene index directory
    /// </summary>
    [StringLength(500, ErrorMessage = "Index path cannot exceed 500 characters")]
    public string? IndexPath { get; set; }
    
    /// <summary>
    /// SQL Server connection string for database operations
    /// </summary>
    [StringLength(1000, ErrorMessage = "Connection string cannot exceed 1000 characters")]
    public string? ConnectionString { get; set; }
    
    /// <summary>
    /// Repository path for content operations
    /// </summary>
    [StringLength(500, ErrorMessage = "Repository path cannot exceed 500 characters")]
    public string? RepositoryPath { get; set; }
    
    /// <summary>
    /// Default output directory for reports
    /// </summary>
    [StringLength(500, ErrorMessage = "Output path cannot exceed 500 characters")]
    public string? DefaultOutputPath { get; set; }
    
    /// <summary>
    /// When this configuration was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When this configuration was last modified
    /// </summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// How many times this configuration has been used
    /// </summary>
    public int UsageCount { get; set; } = 0;
    
    /// <summary>
    /// When this configuration was last used
    /// </summary>
    public DateTime? LastUsed { get; set; }
    
    /// <summary>
    /// Whether this configuration is marked as favorite
    /// </summary>
    public bool IsFavorite { get; set; } = false;
    
    /// <summary>
    /// Tags for organizing configurations
    /// </summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Options for creating or updating a configuration
/// </summary>
public class ConfigurationOptions
{
    [Required(ErrorMessage = "Configuration name is required")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string? Description { get; set; }
    
    [StringLength(500, ErrorMessage = "Index path cannot exceed 500 characters")]
    public string? IndexPath { get; set; }
    
    [StringLength(1000, ErrorMessage = "Connection string cannot exceed 1000 characters")]
    public string? ConnectionString { get; set; }
    
    [StringLength(500, ErrorMessage = "Repository path cannot exceed 500 characters")]
    public string? RepositoryPath { get; set; }
    
    [StringLength(500, ErrorMessage = "Output path cannot exceed 500 characters")]
    public string? DefaultOutputPath { get; set; }
    
    public bool IsFavorite { get; set; } = false;
    
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Result of configuration operations
/// </summary>
public class ConfigurationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SavedConfiguration? Configuration { get; set; }
}
