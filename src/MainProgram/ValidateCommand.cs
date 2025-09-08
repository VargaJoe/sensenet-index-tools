using System.CommandLine;
using Lucene.Net.Index;
using Lucene.Net.Store;
using System.Collections.Generic;
using System.Linq;
using IODirectory = System.IO.Directory;
using System.IO;
using System;
using Lucene.Net.Documents;
using Lucene.Net.Search;

namespace SenseNetIndexTools
{
    public class ValidateCommand
    {
        public static Command Create()
        {
            var validateCommand = new Command("validate", "Validate the structure and integrity of a Lucene index");

            // Add path option same as the other commands
            var pathOption = new Option<string>(
                name: "--path", 
                description: "Path to the Lucene index directory");
            pathOption.IsRequired = true;

            // Option for detailed validation
            var detailedOption = new Option<bool>(
                name: "--detailed",
                description: "Perform a detailed validation with comprehensive checks",
                getDefaultValue: () => false);

            // Option for output file
            var outputOption = new Option<string?>(
                name: "--output",
                description: "Path to save the validation report to a file");

            // Option for report format (summary, detailed, full)
            var reportFormatOption = new Option<string>(
                name: "--report-format",
                description: "Level of detail for the report: 'summary', 'detailed', or 'full' (default: 'summary')",
                getDefaultValue: () => "summary");
            reportFormatOption.FromAmong("summary", "detailed", "full");

            // Option for output format (md/html)
            var formatOption = new Option<string>(
                name: "--format",
                description: "Output format: 'md' (Markdown) or 'html' (HTML format suitable for web viewing)",
                getDefaultValue: () => "md");
            formatOption.FromAmong("md", "html");

            // Option for backup
            var backupOption = new Option<bool>(
                name: "--backup",
                description: "Create a backup of the index before validation",
                getDefaultValue: () => true);

            // Option for backup path
            var backupPathOption = new Option<string?>(
                name: "--backup-path",
                description: "Custom path for storing backups");

            // Option for sample size
            var sampleSizeOption = new Option<int?>(
                name: "--sample-size",
                description: "Number of documents to sample for validation. Use 0 for full validation.",
                getDefaultValue: () => 10);

            // Option to specify required fields
            var requiredFieldsOption = new Option<string?>(
                name: "--required-fields",
                description: "JSON array of required fields. Overrides default SenseNet fields. Example: '[\"Id\",\"Path\"]'");

            // Add all options to the command
            validateCommand.AddOption(pathOption);
            validateCommand.AddOption(detailedOption);
            validateCommand.AddOption(outputOption);
            validateCommand.AddOption(reportFormatOption);
            validateCommand.AddOption(formatOption);
            validateCommand.AddOption(backupOption);
            validateCommand.AddOption(backupPathOption);
            validateCommand.AddOption(sampleSizeOption);
            validateCommand.AddOption(requiredFieldsOption);

            // Set the handler for the command
            // Use a custom binding to support more than 8 parameters
            validateCommand.SetHandler(async (System.CommandLine.Invocation.InvocationContext context) =>
            {
                var path = context.ParseResult.GetValueForOption(pathOption)!;
                var detailed = context.ParseResult.GetValueForOption(detailedOption);
                var output = context.ParseResult.GetValueForOption(outputOption);
                var backup = context.ParseResult.GetValueForOption(backupOption);
                var backupPath = context.ParseResult.GetValueForOption(backupPathOption);
                var sampleSize = context.ParseResult.GetValueForOption(sampleSizeOption);
                var requiredFields = context.ParseResult.GetValueForOption(requiredFieldsOption);
                var reportFormat = context.ParseResult.GetValueForOption(reportFormatOption) ?? "summary";
                var format = context.ParseResult.GetValueForOption(formatOption) ?? "md";

                try
                {
                    // Verify this is a valid Lucene index
                    if (!IndexUtilities.IsValidLuceneIndex(path))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {path}");
                        Environment.Exit(1);
                        await Task.CompletedTask; return;
                    }

                    if (backup)
                    {
                        IndexUtilities.CreateBackup(path, backupPath);
                    }

                    string[]? customFields = null;
                    if (!string.IsNullOrEmpty(requiredFields))
                    {
                        try
                        {
                            customFields = System.Text.Json.JsonSerializer.Deserialize<string[]>(requiredFields);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Failed to parse required fields JSON: {ex.Message}");
                            Environment.Exit(1);
                            await Task.CompletedTask; return;
                        }
                    }

                    var validator = new IndexValidator(path)
                    {
                        SampleSize = sampleSize ?? 10
                    };
                    if (customFields != null)
                    {
                        validator.RequiredFields = customFields;
                    }
                    var results = validator.Validate(detailed);

                    var errorCount = results.Count(r => r.Severity == ValidationSeverity.Error);
                    var warningCount = results.Count(r => r.Severity == ValidationSeverity.Warning);
                    Console.WriteLine($"\nValidation completed with {errorCount} errors and {warningCount} warnings.");

                    foreach (var result in results.OrderByDescending(r => r.Severity))
                    {
                        var color = result.Severity == ValidationSeverity.Error ? ConsoleColor.Red :
                                   result.Severity == ValidationSeverity.Warning ? ConsoleColor.Yellow :
                                   ConsoleColor.Green;
                        var originalColor = Console.ForegroundColor;
                        Console.ForegroundColor = color;
                        Console.WriteLine($"[{result.Severity}] {result.Message}");
                        Console.ForegroundColor = originalColor;
                        if (!string.IsNullOrEmpty(result.Details) && reportFormat != "summary")
                        {
                            Console.WriteLine($"  Details: {result.Details}");
                        }
                    }

                    if (!string.IsNullOrEmpty(output))
                    {
                        if (format.ToLower() == "html")
                            SaveValidationReportHtml(results, output, reportFormat);
                        else
                            SaveValidationReportMarkdown(results, output, reportFormat);
                        Console.WriteLine($"\nValidation report saved to: {output}");
                    }

                    if (results.Any(r => r.Severity == ValidationSeverity.Error))
                    {
                        Environment.Exit(1);
                        await Task.CompletedTask; return;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error during index validation: {ex.Message}");
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    Environment.Exit(1);
                    await Task.CompletedTask; return;
                }
                await Task.CompletedTask; return;
            });

            return validateCommand;
        }

