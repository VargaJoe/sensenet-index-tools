using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenseNet.IndexTools.Core.Models;
using SenseNet.IndexTools.Core.Services;
using System;
using System.Threading.Tasks;

namespace SenseNet.IndexTools.Web.Pages.Settings
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IOptionsMonitor<AppSettings> _appSettingsMonitor;
        private readonly SettingsService _settingsService;

        public IndexModel(
            ILogger<IndexModel> logger,
            IOptionsMonitor<AppSettings> appSettingsMonitor,
            SettingsService settingsService)
        {
            _logger = logger;
            _appSettingsMonitor = appSettingsMonitor;
            _settingsService = settingsService;
        }

        public AppSettings AppSettings { get; set; } = new AppSettings();
        public string StatusMessage { get; set; } = string.Empty;
        public string StatusClass { get; set; } = "alert-info";

        public void OnGet()
        {
            AppSettings = _appSettingsMonitor.CurrentValue;
        }

        public async Task<IActionResult> OnPostAddIndexPathAsync(string name, string path)
        {
            AppSettings = _appSettingsMonitor.CurrentValue;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
            {
                StatusMessage = "Name and path are required.";
                StatusClass = "alert-danger";
                return Page();
            }

            var indexPath = new IndexPath { Name = name, Path = path };
            AppSettings.DefaultIndexPaths.Add(indexPath);

            await UpdateAppSettingsAsync();

            StatusMessage = $"Index path '{name}' added successfully.";
            StatusClass = "alert-success";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteIndexPathAsync(int index)
        {
            AppSettings = _appSettingsMonitor.CurrentValue;

            if (index >= 0 && index < AppSettings.DefaultIndexPaths.Count)
            {
                var name = AppSettings.DefaultIndexPaths[index].Name;
                AppSettings.DefaultIndexPaths.RemoveAt(index);
                await UpdateAppSettingsAsync();

                StatusMessage = $"Index path '{name}' removed successfully.";
                StatusClass = "alert-success";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostAddDatabaseConnectionAsync(string name, string connectionString)
        {
            AppSettings = _appSettingsMonitor.CurrentValue;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(connectionString))
            {
                StatusMessage = "Name and connection string are required.";
                StatusClass = "alert-danger";
                return Page();
            }

            var dbConnection = new DatabaseConnection { Name = name, ConnectionString = connectionString };
            AppSettings.DefaultDatabaseConnections.Add(dbConnection);

            await UpdateAppSettingsAsync();

            StatusMessage = $"Database connection '{name}' added successfully.";
            StatusClass = "alert-success";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteDatabaseConnectionAsync(int index)
        {
            AppSettings = _appSettingsMonitor.CurrentValue;

            if (index >= 0 && index < AppSettings.DefaultDatabaseConnections.Count)
            {
                var name = AppSettings.DefaultDatabaseConnections[index].Name;
                AppSettings.DefaultDatabaseConnections.RemoveAt(index);
                await UpdateAppSettingsAsync();

                StatusMessage = $"Database connection '{name}' removed successfully.";
                StatusClass = "alert-success";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateBackupSettingsAsync(bool createBackupsByDefault, string defaultBackupPath)
        {
            AppSettings = _appSettingsMonitor.CurrentValue;

            AppSettings.BackupSettings.CreateBackupsByDefault = createBackupsByDefault;
            AppSettings.BackupSettings.DefaultBackupPath = defaultBackupPath ?? string.Empty;

            await UpdateAppSettingsAsync();

            StatusMessage = "Backup settings updated successfully.";
            StatusClass = "alert-success";
            return RedirectToPage();
        }

        private async Task UpdateAppSettingsAsync()
        {
            try
            {
                // Use the settings service to save the settings
                await _settingsService.SaveSettingsAsync(AppSettings);
                _logger.LogInformation("Settings updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings: {Message}", ex.Message);
                StatusMessage = $"Error saving settings: {ex.Message}";
                StatusClass = "alert-danger";
            }
        }
    }
}