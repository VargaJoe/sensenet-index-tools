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
    public class ValidateModel : PageModel
    {
        private readonly ILogger<ValidateModel> _logger;
        private readonly ValidationService _validationService;
        private readonly ReportStorageService _reportStorage;
        private readonly AppSettings _appSettings;

        public ValidateModel(
            ILogger<ValidateModel> logger,
            ValidationService validationService,
            ReportStorageService reportStorage,
            IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _validationService = validationService;
            _reportStorage = reportStorage;
            _appSettings = appSettings.Value;
        }

        [BindProperty]
        public string IndexPath { get; set; } = string.Empty;

        [BindProperty]
        public string CustomIndexPath { get; set; } = string.Empty;

        [BindProperty]
        public bool Detailed { get; set; } = true;

        [BindProperty]
        public int? SampleSize { get; set; } = 100;

        [BindProperty]
        public bool CreateBackup { get; set; } = true;

        [BindProperty]
        public string BackupPath { get; set; } = string.Empty;

        public List<IndexPath> IndexPaths { get; set; } = new();
        public bool ShowResult { get; set; } = false;
        public string ResultMessage { get; set; } = string.Empty;
        public string ResultClass { get; set; } = "alert-info";
        public ValidationService.ValidationResult? ValidationResult { get; set; }
        public string ReportId { get; set; } = string.Empty;

        public void OnGet()
        {
            IndexPaths = _appSettings.DefaultIndexPaths;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            IndexPaths = _appSettings.DefaultIndexPaths;
            ShowResult = true;

            // Determine which path to use
            var path = !string.IsNullOrEmpty(CustomIndexPath) ? CustomIndexPath : IndexPath;
            
            if (string.IsNullOrEmpty(path))
            {
                ResultMessage = "Please provide an index path.";
                ResultClass = "alert-danger";
                return Page();
            }

            try
            {
                _logger.LogInformation("Validating index at {Path} (Detailed: {Detailed})", path, Detailed);
                
                var validationResult = await _validationService.ValidateIndexAsync(
                    path, 
                    Detailed, 
                    SampleSize, 
                    CreateBackup, 
                    BackupPath);
                
                ValidationResult = validationResult;
                
                if (validationResult.IsValid)
                {
                    ResultMessage = $"Index validation successful: {path}";
                    ResultClass = "alert-success";
                }
                else
                {
                    ResultMessage = $"Index validation found issues: {path}";
                    ResultClass = "alert-warning";
                }
                
                // Store the report
                var parameters = new Dictionary<string, string>
                {
                    { "IndexPath", path },
                    { "Detailed", Detailed.ToString() }
                };
                
                if (SampleSize.HasValue)
                {
                    parameters.Add("SampleSize", SampleSize.ToString());
                }
                
                if (CreateBackup)
                {
                    parameters.Add("CreateBackup", "true");
                    if (!string.IsNullOrEmpty(BackupPath))
                    {
                        parameters.Add("BackupPath", BackupPath);
                    }
                }
                
                var reportMetadata = await _reportStorage.StoreReportAsync(
                    "validation",
                    $"Validation - {path}",
                    validationResult,
                    parameters);
                
                ReportId = reportMetadata.Id;
            }
            catch (Exception ex)
            {
                ResultMessage = $"Error: {ex.Message}";
                ResultClass = "alert-danger";
                _logger.LogError(ex, "Error validating index at {Path}", path);
            }

            return Page();
        }
    }
}
