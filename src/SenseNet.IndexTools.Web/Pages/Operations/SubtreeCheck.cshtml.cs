using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SenseNet.IndexTools.Core.Models;
using SenseNet.IndexTools.Core.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SenseNet.IndexTools.Web.Pages.Operations
{
    public class SubtreeCheckModel : PageModel
    {
        private readonly ILogger<SubtreeCheckModel> _logger;
        private readonly SubtreeCheckerService _subtreeCheckerService;
        private readonly ReportStorageService _reportStorage;
        private readonly AppSettings _appSettings;

        public SubtreeCheckModel(
            ILogger<SubtreeCheckModel> logger,
            SubtreeCheckerService subtreeCheckerService,
            ReportStorageService reportStorage,
            IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _subtreeCheckerService = subtreeCheckerService;
            _reportStorage = reportStorage;
            _appSettings = appSettings.Value;
        }

        [BindProperty]
        public string IndexPath { get; set; } = string.Empty;

        [BindProperty]
        public string CustomIndexPath { get; set; } = string.Empty;

        [BindProperty]
        public string ConnectionString { get; set; } = string.Empty;

        [BindProperty]
        public string CustomConnectionString { get; set; } = string.Empty;

        [BindProperty]
        public string RepositoryPath { get; set; } = "/Root/Content";

        [BindProperty]
        public bool Recursive { get; set; } = true;

        [BindProperty]
        public int Depth { get; set; } = 0;

        [BindProperty]
        public bool Detailed { get; set; } = true;

        public List<IndexPath> IndexPaths { get; set; } = new();
        public List<DatabaseConnection> DatabaseConnections { get; set; } = new();
        public bool ShowResult { get; set; } = false;
        public string ResultMessage { get; set; } = string.Empty;
        public string ResultClass { get; set; } = "alert-info";
        public SubtreeCheckerService.SubtreeCheckResult? SubtreeCheckResult { get; set; }
        public string ReportId { get; set; } = string.Empty;

        public void OnGet()
        {
            IndexPaths = _appSettings.DefaultIndexPaths;
            DatabaseConnections = _appSettings.DefaultDatabaseConnections;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            IndexPaths = _appSettings.DefaultIndexPaths;
            DatabaseConnections = _appSettings.DefaultDatabaseConnections;
            ShowResult = true;

            // Determine which path to use
            var indexPath = !string.IsNullOrEmpty(CustomIndexPath) ? CustomIndexPath : IndexPath;
            var connString = !string.IsNullOrEmpty(CustomConnectionString) ? CustomConnectionString : ConnectionString;
            
            if (string.IsNullOrEmpty(indexPath) || string.IsNullOrEmpty(connString) || string.IsNullOrEmpty(RepositoryPath))
            {
                ResultMessage = "Please provide all required parameters: index path, connection string, and repository path.";
                ResultClass = "alert-danger";
                return Page();
            }

            try
            {
                _logger.LogInformation("Checking subtree {Path} in index {IndexPath} (Recursive: {Recursive}, Depth: {Depth})", 
                    RepositoryPath, indexPath, Recursive, Depth);
                  var checkResult = await _subtreeCheckerService.CheckSubtreeAsync(
                    indexPath,
                    connString,
                    RepositoryPath,
                    Recursive,
                    Detailed);
                
                SubtreeCheckResult = checkResult;
                
                if (checkResult.MismatchedItems.Count == 0)
                {
                    ResultMessage = $"All items match between database and index for {RepositoryPath}";
                    ResultClass = "alert-success";
                }
                else
                {
                    ResultMessage = $"Found {checkResult.MismatchedItems.Count} mismatched items for {RepositoryPath}";
                    ResultClass = "alert-warning";
                }
                
                // Store the report
                var parameters = new Dictionary<string, string>
                {
                    { "IndexPath", indexPath },
                    { "RepositoryPath", RepositoryPath },
                    { "Recursive", Recursive.ToString() },
                    { "Depth", Depth.ToString() },
                    { "Detailed", Detailed.ToString() }
                };
                
                var reportMetadata = await _reportStorage.StoreReportAsync(
                    "subtree",
                    $"Subtree Check - {RepositoryPath}",
                    checkResult,
                    parameters);
                
                ReportId = reportMetadata.Id;
            }
            catch (Exception ex)
            {
                ResultMessage = $"Error: {ex.Message}";
                ResultClass = "alert-danger";
                _logger.LogError(ex, "Error checking subtree {Path} in index {IndexPath}", RepositoryPath, indexPath);
            }

            return Page();
        }
    }
}
