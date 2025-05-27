using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SenseNet.IndexTools.Core.Models;
using SenseNet.IndexTools.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SenseNet.IndexTools.Web.Pages.Operations
{
    public class LastActivityIdModel : PageModel
    {
        private readonly ILogger<LastActivityIdModel> _logger;
        private readonly LastActivityIdService _lastActivityIdService;
        private readonly ReportStorageService _reportStorage;
        private readonly AppSettings _appSettings;

        public LastActivityIdModel(
            ILogger<LastActivityIdModel> logger,
            LastActivityIdService lastActivityIdService,
            ReportStorageService reportStorage,
            IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _lastActivityIdService = lastActivityIdService;
            _reportStorage = reportStorage;
            _appSettings = appSettings.Value;
        }

        [BindProperty]
        public string IndexPath { get; set; } = string.Empty;

        [BindProperty]
        public string CustomIndexPath { get; set; } = string.Empty;

        [BindProperty]
        public long? NewId { get; set; }

        [BindProperty]
        public long? InitialId { get; set; }

        [BindProperty]
        public bool CreateBackup { get; set; } = true;

        [BindProperty]
        public string BackupPath { get; set; } = string.Empty;

        public List<IndexPath> IndexPaths { get; set; } = new();
        public bool ShowResult { get; set; } = false;
        public string ResultMessage { get; set; } = string.Empty;
        public string ResultClass { get; set; } = "alert-info";
        public long? LastActivityId { get; set; }
        public IEnumerable<long>? ActivityGaps { get; set; }

        public void OnGet()
        {
            IndexPaths = _appSettings.DefaultIndexPaths;
        }

        public async Task<IActionResult> OnPostGetLastActivityIdAsync(string indexPath, string customIndexPath)
        {
            IndexPaths = _appSettings.DefaultIndexPaths;
            ShowResult = true;

            // Determine which path to use
            var path = !string.IsNullOrEmpty(customIndexPath) ? customIndexPath : indexPath;
            
            if (string.IsNullOrEmpty(path))
            {
                ResultMessage = "Please provide an index path.";
                ResultClass = "alert-danger";
                return Page();
            }

            try
            {
                var result = await _lastActivityIdService.GetLastActivityIdAsync(path);
                LastActivityId = result.LastActivityId;
                ActivityGaps = result.Gaps;
                
                ResultMessage = $"Successfully retrieved LastActivityId from index at {path}";
                ResultClass = "alert-success";
                
                // Store the report
                var parameters = new Dictionary<string, string>
                {
                    { "IndexPath", path }
                };
                
                await _reportStorage.StoreReportAsync(
                    "lastactivity",
                    $"LastActivityId - Get - {path}",
                    new
                    {
                        LastActivityId = result.LastActivityId,
                        ActivityGaps = result.Gaps,
                        IndexPath = path,
                        Timestamp = DateTime.Now
                    },
                    parameters);
            }
            catch (Exception ex)
            {
                ResultMessage = $"Error: {ex.Message}";
                ResultClass = "alert-danger";
                _logger.LogError(ex, "Error getting LastActivityId from {Path}", path);
            }

            return Page();
        }

        public async Task<IActionResult> OnPostSetLastActivityIdAsync(string indexPath, string customIndexPath, long newId, bool createBackup, string backupPath)
        {
            IndexPaths = _appSettings.DefaultIndexPaths;
            ShowResult = true;

            // Determine which path to use
            var path = !string.IsNullOrEmpty(customIndexPath) ? customIndexPath : indexPath;
            
            if (string.IsNullOrEmpty(path))
            {
                ResultMessage = "Please provide an index path.";
                ResultClass = "alert-danger";
                return Page();
            }

            try
            {
                var success = await _lastActivityIdService.SetLastActivityIdAsync(path, newId, createBackup, backupPath);
                
                if (success)
                {
                    ResultMessage = $"Successfully set LastActivityId to {newId} in index at {path}";
                    ResultClass = "alert-success";
                    LastActivityId = newId;
                }
                else
                {
                    ResultMessage = "Operation completed but could not verify success.";
                    ResultClass = "alert-warning";
                }
                
                // Store the report
                var parameters = new Dictionary<string, string>
                {
                    { "IndexPath", path },
                    { "NewId", newId.ToString() },
                    { "CreateBackup", createBackup.ToString() }
                };
                
                if (!string.IsNullOrEmpty(backupPath))
                {
                    parameters.Add("BackupPath", backupPath);
                }
                
                await _reportStorage.StoreReportAsync(
                    "lastactivity",
                    $"LastActivityId - Set - {path}",
                    new
                    {
                        LastActivityId = newId,
                        IndexPath = path,
                        CreateBackup = createBackup,
                        BackupPath = backupPath,
                        Success = success,
                        Timestamp = DateTime.Now
                    },
                    parameters);
            }
            catch (Exception ex)
            {
                ResultMessage = $"Error: {ex.Message}";
                ResultClass = "alert-danger";
                _logger.LogError(ex, "Error setting LastActivityId in {Path}", path);
            }

            return Page();
        }

        public async Task<IActionResult> OnPostInitLastActivityIdAsync(string indexPath, string customIndexPath, long initialId, bool createBackup, string backupPath)
        {
            IndexPaths = _appSettings.DefaultIndexPaths;
            ShowResult = true;

            // Determine which path to use
            var path = !string.IsNullOrEmpty(customIndexPath) ? customIndexPath : indexPath;
            
            if (string.IsNullOrEmpty(path))
            {
                ResultMessage = "Please provide an index path.";
                ResultClass = "alert-danger";
                return Page();
            }

            try
            {
                var success = await _lastActivityIdService.InitLastActivityIdAsync(path, initialId, createBackup, backupPath);
                
                if (success)
                {
                    ResultMessage = $"Successfully initialized LastActivityId to {initialId} in index at {path}";
                    ResultClass = "alert-success";
                    LastActivityId = initialId;
                }
                else
                {
                    ResultMessage = "Operation completed but could not verify success.";
                    ResultClass = "alert-warning";
                }
                
                // Store the report
                var parameters = new Dictionary<string, string>
                {
                    { "IndexPath", path },
                    { "InitialId", initialId.ToString() },
                    { "CreateBackup", createBackup.ToString() }
                };
                
                if (!string.IsNullOrEmpty(backupPath))
                {
                    parameters.Add("BackupPath", backupPath);
                }
                
                await _reportStorage.StoreReportAsync(
                    "lastactivity",
                    $"LastActivityId - Initialize - {path}",
                    new
                    {
                        LastActivityId = initialId,
                        IndexPath = path,
                        CreateBackup = createBackup,
                        BackupPath = backupPath,
                        Success = success,
                        Timestamp = DateTime.Now
                    },
                    parameters);
            }
            catch (Exception ex)
            {
                ResultMessage = $"Error: {ex.Message}";
                ResultClass = "alert-danger";
                _logger.LogError(ex, "Error initializing LastActivityId in {Path}", path);
            }

            return Page();
        }
    }
}
