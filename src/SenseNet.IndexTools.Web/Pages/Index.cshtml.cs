using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SenseNet.IndexTools.Core.Models;
using SenseNet.IndexTools.Core.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SenseNet.IndexTools.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly ReportStorageService _reportStorage;
    private readonly AppSettings _appSettings;

    public IndexModel(
        ILogger<IndexModel> logger,
        ReportStorageService reportStorage,
        IOptions<AppSettings> appSettings)
    {
        _logger = logger;
        _reportStorage = reportStorage;
        _appSettings = appSettings.Value;
    }

    public List<ReportStorageService.ReportMetadata> RecentReports { get; set; } = new();
    public List<IndexPath> IndexPaths { get; set; } = new();
    public BackupSettings BackupSettings { get; set; } = new();

    public async Task OnGetAsync()
    {
        // Get recent reports of all types
        var validationReports = await _reportStorage.GetReportMetadataListAsync("validation");
        var lastActivityReports = await _reportStorage.GetReportMetadataListAsync("lastactivity");
        var subtreeReports = await _reportStorage.GetReportMetadataListAsync("subtree");
        
        // Combine and sort by date
        var allReports = validationReports
            .Concat(lastActivityReports)
            .Concat(subtreeReports)
            .OrderByDescending(r => r.CreatedAt)
            .Take(5) // Show only the 5 most recent
            .ToList();
        
        RecentReports = allReports;
        
        // Get settings
        IndexPaths = _appSettings.DefaultIndexPaths;
        BackupSettings = _appSettings.BackupSettings;
    }
}
