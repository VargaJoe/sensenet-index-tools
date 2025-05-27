using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SenseNet.IndexTools.Core.Models;
using SenseNet.IndexTools.Core.Services;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace SenseNet.IndexTools.Web.Pages.Reports
{
    public class ViewReportModel : PageModel
    {
        private readonly ILogger<ViewReportModel> _logger;
        private readonly ReportStorageService _reportStorage;

        public ViewReportModel(
            ILogger<ViewReportModel> logger,
            ReportStorageService reportStorage)
        {
            _logger = logger;
            _reportStorage = reportStorage;
        }

        [BindProperty(SupportsGet = true)]
        public string Type { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public string Id { get; set; } = string.Empty;

        public string ReportType => Type;
        public string ReportTitle { get; set; } = "Report Details";
        public ReportStorageService.ReportMetadata? ReportMetadata { get; set; }
        public object? ReportData { get; set; }

        private async Task<(T Report, ReportStorageService.ReportMetadata Metadata)> GetTypedReportAsync<T>(string type, string id)
        {
            var result = await _reportStorage.GetReportAsync<T>(type, id);
            return (result.Report, result.Metadata);
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (string.IsNullOrEmpty(Type) || string.IsNullOrEmpty(Id))
            {
                return RedirectToPage("/Reports/Index");
            }

            try
            {
                switch (Type.ToLower())
                {
                    case "lastactivity":
                        var (lastActivityReportJson, lastActivityMetadata) = await GetTypedReportAsync<JsonElement>(Type, Id);
                        var reportJson = lastActivityReportJson.GetRawText();
                        var tuple = JsonSerializer.Deserialize<(long, long[])>(reportJson);
                        
                        ReportData = new LastActivityReport
                        {
                            LastActivityId = tuple.Item1,
                            ActivityGaps = tuple.Item2,
                            IndexPath = lastActivityMetadata?.Parameters.GetValueOrDefault("IndexPath"),
                            Timestamp = lastActivityMetadata?.CreatedAt.ToString(),
                            Success = true
                        };
                        ReportMetadata = lastActivityMetadata;
                        ReportTitle = ReportMetadata?.Title ?? "LastActivityId Report";
                        break;

                    case "validation":
                        var (validationReportJson, validationMetadata) = await GetTypedReportAsync<JsonElement>(Type, Id);
                        var validationReport = JsonSerializer.Deserialize<ValidationReport>(validationReportJson.GetRawText());
                        if (validationReport != null)
                        {
                            validationReport.IndexPath = validationMetadata?.Parameters.GetValueOrDefault("IndexPath") ?? string.Empty;
                            validationReport.DetailedValidation = true;
                        }
                        ReportData = validationReport;
                        ReportMetadata = validationMetadata;
                        ReportTitle = ReportMetadata?.Title ?? "Validation Report";
                        break;

                    case "subtree":
                        var (subtreeReportJson, subtreeMetadata) = await GetTypedReportAsync<JsonElement>(Type, Id);
                        var subtreeReport = JsonSerializer.Deserialize<SubtreeReport>(subtreeReportJson.GetRawText());
                        ReportData = subtreeReport;
                        ReportMetadata = subtreeMetadata;
                        ReportTitle = ReportMetadata?.Title ?? "Subtree Check Report";
                        break;

                    default:
                        _logger.LogWarning("Unknown report type: {Type}", Type);
                        return RedirectToPage("/Reports/Index");
                }

                return Page();
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error retrieving report {Id} of type {Type}", Id, Type);
                return RedirectToPage("/Reports/Index");
            }
        }
    }
}
