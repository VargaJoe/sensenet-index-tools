using System.Text.Json;
using SenseNetIndexTools;

namespace WebApp.Services;

public class IndexValidationService
{
    private readonly ILogger<IndexValidationService> _logger;

    public IndexValidationService(ILogger<IndexValidationService> logger)
    {
        _logger = logger;
    }

    public bool ValidatePath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalizedPath = Path.GetFullPath(path);
            var exists = Directory.Exists(normalizedPath);
            _logger.LogInformation("Validating index path: {Path}, Exists: {Exists}", normalizedPath, exists);
            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating path: {Path}", path);
            return false;
        }
    }

    public async Task<ValidationServiceResult> ValidateIndexAsync(ValidationOptions options)
    {
        _logger.LogInformation("Starting index validation for: {Path}", options.IndexPath);

        try
        {
            if (!ValidatePath(options.IndexPath))
            {
                throw new DirectoryNotFoundException($"Index directory not found: {options.IndexPath}");
            }

            var result = new ValidationServiceResult
            {
                IndexPath = options.IndexPath,
                StartTime = DateTime.UtcNow,
                Options = options
            };

            // Create output file path if not specified
            var outputPath = options.OutputPath;
            if (string.IsNullOrEmpty(outputPath))
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"validation_report_{timestamp}.{options.Format}";
                outputPath = Path.Combine(Path.GetTempPath(), fileName);
            }

            // Execute the validation command
            await ExecuteValidationAsync(options, outputPath);

            // Read the generated report
            if (File.Exists(outputPath))
            {
                result.ReportContent = await File.ReadAllTextAsync(outputPath);
                result.ReportPath = outputPath;
            }

            result.EndTime = DateTime.UtcNow;
            result.Success = true;
            result.Message = "Validation completed successfully";

            _logger.LogInformation("Index validation completed successfully for: {Path}", options.IndexPath);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during index validation: {Path}", options.IndexPath);
            return new ValidationServiceResult
            {
                IndexPath = options.IndexPath,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                Success = false,
                Message = $"Validation failed: {ex.Message}",
                Options = options
            };
        }
    }

    private async Task ExecuteValidationAsync(ValidationOptions options, string outputPath)
    {
        _logger.LogInformation("Executing validation with options: {Options}", JsonSerializer.Serialize(options));
        
        try
        {
            // Verify this is a valid Lucene index
            if (!IndexUtilities.IsValidLuceneIndex(options.IndexPath))
            {
                throw new InvalidOperationException($"The directory does not appear to be a valid Lucene index: {options.IndexPath}");
            }

            if (options.CreateBackup)
            {
                IndexUtilities.CreateBackup(options.IndexPath, options.BackupPath);
            }

            // Parse custom fields if provided
            string[]? customFields = null;
            if (!string.IsNullOrEmpty(options.RequiredFields))
            {
                try
                {
                    customFields = JsonSerializer.Deserialize<string[]>(options.RequiredFields);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to parse required fields JSON: {ex.Message}");
                }
            }

            // Create validator and execute validation
            var validator = new IndexValidator(options.IndexPath)
            {
                SampleSize = options.SampleSize ?? 10
            };
            
            if (customFields != null)
            {
                validator.RequiredFields = customFields;
            }
            
            var results = validator.Validate(options.Detailed);

            // Generate report based on format
            if (options.Format == "html")
            {
                await GenerateHtmlReportAsync(results, outputPath, options.ReportFormat);
            }
            else
            {
                await GenerateMarkdownReportAsync(results, outputPath, options.ReportFormat);
            }

            _logger.LogInformation("Validation completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during index validation");
            throw;
        }
    }

    private async Task GenerateMarkdownReportAsync(IEnumerable<ValidationResult> results, string outputPath, string reportFormat)
    {
        using (var writer = new StreamWriter(outputPath, false))
        {
            await writer.WriteLineAsync("# SenseNet Index Validation Report");
            await writer.WriteLineAsync($"Generated: {DateTime.Now}");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("## Summary");
            await writer.WriteLineAsync($"- Errors: {results.Count(r => r.Severity == ValidationSeverity.Error)}");
            await writer.WriteLineAsync($"- Warnings: {results.Count(r => r.Severity == ValidationSeverity.Warning)}");
            await writer.WriteLineAsync($"- Info: {results.Count(r => r.Severity == ValidationSeverity.Info)}");
            await writer.WriteLineAsync();
            
            if (reportFormat != "summary")
            {
                // Field info
                var fieldInfo = results.FirstOrDefault(r => r.Message == "Complete list of index fields");
                if (fieldInfo != null)
                {
                    await writer.WriteLineAsync("## Index Fields");
                    await writer.WriteLineAsync("All fields present in the index:");
                    await writer.WriteLineAsync("```");
                    await writer.WriteLineAsync(fieldInfo.Details);
                    await writer.WriteLineAsync("```");
                    await writer.WriteLineAsync();
                }
            }
            
            await writer.WriteLineAsync("## Validation Details");
            foreach (var result in results.OrderByDescending(r => r.Severity))
            {
                if (result.Message == "Complete list of index fields" && reportFormat != "full")
                    continue;
                await writer.WriteLineAsync($"### [{result.Severity}] {result.Message}");
                if (!string.IsNullOrEmpty(result.Details) && reportFormat != "summary")
                {
                    await writer.WriteLineAsync($"Details: {result.Details}");
                }
                await writer.WriteLineAsync();
            }
        }
    }

    private async Task GenerateHtmlReportAsync(IEnumerable<ValidationResult> results, string outputPath, string reportFormat)
    {
        using (var writer = new StreamWriter(outputPath, false))
        {
            await writer.WriteLineAsync("<!DOCTYPE html>");
            await writer.WriteLineAsync("<html lang=\"en\">");
            await writer.WriteLineAsync("<head>");
            await writer.WriteLineAsync("<meta charset=\"utf-8\">");
            await writer.WriteLineAsync("<title>SenseNet Index Validation Report</title>");
            await writer.WriteLineAsync("<style>");
            await writer.WriteLineAsync(@"body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Arial, sans-serif; background: #fafbfc; color: #333; max-width: 900px; margin: 0 auto; padding: 24px; } h1, h2 { border-bottom: 1px solid #eee; padding-bottom: 0.3em; margin-top: 1.5em; color: #24292e; } .summary { background: #f6f8fa; padding: 20px; border-radius: 6px; margin-bottom: 30px; border-left: 4px solid #0366d6; } .error { color: #d73a49; font-weight: bold; } .warning { color: #f66a0a; font-weight: bold; } .info { color: #0366d6; font-weight: bold; } .details { margin-left: 1em; color: #555; font-size: 0.97em; } table { border-collapse: collapse; width: 100%; margin: 1em 0; font-size: 14px; } th, td { padding: 10px 8px; border-bottom: 1px solid #ddd; } th { background: #f6f8fa; font-weight: 600; color: #586069; } tr:hover { background-color: #f6f8fa; } .field-list { background: #f8f9fa; padding: 10px; border-radius: 4px; font-family: monospace; font-size: 13px; } .section { margin-bottom: 2em; }");
            await writer.WriteLineAsync("</style>");
            await writer.WriteLineAsync("</head><body>");
            await writer.WriteLineAsync("<h1>SenseNet Index Validation Report</h1>");
            await writer.WriteLineAsync($"<div class='summary'><h2>Summary</h2><ul><li><span class='error'>Errors:</span> {results.Count(r => r.Severity == ValidationSeverity.Error)}</li><li><span class='warning'>Warnings:</span> {results.Count(r => r.Severity == ValidationSeverity.Warning)}</li><li><span class='info'>Info:</span> {results.Count(r => r.Severity == ValidationSeverity.Info)}</li></ul></div>");
            
            if (reportFormat != "summary")
            {
                var fieldInfo = results.FirstOrDefault(r => r.Message == "Complete list of index fields");
                if (fieldInfo != null)
                {
                    await writer.WriteLineAsync("<div class='section'><h2>Index Fields</h2><div class='field-list'>");
                    await writer.WriteLineAsync(fieldInfo.Details.Replace("\n", "<br>"));
                    await writer.WriteLineAsync("</div></div>");
                }
            }
            
            await writer.WriteLineAsync("<div class='section'><h2>Validation Details</h2><table><thead><tr><th>Severity</th><th>Message</th>");
            if (reportFormat != "summary") await writer.WriteLineAsync("<th>Details</th>");
            await writer.WriteLineAsync("</tr></thead><tbody>");
            
            foreach (var result in results.OrderByDescending(r => r.Severity))
            {
                if (result.Message == "Complete list of index fields" && reportFormat != "full")
                    continue;
                var sevClass = result.Severity == ValidationSeverity.Error ? "error" : result.Severity == ValidationSeverity.Warning ? "warning" : "info";
                await writer.WriteAsync($"<tr><td class='{sevClass}'>{result.Severity}</td><td>{System.Net.WebUtility.HtmlEncode(result.Message)}</td>");
                if (reportFormat != "summary")
                    await writer.WriteAsync($"<td class='details'>{System.Net.WebUtility.HtmlEncode(result.Details)}</td>");
                await writer.WriteLineAsync("</tr>");
            }
            
            await writer.WriteLineAsync("</tbody></table></div>");
            await writer.WriteLineAsync($"<footer style='margin-top:2em;font-size:13px;color:#888;'>Generated: {DateTime.Now}</footer>");
            await writer.WriteLineAsync("</body></html>");
        }
    }
}

public class ValidationOptions
{
    public string IndexPath { get; set; } = string.Empty;
    public bool Detailed { get; set; } = false;
    public string? OutputPath { get; set; }
    public string ReportFormat { get; set; } = "summary";
    public string Format { get; set; } = "md";
    public bool Backup { get; set; } = true;
    public bool CreateBackup { get; set; } = true;
    public string? BackupPath { get; set; }
    public int? SampleSize { get; set; } = 10;
    public string? RequiredFields { get; set; }
}

public class ValidationServiceResult
{
    public string IndexPath { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ReportContent { get; set; }
    public string? ReportPath { get; set; }
    public ValidationOptions? Options { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}
