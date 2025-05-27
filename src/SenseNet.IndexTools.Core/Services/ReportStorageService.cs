using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SenseNet.IndexTools.Core.Services
{
    /// <summary>
    /// Configuration options for the ReportStorageService
    /// </summary>
    public class ReportStorageOptions
    {
        /// <summary>
        /// Directory where reports will be stored
        /// </summary>
        public string ReportStorageDirectory { get; set; } = "Reports";
    }

    /// <summary>
    /// Service for storing and retrieving operation reports
    /// </summary>
    public class ReportStorageService
    {
        private readonly ILogger<ReportStorageService> _logger;
        private readonly ReportStorageOptions _options;

        public ReportStorageService(
            ILogger<ReportStorageService> logger,
            IOptions<ReportStorageOptions> options)
        {
            _logger = logger;
            _options = options.Value;

            // Ensure the report directory exists
            Directory.CreateDirectory(_options.ReportStorageDirectory);
        }

        /// <summary>
        /// Metadata about a stored report
        /// </summary>
        public class ReportMetadata
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string ReportType { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public string FilePath { get; set; } = string.Empty;
            public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        }

        /// <summary>
        /// Stores a report object
        /// </summary>
        /// <typeparam name="T">Type of the report object</typeparam>
        /// <param name="reportType">Type of report (e.g. "validation", "subtree-check")</param>
        /// <param name="title">User-friendly title for the report</param>
        /// <param name="reportObject">The report object to store</param>
        /// <param name="parameters">Optional parameters describing the operation</param>
        /// <returns>Metadata about the stored report</returns>
        public async Task<ReportMetadata> StoreReportAsync<T>(
            string reportType,
            string title,
            T reportObject,
            Dictionary<string, string>? parameters = null)
        {
            var reportId = Guid.NewGuid().ToString();
            var timestamp = DateTime.Now;
            var reportDir = Path.Combine(_options.ReportStorageDirectory, reportType);
            
            Directory.CreateDirectory(reportDir);
            
            var fileName = $"{timestamp:yyyyMMdd-HHmmss}-{reportId}.json";
            var filePath = Path.Combine(reportDir, fileName);
            
            var metadata = new ReportMetadata
            {
                Id = reportId,
                ReportType = reportType,
                Title = title,
                CreatedAt = timestamp,
                FilePath = filePath,
                Parameters = parameters ?? new Dictionary<string, string>()
            };
            
            try
            {
                // Store the report object
                var reportJson = JsonSerializer.Serialize(reportObject, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(filePath, reportJson);
                
                // Store the metadata separately
                var metadataPath = Path.Combine(reportDir, $"{timestamp:yyyyMMdd-HHmmss}-{reportId}.metadata.json");
                var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(metadataPath, metadataJson);
                
                _logger.LogInformation("Stored report {ReportId} of type {ReportType}", reportId, reportType);
                
                return metadata;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing report: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Retrieves a report by its ID
        /// </summary>
        /// <typeparam name="T">Type of the report object</typeparam>
        /// <param name="reportType">Type of report</param>
        /// <param name="reportId">ID of the report</param>
        /// <returns>The report object and its metadata</returns>
        public async Task<(T Report, ReportMetadata Metadata)> GetReportAsync<T>(string reportType, string reportId)
        {
            var reportDir = Path.Combine(_options.ReportStorageDirectory, reportType);
            var files = Directory.GetFiles(reportDir, $"*-{reportId}.json");
            
            if (files.Length == 0)
            {
                _logger.LogWarning("Report {ReportId} of type {ReportType} not found", reportId, reportType);
                throw new FileNotFoundException($"Report {reportId} of type {reportType} not found");
            }
            
            var reportPath = files[0];
            var metadataPath = Path.Combine(reportDir, $"{Path.GetFileNameWithoutExtension(reportPath)}.metadata.json");
            
            try
            {
                // Load the report
                var reportJson = await File.ReadAllTextAsync(reportPath);
                var report = JsonSerializer.Deserialize<T>(reportJson);
                
                // Load the metadata
                ReportMetadata metadata;
                if (File.Exists(metadataPath))
                {
                    var metadataJson = await File.ReadAllTextAsync(metadataPath);
                    metadata = JsonSerializer.Deserialize<ReportMetadata>(metadataJson) 
                        ?? new ReportMetadata { Id = reportId, ReportType = reportType, FilePath = reportPath };
                }
                else
                {
                    // Create minimal metadata if file doesn't exist
                    metadata = new ReportMetadata 
                    { 
                        Id = reportId, 
                        ReportType = reportType, 
                        FilePath = reportPath,
                        CreatedAt = File.GetCreationTime(reportPath)
                    };
                }
                
                return (report!, metadata);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving report {ReportId}: {Message}", reportId, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Gets metadata for all reports of a specific type
        /// </summary>
        /// <param name="reportType">Type of report to retrieve</param>
        /// <returns>List of report metadata</returns>
        public async Task<List<ReportMetadata>> GetReportMetadataListAsync(string reportType)
        {
            var result = new List<ReportMetadata>();
            var reportDir = Path.Combine(_options.ReportStorageDirectory, reportType);
            
            if (!Directory.Exists(reportDir))
            {
                return result;
            }
            
            var metadataFiles = Directory.GetFiles(reportDir, "*.metadata.json");
            
            foreach (var file in metadataFiles)
            {
                try
                {
                    var metadataJson = await File.ReadAllTextAsync(file);
                    var metadata = JsonSerializer.Deserialize<ReportMetadata>(metadataJson);
                    if (metadata != null)
                    {
                        result.Add(metadata);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading metadata file {File}: {Message}", file, ex.Message);
                    // Continue with other files
                }
            }
            
            // Sort by creation date, newest first
            result.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            
            return result;
        }

        /// <summary>
        /// Gets the most recent report of a specific type
        /// </summary>
        /// <typeparam name="T">Type of the report object</typeparam>
        /// <param name="reportType">Type of report to retrieve</param>
        /// <returns>The most recent report and its metadata, or null if no reports exist</returns>
        public async Task<(T? Report, ReportMetadata? Metadata)> GetMostRecentReportAsync<T>(string reportType)
        {
            var metadataList = await GetReportMetadataListAsync(reportType);
            
            if (metadataList.Count == 0)
            {
                return (default, null);
            }
            
            // Get the most recent report (already sorted)
            var mostRecent = metadataList[0];
            
            try
            {
                var reportJson = await File.ReadAllTextAsync(mostRecent.FilePath);
                var report = JsonSerializer.Deserialize<T>(reportJson);
                
                return (report!, mostRecent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving most recent report of type {ReportType}: {Message}", 
                    reportType, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Deletes a report by its ID
        /// </summary>
        /// <param name="reportType">Type of report</param>
        /// <param name="reportId">ID of the report</param>
        /// <returns>True if successful</returns>
        public async Task<bool> DeleteReportAsync(string reportType, string reportId)
        {
            var reportDir = Path.Combine(_options.ReportStorageDirectory, reportType);
            var reportFiles = Directory.GetFiles(reportDir, $"*-{reportId}*");
            
            if (reportFiles.Length == 0)
            {
                _logger.LogWarning("Report {ReportId} of type {ReportType} not found for deletion", 
                    reportId, reportType);
                return false;
            }
            
            try
            {
                foreach (var file in reportFiles)
                {
                    File.Delete(file);
                }
                
                _logger.LogInformation("Deleted report {ReportId} of type {ReportType}", reportId, reportType);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting report {ReportId}: {Message}", reportId, ex.Message);
                return false;
            }
        }
    }
}