        // Old SaveValidationReport replaced by two new methods:
        private static void SaveValidationReportMarkdown(IEnumerable<ValidationResult> results, string outputPath, string reportFormat)
        {
            using (var writer = new StreamWriter(outputPath, false))
            {
                writer.WriteLine("# SenseNet Index Validation Report");
                writer.WriteLine($"Generated: {DateTime.Now}");
                writer.WriteLine();
                writer.WriteLine("## Summary");
                writer.WriteLine($"- Errors: {results.Count(r => r.Severity == ValidationSeverity.Error)}");
                writer.WriteLine($"- Warnings: {results.Count(r => r.Severity == ValidationSeverity.Warning)}");
                writer.WriteLine($"- Info: {results.Count(r => r.Severity == ValidationSeverity.Info)}");
                writer.WriteLine();
                if (reportFormat != "summary")
                {
                    // Field info
                    var fieldInfo = results.FirstOrDefault(r => r.Message == "Complete list of index fields");
                    if (fieldInfo != null)
                    {
                        writer.WriteLine("## Index Fields");
                        writer.WriteLine("All fields present in the index:");
                        writer.WriteLine("```");
                        writer.WriteLine(fieldInfo.Details);
                        writer.WriteLine("```");
                        writer.WriteLine();
                    }
                }
                writer.WriteLine("## Validation Details");
                foreach (var result in results.OrderByDescending(r => r.Severity))
                {
                    if (result.Message == "Complete list of index fields" && reportFormat != "full")
                        continue;
                    writer.WriteLine($"### [{result.Severity}] {result.Message}");
                    if (!string.IsNullOrEmpty(result.Details) && reportFormat != "summary")
                    {
                        writer.WriteLine($"Details: {result.Details}");
                    }
                    writer.WriteLine();
                }
            }
        }
        private static void SaveValidationReportHtml(IEnumerable<ValidationResult> results, string outputPath, string reportFormat)
        {
            using (var writer = new StreamWriter(outputPath, false))
            {
                writer.WriteLine("<!DOCTYPE html>");
                writer.WriteLine("<html lang=\"en\">");
                writer.WriteLine("<head>");
                writer.WriteLine("<meta charset=\"utf-8\">");
                writer.WriteLine("<title>SenseNet Index Validation Report</title>");
                writer.WriteLine("<style>");
                writer.WriteLine(@"body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Arial, sans-serif; background: #fafbfc; color: #333; max-width: 900px; margin: 0 auto; padding: 24px; } h1, h2 { border-bottom: 1px solid #eee; padding-bottom: 0.3em; margin-top: 1.5em; color: #24292e; } .summary { background: #f6f8fa; padding: 20px; border-radius: 6px; margin-bottom: 30px; border-left: 4px solid #0366d6; } .error { color: #d73a49; font-weight: bold; } .warning { color: #f66a0a; font-weight: bold; } .info { color: #0366d6; font-weight: bold; } .details { margin-left: 1em; color: #555; font-size: 0.97em; } table { border-collapse: collapse; width: 100%; margin: 1em 0; font-size: 14px; } th, td { padding: 10px 8px; border-bottom: 1px solid #ddd; } th { background: #f6f8fa; font-weight: 600; color: #586069; } tr:hover { background-color: #f6f8fa; } .field-list { background: #f8f9fa; padding: 10px; border-radius: 4px; font-family: monospace; font-size: 13px; } .section { margin-bottom: 2em; }");
                writer.WriteLine("</style>");
                writer.WriteLine("</head><body>");
                writer.WriteLine("<h1>SenseNet Index Validation Report</h1>");
                writer.WriteLine($"<div class='summary'><h2>Summary</h2><ul><li><span class='error'>Errors:</span> {results.Count(r => r.Severity == ValidationSeverity.Error)}</li><li><span class='warning'>Warnings:</span> {results.Count(r => r.Severity == ValidationSeverity.Warning)}</li><li><span class='info'>Info:</span> {results.Count(r => r.Severity == ValidationSeverity.Info)}</li></ul></div>");
                if (reportFormat != "summary")
                {
                    var fieldInfo = results.FirstOrDefault(r => r.Message == "Complete list of index fields");
                    if (fieldInfo != null)
                    {
                        writer.WriteLine("<div class='section'><h2>Index Fields</h2><div class='field-list'>");
                        writer.WriteLine(fieldInfo.Details.Replace("\n", "<br>"));
                        writer.WriteLine("</div></div>");
                    }
                }
                writer.WriteLine("<div class='section'><h2>Validation Details</h2><table><thead><tr><th>Severity</th><th>Message</th>");
                if (reportFormat != "summary") writer.WriteLine("<th>Details</th>");
                writer.WriteLine("</tr></thead><tbody>");
                foreach (var result in results.OrderByDescending(r => r.Severity))
                {
                    if (result.Message == "Complete list of index fields" && reportFormat != "full")
                        continue;
                    var sevClass = result.Severity == ValidationSeverity.Error ? "error" : result.Severity == ValidationSeverity.Warning ? "warning" : "info";
                    writer.Write($"<tr><td class='{sevClass}'>{result.Severity}</td><td>{System.Net.WebUtility.HtmlEncode(result.Message)}</td>");
                    if (reportFormat != "summary")
                        writer.Write($"<td class='details'>{System.Net.WebUtility.HtmlEncode(result.Details)}</td>");
                    writer.WriteLine("</tr>");
                }
                writer.WriteLine("</tbody></table></div>");
                writer.WriteLine($"<footer style='margin-top:2em;font-size:13px;color:#888;'>Generated: {DateTime.Now}</footer>");
                writer.WriteLine("</body></html>");
            }
        }
    }
}
