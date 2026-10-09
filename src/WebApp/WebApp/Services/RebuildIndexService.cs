using SenseNetIndexTools;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace WebApp.Services
{
    public class RebuildIndexService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<RebuildIndexService> _logger;

        public RebuildIndexService(HttpClient httpClient, ILogger<RebuildIndexService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<RebuildIndexResult> RebuildIndexAsync(RebuildIndexOptions options)
        {
            var result = new RebuildIndexResult
            {
                StartTime = DateTime.Now,
                Options = options
            };

            try
            {
                if (new[] { options.FilePath, options.ContentId, options.ContentPath }.Count(v => !string.IsNullOrWhiteSpace(v)) != 1)
                    throw new ArgumentException("Specify exactly one file, content path or content ID.");
                if (!string.IsNullOrEmpty(options.FilePath))
                {
                    result = await ProcessFileAsync(options);
                }
                else
                {
                    var singleResult = await RebuildSingleItemAsync(options);
                    result.ItemsProcessed = 1;
                    result.SuccessfulItems = singleResult.Success ? 1 : 0;
                    result.FailedItems = singleResult.Success ? 0 : 1;
                    result.ItemResults = new List<RebuildItemResult> { singleResult };
                }

                result.Success = result.ItemsProcessed > 0 && result.FailedItems == 0 && result.ErrorMessage == null;
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during index rebuild operation");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        private async Task<RebuildIndexResult> ProcessFileAsync(RebuildIndexOptions options)
        {
            var result = new RebuildIndexResult
            {
                StartTime = DateTime.Now,
                Options = options,
                ItemResults = new List<RebuildItemResult>()
            };

            try
            {
                if (!File.Exists(options.FilePath))
                {
                    throw new FileNotFoundException($"File not found: {options.FilePath}");
                }

                var lines = await File.ReadAllLinesAsync(options.FilePath);
                result.ItemsProcessed = 0;
                result.SuccessfulItems = 0;
                result.FailedItems = 0;

                foreach (var line in lines)
                {
                    var item = line.Trim();
                    if (string.IsNullOrWhiteSpace(item) || item.StartsWith("#"))
                    {
                        continue; // Skip empty lines and comments
                    }

                    result.ItemsProcessed++;

                    var itemOptions = new RebuildIndexOptions
                    {
                        RepoUrl = options.RepoUrl,
                        ApiKey = options.ApiKey,
                        Recursive = options.Recursive,
                        RebuildLevel = options.RebuildLevel,
                        Verbose = options.Verbose
                    };

                    if (item.StartsWith("/Root"))
                    {
                        itemOptions.ContentPath = item;
                    }
                    else if (int.TryParse(item, out _))
                    {
                        itemOptions.ContentId = item;
                    }
                    else
                    {
                        var failedResult = new RebuildItemResult
                        {
                            Identifier = item,
                            Success = false,
                            ErrorMessage = "Invalid format (not a path or numeric ID)"
                        };
                        result.ItemResults.Add(failedResult);
                        result.FailedItems++;
                        continue;
                    }

                    var itemResult = await RebuildSingleItemAsync(itemOptions);
                    result.ItemResults.Add(itemResult);

                    if (itemResult.Success)
                    {
                        result.SuccessfulItems++;
                    }
                    else
                    {
                        result.FailedItems++;
                    }

                    // Small delay to avoid overwhelming the server
                    await Task.Delay(100);
                }

                result.Success = result.ItemsProcessed > 0 && result.FailedItems == 0 && result.ErrorMessage == null;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<RebuildItemResult> RebuildSingleItemAsync(RebuildIndexOptions options)
        {
            var result = new RebuildItemResult
            {
                Identifier = options.ContentPath ?? options.ContentId ?? "Unknown"
            };

            try
            {
                using var request = RebuildIndexRequest.Create(options.RepoUrl, options.ApiKey,
                    options.ContentPath, options.ContentId, options.Recursive, options.RebuildLevel, SenseNetIndexTools.RuntimeSettings.Load().BearerToken);
                if (options.Verbose) _logger.LogInformation("Request URL: {Url}", request.RequestUri);
                using var response = await _httpClient.SendAsync(request);
                if (options.Verbose) _logger.LogInformation("Response status: {Status}", response.StatusCode);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"API call failed with status {response.StatusCode}");

                result.Success = true;


            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, $"Error rebuilding index for {result.Identifier}");
            }

            return result;
        }
    }

    public class RebuildIndexOptions
    {
        public string RepoUrl { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore]
        public string ApiKey { get; set; } = string.Empty;
        public string? ContentPath { get; set; }
        public string? ContentId { get; set; }
        public string? FilePath { get; set; }
        public bool Recursive { get; set; }
        public string RebuildLevel { get; set; } = "IndexOnly";
        public bool Verbose { get; set; }
    }

    public class RebuildIndexResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public RebuildIndexOptions Options { get; set; } = new();
        public int ItemsProcessed { get; set; }
        public int SuccessfulItems { get; set; }
        public int FailedItems { get; set; }
        public List<RebuildItemResult>? ItemResults { get; set; }
    }

    public class RebuildItemResult
    {
        public string Identifier { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
