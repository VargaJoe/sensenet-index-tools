using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SenseNet.IndexTools.Core.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SenseNet.IndexTools.Web.Pages.Reports
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly ReportStorageService _reportStorage;

        public IndexModel(
            ILogger<IndexModel> logger,
            ReportStorageService reportStorage)
        {
            _logger = logger;
            _reportStorage = reportStorage;
        }

        public List<ReportStorageService.ReportMetadata> LastActivityReports { get; set; } = new();
        public List<ReportStorageService.ReportMetadata> ValidationReports { get; set; } = new();
        public List<ReportStorageService.ReportMetadata> SubtreeReports { get; set; } = new();

        public async Task OnGetAsync()
        {
            LastActivityReports = await _reportStorage.GetReportMetadataListAsync("lastactivity");
            ValidationReports = await _reportStorage.GetReportMetadataListAsync("validation");
            SubtreeReports = await _reportStorage.GetReportMetadataListAsync("subtree");
        }

        public async Task<IActionResult> OnPostDeleteReportAsync(string reportType, string reportId)
        {
            if (string.IsNullOrEmpty(reportType) || string.IsNullOrEmpty(reportId))
            {
                return RedirectToPage();
            }

            await _reportStorage.DeleteReportAsync(reportType, reportId);
            
            return RedirectToPage();
        }
    }
}
