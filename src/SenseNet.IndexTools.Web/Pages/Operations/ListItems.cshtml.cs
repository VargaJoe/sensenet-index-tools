using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenseNet.IndexTools.Core.Models;
using SenseNet.IndexTools.Core.Services;

namespace SenseNet.IndexTools.Web.Pages.Operations
{
    public class ListItemsModel : PageModel
    {
        private readonly ILogger<ListItemsModel> _logger;
        private readonly IndexListerService _indexListerService;
        private readonly DatabaseListerService _dbListerService;
        private readonly ReportStorageService _reportStorage;
        private readonly AppSettings _appSettings;

        public ListItemsModel(
            ILogger<ListItemsModel> logger,
            IndexListerService indexListerService,
            DatabaseListerService dbListerService,
            ReportStorageService reportStorage,
            IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _indexListerService = indexListerService;
            _dbListerService = dbListerService;
            _reportStorage = reportStorage;
            _appSettings = appSettings.Value;
        }

        // Index list properties
        [BindProperty]
        public string IndexPath { get; set; } = string.Empty;

        [BindProperty]
        public string CustomIndexPath { get; set; } = string.Empty;

        [BindProperty]
        public string RepositoryPath { get; set; } = "/Root";

        [BindProperty]
        public bool Recursive { get; set; } = true;

        [BindProperty]
        public int Depth { get; set; } = 0;

        // Database list properties
        [BindProperty]
        public string ConnectionString { get; set; } = string.Empty;

        [BindProperty]
        public string CustomConnectionString { get; set; } = string.Empty;

        [BindProperty]
        public string DbRepositoryPath { get; set; } = "/Root";

        [BindProperty]
        public bool DbRecursive { get; set; } = true;

        [BindProperty]
        public int DbDepth { get; set; } = 0;

        // Compare properties
        [BindProperty]
        public string CompareIndexPath { get; set; } = string.Empty;

        [BindProperty]
        public string CompareCustomIndexPath { get; set; } = string.Empty;

        [BindProperty]
        public string CompareConnectionString { get; set; } = string.Empty;

        [BindProperty]
        public string CompareCustomConnectionString { get; set; } = string.Empty;

        [BindProperty]
        public string CompareRepositoryPath { get; set; } = "/Root";

        [BindProperty]
        public bool CompareRecursive { get; set; } = true;

        [BindProperty]
        public int CompareDepth { get; set; } = 0;

        // Settings and results
        public List<IndexPath> IndexPaths { get; set; } = new();
        public List<DatabaseConnection> DatabaseConnections { get; set; } = new();

        public IndexListerService.IndexListResult? IndexListResult { get; set; }
        public DatabaseListerService.DbListResult? DbListResult { get; set; }
        public CompareResult? CompareResult { get; set; }

        // Add missing properties for view support
        public bool ShowResult { get; set; }
        public string ResultClass { get; set; } = "alert-info";
        public string ResultMessage { get; set; } = string.Empty;
        public List<ContentItem> ContentItems { get; set; } = new List<ContentItem>();

        public void OnGet()
        {
            // Load settings
            IndexPaths = _appSettings.DefaultIndexPaths;
            DatabaseConnections = _appSettings.DefaultDatabaseConnections;
        }

