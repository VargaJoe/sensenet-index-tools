using System.Text.Json;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// Service for managing saved configurations that can be reused across operations
/// </summary>
public class ConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly string _configurationFilePath;
    private List<SavedConfiguration> _configurations = new();

    public ConfigurationService(ILogger<ConfigurationService> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        // Store configurations in a JSON file in the Data directory
        var dataDirectory = Path.Combine(environment.ContentRootPath, "Data");
        Directory.CreateDirectory(dataDirectory);
        _configurationFilePath = Path.Combine(dataDirectory, "configurations.json");
        
        LoadConfigurations();
    }

    /// <summary>
    /// Get all saved configurations
    /// </summary>
    public async Task<List<SavedConfiguration>> GetAllConfigurationsAsync()
    {
        await Task.CompletedTask; // For consistency with async pattern
        return _configurations.OrderBy(c => c.Name).ToList();
    }

    /// <summary>
    /// Get configurations marked as favorites
    /// </summary>
    public async Task<List<SavedConfiguration>> GetFavoriteConfigurationsAsync()
    {
        await Task.CompletedTask;
        return _configurations.Where(c => c.IsFavorite)
                             .OrderBy(c => c.Name)
                             .ToList();
    }

    /// <summary>
    /// Get recently used configurations
    /// </summary>
    public async Task<List<SavedConfiguration>> GetRecentConfigurationsAsync(int count = 5)
    {
        await Task.CompletedTask;
        return _configurations.Where(c => c.LastUsed.HasValue)
                             .OrderByDescending(c => c.LastUsed)
                             .Take(count)
                             .ToList();
    }

    /// <summary>
    /// Get a specific configuration by ID
    /// </summary>
    public async Task<SavedConfiguration?> GetConfigurationAsync(Guid id)
    {
        await Task.CompletedTask;
        return _configurations.FirstOrDefault(c => c.Id == id);
    }

    /// <summary>
    /// Create a new configuration
    /// </summary>
    public async Task<ConfigurationResult> CreateConfigurationAsync(ConfigurationOptions options)
    {
        try
        {
            // Check if name already exists
            if (_configurations.Any(c => c.Name.Equals(options.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return new ConfigurationResult
                {
                    Success = false,
                    Message = $"A configuration with the name '{options.Name}' already exists."
                };
            }

            var configuration = new SavedConfiguration
            {
                Name = options.Name,
                Description = options.Description,
                IndexPath = options.IndexPath,
                ConnectionString = options.ConnectionString,
                RepositoryPath = options.RepositoryPath,
                DefaultOutputPath = options.DefaultOutputPath,
                IsFavorite = options.IsFavorite,
                Tags = options.Tags
            };

            _configurations.Add(configuration);
            await SaveConfigurationsAsync();

            _logger.LogInformation("Created new configuration: {ConfigurationName}", options.Name);

            return new ConfigurationResult
            {
                Success = true,
                Message = "Configuration created successfully.",
                Configuration = configuration
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating configuration: {ConfigurationName}", options.Name);
            return new ConfigurationResult
            {
                Success = false,
                Message = $"Error creating configuration: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Update an existing configuration
    /// </summary>
    public async Task<ConfigurationResult> UpdateConfigurationAsync(Guid id, ConfigurationOptions options)
    {
        try
        {
            var configuration = _configurations.FirstOrDefault(c => c.Id == id);
            if (configuration == null)
            {
                return new ConfigurationResult
                {
                    Success = false,
                    Message = "Configuration not found."
                };
            }

            // Check if name already exists (excluding current configuration)
            if (_configurations.Any(c => c.Id != id && c.Name.Equals(options.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return new ConfigurationResult
                {
                    Success = false,
                    Message = $"A configuration with the name '{options.Name}' already exists."
                };
            }

            configuration.Name = options.Name;
            configuration.Description = options.Description;
            configuration.IndexPath = options.IndexPath;
            configuration.ConnectionString = options.ConnectionString;
            configuration.RepositoryPath = options.RepositoryPath;
            configuration.DefaultOutputPath = options.DefaultOutputPath;
            configuration.IsFavorite = options.IsFavorite;
            configuration.Tags = options.Tags;
            configuration.LastModified = DateTime.UtcNow;

            await SaveConfigurationsAsync();

            _logger.LogInformation("Updated configuration: {ConfigurationName}", options.Name);

            return new ConfigurationResult
            {
                Success = true,
                Message = "Configuration updated successfully.",
                Configuration = configuration
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating configuration: {ConfigurationId}", id);
            return new ConfigurationResult
            {
                Success = false,
                Message = $"Error updating configuration: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Delete a configuration
    /// </summary>
    public async Task<ConfigurationResult> DeleteConfigurationAsync(Guid id)
    {
        try
        {
            var configuration = _configurations.FirstOrDefault(c => c.Id == id);
            if (configuration == null)
            {
                return new ConfigurationResult
                {
                    Success = false,
                    Message = "Configuration not found."
                };
            }

            _configurations.Remove(configuration);
            await SaveConfigurationsAsync();

            _logger.LogInformation("Deleted configuration: {ConfigurationName}", configuration.Name);

            return new ConfigurationResult
            {
                Success = true,
                Message = "Configuration deleted successfully."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting configuration: {ConfigurationId}", id);
            return new ConfigurationResult
            {
                Success = false,
                Message = $"Error deleting configuration: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Mark a configuration as used (increments usage count and updates last used)
    /// </summary>
    public async Task MarkConfigurationUsedAsync(Guid id)
    {
        try
        {
            var configuration = _configurations.FirstOrDefault(c => c.Id == id);
            if (configuration != null)
            {
                configuration.UsageCount++;
                configuration.LastUsed = DateTime.UtcNow;
                await SaveConfigurationsAsync();
                
                _logger.LogDebug("Marked configuration as used: {ConfigurationName}", configuration.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking configuration as used: {ConfigurationId}", id);
        }
    }

    /// <summary>
    /// Search configurations by name, description, or tags
    /// </summary>
    public async Task<List<SavedConfiguration>> SearchConfigurationsAsync(string searchTerm)
    {
        await Task.CompletedTask;
        
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return await GetAllConfigurationsAsync();
        }

        var term = searchTerm.ToLowerInvariant();
        return _configurations.Where(c =>
            c.Name.ToLowerInvariant().Contains(term) ||
            (c.Description?.ToLowerInvariant().Contains(term) ?? false) ||
            c.Tags.Any(tag => tag.ToLowerInvariant().Contains(term))
        ).OrderBy(c => c.Name).ToList();
    }

    /// <summary>
    /// Load configurations from JSON file
    /// </summary>
    private void LoadConfigurations()
    {
        try
        {
            if (File.Exists(_configurationFilePath))
            {
                var json = File.ReadAllText(_configurationFilePath);
                var configurations = JsonSerializer.Deserialize<List<SavedConfiguration>>(json);
                _configurations = configurations ?? new List<SavedConfiguration>();
                
                _logger.LogInformation("Loaded {Count} configurations from {FilePath}", 
                    _configurations.Count, _configurationFilePath);
            }
            else
            {
                _configurations = new List<SavedConfiguration>();
                _logger.LogInformation("No existing configuration file found. Starting with empty configuration list.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configurations from {FilePath}", _configurationFilePath);
            _configurations = new List<SavedConfiguration>();
        }
    }

    /// <summary>
    /// Save configurations to JSON file
    /// </summary>
    private async Task SaveConfigurationsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_configurations, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(_configurationFilePath, json);
            _logger.LogDebug("Saved {Count} configurations to {FilePath}", 
                _configurations.Count, _configurationFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configurations to {FilePath}", _configurationFilePath);
            throw;
        }
    }
}
