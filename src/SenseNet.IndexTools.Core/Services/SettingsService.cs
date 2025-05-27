using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenseNet.IndexTools.Core.Models;

namespace SenseNet.IndexTools.Core.Services
{
    /// <summary>
    /// Service for managing application settings persistence
    /// </summary>
    public class SettingsService
    {
        private readonly ILogger<SettingsService> _logger;
        private readonly string _settingsPath;
        private readonly IOptionsMonitor<AppSettings> _appSettingsMonitor;
        private readonly IConfiguration _configuration;

        public SettingsService(
            ILogger<SettingsService> logger,
            string contentRootPath,
            IOptionsMonitor<AppSettings> appSettingsMonitor,
            IConfiguration configuration)
        {
            _logger = logger;
            _settingsPath = Path.Combine(contentRootPath, "usersettings.json");
            _appSettingsMonitor = appSettingsMonitor;
            _configuration = configuration;
        }

        /// <summary>
        /// Gets the current application settings
        /// </summary>
        public AppSettings GetCurrentSettings()
        {
            return _appSettingsMonitor.CurrentValue;
        }

        /// <summary>
        /// Saves the application settings to a JSON file
        /// </summary>
        /// <param name="settings">The settings to save</param>
        public async Task SaveSettingsAsync(AppSettings settings)
        {
            try
            {
                // Create a root object to match the expected structure
                var rootObject = new
                {
                    AppSettings = settings
                };

                // Serialize the settings to JSON
                var json = JsonSerializer.Serialize(rootObject, new JsonSerializerOptions { WriteIndented = true });
                
                // Write to the file
                await File.WriteAllTextAsync(_settingsPath, json);
                
                _logger.LogInformation("Settings saved to {SettingsPath}", _settingsPath);

                // Force configuration reload
                if (_configuration is IConfigurationRoot configRoot)
                {
                    configRoot.Reload();
                    _logger.LogInformation("Configuration reloaded");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Loads the settings from the JSON file
        /// </summary>
        /// <returns>The loaded settings, or default settings if the file doesn't exist</returns>
        public async Task<AppSettings> LoadSettingsAsync()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    _logger.LogInformation("Settings file not found at {SettingsPath}, using defaults", _settingsPath);
                    return new AppSettings();
                }

                var json = await File.ReadAllTextAsync(_settingsPath);
                var rootObject = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (rootObject.TryGetProperty("AppSettings", out var appSettingsElement))
                {
                    var settings = JsonSerializer.Deserialize<AppSettings>(appSettingsElement.GetRawText());
                    return settings ?? new AppSettings();
                }
                
                _logger.LogWarning("Settings file doesn't contain 'AppSettings' section");
                return new AppSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings: {Message}", ex.Message);
                return new AppSettings();
            }
        }
    }
}