        public async Task<IActionResult> OnPostListIndexAsync()
        {
            IndexPaths = _appSettings.DefaultIndexPaths;
            DatabaseConnections = _appSettings.DefaultDatabaseConnections;

            // Determine actual index path
            string actualIndexPath = IndexPath;
            if (IndexPath == "custom")
            {
                actualIndexPath = CustomIndexPath;
            }

            if (string.IsNullOrEmpty(actualIndexPath))
            {
                ModelState.AddModelError("IndexPath", "Please select or enter an index path.");
                return Page();
            }

            if (string.IsNullOrEmpty(RepositoryPath))
            {
                ModelState.AddModelError("RepositoryPath", "Repository path is required.");
                return Page();
            }

            try
            {
                IndexListResult = await _indexListerService.ListIndexItemsAsync(
                    actualIndexPath,
                    RepositoryPath,
                    Recursive,
                    Depth);                // Store the report
                await _reportStorage.StoreReportAsync(
                    "indexlist",
                    $"Index List - {RepositoryPath}",
                    IndexListResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing index items: {Message}", ex.Message);
                ModelState.AddModelError(string.Empty, $"Error listing index items: {ex.Message}");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostListDatabaseAsync()
        {
            IndexPaths = _appSettings.DefaultIndexPaths;
            DatabaseConnections = _appSettings.DefaultDatabaseConnections;

            // Determine actual connection string
            string actualConnectionString = ConnectionString;
            if (ConnectionString == "custom")
            {
                actualConnectionString = CustomConnectionString;
            }

            if (string.IsNullOrEmpty(actualConnectionString))
            {
                ModelState.AddModelError("ConnectionString", "Please select or enter a database connection string.");
                return Page();
            }

            if (string.IsNullOrEmpty(DbRepositoryPath))
            {
                ModelState.AddModelError("DbRepositoryPath", "Repository path is required.");
                return Page();
            }

            try
            {
                DbListResult = await _dbListerService.ListDatabaseItemsAsync(
                    actualConnectionString,
                    DbRepositoryPath,
                    DbRecursive,
                    DbDepth);                // Store the report
                await _reportStorage.StoreReportAsync(
                    "dblist",
                    $"Database List - {DbRepositoryPath}",
                    DbListResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing database items: {Message}", ex.Message);
                ModelState.AddModelError(string.Empty, $"Error listing database items: {ex.Message}");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostCompareAsync()
        {
            IndexPaths = _appSettings.DefaultIndexPaths;
            DatabaseConnections = _appSettings.DefaultDatabaseConnections;

            // Determine actual index path
            string actualIndexPath = CompareIndexPath;
            if (CompareIndexPath == "custom")
            {
                actualIndexPath = CompareCustomIndexPath;
            }

            // Determine actual connection string
            string actualConnectionString = CompareConnectionString;
            if (CompareConnectionString == "custom")
            {
                actualConnectionString = CompareCustomConnectionString;
            }

            if (string.IsNullOrEmpty(actualIndexPath))
            {
                ModelState.AddModelError("CompareIndexPath", "Please select or enter an index path.");
                return Page();
            }

            if (string.IsNullOrEmpty(actualConnectionString))
            {
                ModelState.AddModelError("CompareConnectionString", "Please select or enter a database connection string.");
                return Page();
            }

            if (string.IsNullOrEmpty(CompareRepositoryPath))
            {
                ModelState.AddModelError("CompareRepositoryPath", "Repository path is required.");
                return Page();
            }

            try
            {
                var result = new CompareResult
                {
                    IndexPath = actualIndexPath,
                    RepositoryPath = CompareRepositoryPath,
                    Recursive = CompareRecursive,
                    Depth = CompareDepth,
                    StartTime = DateTime.Now
                };

                // Get items from database
                var dbResult = await _dbListerService.ListDatabaseItemsAsync(
                    actualConnectionString,
                    CompareRepositoryPath,
                    CompareRecursive,
                    CompareDepth);

                // Get items from index
                var indexResult = await _indexListerService.ListIndexItemsAsync(
                    actualIndexPath,
                    CompareRepositoryPath,
                    CompareRecursive,
                    CompareDepth);

                // Add any errors from both operations
                result.Errors.AddRange(dbResult.Errors);
                result.Errors.AddRange(indexResult.Errors);
                
                // Process results if both operations succeeded
                if (!dbResult.Errors.Any() && !indexResult.Errors.Any())
                {
                    result.DbItemCount = dbResult.Items.Count;
                    result.IndexItemCount = indexResult.Items.Count;

                    // Convert to comparison items
                    var dbItems = dbResult.Items.Select(i => new ContentItem
                    {
                        NodeId = i.NodeId,
                        VersionId = i.VersionId,
                        Path = i.Path,
                        NodeType = i.NodeType,
                        InDatabase = true,
                        InIndex = false
                    }).ToList();

                    var indexItems = indexResult.Items.Select(i => new ContentItem
                    {
                        Path = i.Path,
                        NodeType = i.Type,
                        InDatabase = false,
                        InIndex = true,
                        IndexNodeId = i.Id,
                        IndexVersionId = i.VersionId
                    }).ToList();

                    // Combine and group items by path for side-by-side display
                    var combinedItems = dbItems.Union(indexItems)
                        .GroupBy(i => i.Path.ToLowerInvariant())
                        .Select(g =>
                        {
                            var dbItem = g.FirstOrDefault(i => i.InDatabase);
                            var indexItem = g.FirstOrDefault(i => i.InIndex);

                            if (dbItem != null && indexItem != null)
                            {
                                // Found in both - merge the index data into the DB item
                                dbItem.InIndex = true;
                                dbItem.IndexNodeId = indexItem.IndexNodeId;
                                dbItem.IndexVersionId = indexItem.IndexVersionId;
                                return dbItem;
                            }
                            
                            return dbItem ?? indexItem!;
                        }).ToList();

                    // Count matched/mismatched items
                    var matchedItems = combinedItems.Where(i => i.Status == "Match").ToList();
                    var mismatchedItems = combinedItems.Where(i => i.Status != "Match").ToList();

                    result.MatchedItemCount = matchedItems.Count;
                    result.MismatchedItemCount = mismatchedItems.Count;
                    result.MismatchedItems = mismatchedItems;
                }

                result.EndTime = DateTime.Now;
                CompareResult = result;                // Store the report
                await _reportStorage.StoreReportAsync(
                    "compare",
                    $"Compare - {CompareRepositoryPath}",
                    result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error comparing items: {Message}", ex.Message);
                ModelState.AddModelError(string.Empty, $"Error comparing items: {ex.Message}");
            }

            return Page();
        }
    }
}
