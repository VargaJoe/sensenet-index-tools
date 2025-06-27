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
                    if (!Program.IsValidLuceneIndex(path))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {path}");
                        Environment.Exit(1);
                        await Task.CompletedTask; return;
                    }

                    if (backup)
                    {
                        Program.CreateBackup(path, backupPath);
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

    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public class ValidationResult
    {
        public ValidationSeverity Severity { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }

        public ValidationResult(ValidationSeverity severity, string message, string details = "")
        {
            Severity = severity;
            Message = message;
            Details = details;
        }
    }

    public class IndexValidator
    {
        private readonly string _indexPath;

        // Configuration properties
        public int SampleSize { get; set; } = 10;
        
        // Default SenseNet fields that should be present in the index
        public static readonly string[] DefaultSenseNetFields = new[] { 
            "Id", "VersionId", "NodeTimestamp", "VersionTimestamp", 
            "Path", "Version", "IsLastPublic", "IsLastDraft" 
        };
        
        private string[] _requiredFields;
        
        public string[] RequiredFields
        {
            get => _requiredFields;
            set => _requiredFields = value;
        }

        public IndexValidator(string indexPath)
        {
            _indexPath = indexPath;
            _requiredFields = DefaultSenseNetFields;  // Use defaults unless overridden
        }

        public IEnumerable<ValidationResult> Validate(bool detailed)
        {
            var results = new List<ValidationResult>();

            // Basic structure validation
            results.AddRange(ValidateBasicStructure());

            // Check for segments file
            results.AddRange(ValidateSegmentsFile());

            // Check index lock status
            results.AddRange(ValidateIndexLock());

            // Check index reader opens successfully
            results.AddRange(ValidateIndexReaderOpens());

            // Check commit data integrity
            results.AddRange(ValidateCommitData());

            // Check for SenseNet specific fields
            results.AddRange(ValidateSenseNetFields());

            // If detailed validation requested, perform deeper checks
            if (detailed)
            {
                // Check document integrity
                results.AddRange(ValidateDocumentIntegrity());

                // Check segments integrity
                results.AddRange(ValidateSegmentsIntegrity());
                
                // Check for orphaned files
                results.AddRange(ValidateForOrphanedFiles());
            }

            return results;
        }

        private IEnumerable<ValidationResult> ValidateBasicStructure()
        {
            var results = new List<ValidationResult>();

            // Check if directory exists
            if (!IODirectory.Exists(_indexPath))
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Error,
                    "Index directory does not exist",
                    $"Directory path: {_indexPath}"
                ));
                return results;
            }

            // Check if there are files in the directory
            var files = IODirectory.GetFiles(_indexPath);
            if (files.Length == 0)
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Error,
                    "Index directory is empty",
                    $"Directory path: {_indexPath}"
                ));
                return results;
            }

            results.Add(new ValidationResult(
                ValidationSeverity.Info,
                "Index directory structure verified",
                $"Directory contains {files.Length} files"
            ));

            return results;
        }

        private IEnumerable<ValidationResult> ValidateSegmentsFile()
        {
            var results = new List<ValidationResult>();

            // Check for segments file (segments_N)
            var files = IODirectory.GetFiles(_indexPath) ?? Array.Empty<string>();
            var segmentsFiles = files
                .Where(f => Path.GetFileName(f).StartsWith("segments"))
                .ToList();

            if (segmentsFiles.Count == 0)
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Error,
                    "No segments file found in the index",
                    "A valid Lucene index should contain a segments_N file"
                ));
                return results;
            }

            var currentSegmentFile = segmentsFiles
                .OrderByDescending(f => Path.GetFileName(f))
                .First();

            results.Add(new ValidationResult(
                ValidationSeverity.Info,
                "Segments file found",
                $"Current segments file: {Path.GetFileName(currentSegmentFile)}"
            ));

            if (segmentsFiles.Count > 1)
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Info,
                    "Multiple segments files found",
                    $"Found {segmentsFiles.Count} segments files, current is {Path.GetFileName(currentSegmentFile)}"
                ));
            }

            return results;
        }

        private IEnumerable<ValidationResult> ValidateIndexLock()
        {
            var results = new List<ValidationResult>();

            try
            {
                using (var directory = FSDirectory.Open(new DirectoryInfo(_indexPath)))
                {
                    if (IndexWriter.IsLocked(directory))
                    {
                        results.Add(new ValidationResult(
                            ValidationSeverity.Warning,
                            "Index is locked",
                            "The index is currently locked which could indicate a crash or active write operation"
                        ));
                    }
                    else
                    {
                        results.Add(new ValidationResult(
                            ValidationSeverity.Info,
                            "Index is not locked",
                            "The index is not currently locked"
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Error,
                    "Failed to check index lock status",
                    ex.Message
                ));
            }

            return results;
        }

        private IEnumerable<ValidationResult> ValidateIndexReaderOpens()
        {
            var results = new List<ValidationResult>();

            try
            {
                using (var directory = FSDirectory.Open(new DirectoryInfo(_indexPath)))
                {
                    if (!IndexReader.IndexExists(directory))
                    {
                        results.Add(new ValidationResult(
                            ValidationSeverity.Error,
                            "Index format is invalid",
                            "IndexReader.IndexExists returned false"
                        ));
                        return results;
                    }

                    using (var reader = IndexReader.Open(directory, true))
                    {
                        results.Add(new ValidationResult(
                            ValidationSeverity.Info,
                            "Successfully opened index with IndexReader",
                            $"Index contains {reader.NumDocs()} documents, maximum doc ID: {reader.MaxDoc()}"
                        ));

                        if (reader.HasDeletions())
                        {
                            var deletedCount = reader.MaxDoc() - reader.NumDocs();
                            results.Add(new ValidationResult(
                                ValidationSeverity.Info,
                                "Index contains deleted documents",
                                $"Number of deleted documents: {deletedCount} ({(deletedCount * 100.0 / reader.MaxDoc()):F1}% of total)"
                            ));

                            // If high percentage of deleted docs, suggest optimization
                            if (deletedCount * 100.0 / reader.MaxDoc() > 20)
                            {
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Warning,
                                    "High percentage of deleted documents",
                                    "Consider optimizing the index to reclaim space"
                                ));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Error,
                    "Failed to open index with IndexReader",
                    ex.Message
                ));
            }

            return results;
        }

        private IEnumerable<ValidationResult> ValidateCommitData()
        {
            var results = new List<ValidationResult>();

            try
            {
                using (var directory = FSDirectory.Open(new DirectoryInfo(_indexPath)))
                {
                    if (IndexReader.IndexExists(directory))
                    {
                        using (var reader = IndexReader.Open(directory, true))
                        {
                            var commitUserData = reader.GetCommitUserData();
                            if (commitUserData != null && commitUserData.Count > 0)
                            {
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Info,
                                    "Commit user data found",
                                    $"Commit data contains {commitUserData.Count} entries"
                                ));

                                // Check for LastActivityId
                                if (commitUserData.ContainsKey("LastActivityId"))
                                {
                                    var lastActivityId = commitUserData["LastActivityId"];
                                    if (long.TryParse(lastActivityId, out var _))
                                    {
                                        results.Add(new ValidationResult(
                                            ValidationSeverity.Info,
                                            "LastActivityId found and is valid",
                                            $"LastActivityId = {lastActivityId}"
                                        ));
                                    }
                                    else
                                    {
                                        results.Add(new ValidationResult(
                                            ValidationSeverity.Warning,
                                            "LastActivityId found but is not a valid number",
                                            $"LastActivityId = {lastActivityId}"
                                        ));
                                    }
                                }
                                else
                                {
                                    results.Add(new ValidationResult(
                                        ValidationSeverity.Warning,
                                        "LastActivityId not found in commit data",
                                        "The index may not be initialized for SenseNet or may be from another system"
                                    ));
                                }

                                // Log all commit data for review
                                var commitDataDetails = string.Join(", ", 
                                    commitUserData.Select(kv => $"{kv.Key}={kv.Value}"));
                                
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Info,
                                    "Commit user data details",
                                    commitDataDetails
                                ));
                            }
                            else
                            {
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Warning,
                                    "No commit user data found",
                                    "The index may not have been properly initialized"
                                ));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Error,
                    "Failed to read commit user data",
                    ex.Message
                ));
            }

            return results;
        }

        private IEnumerable<ValidationResult> ValidateSenseNetFields()
        {
            var results = new List<ValidationResult>();

            try
            {
                using (var directory = FSDirectory.Open(new DirectoryInfo(_indexPath)))
                {
                    if (IndexReader.IndexExists(directory))
                    {
                        using (var reader = IndexReader.Open(directory, true))
                        {
                            var fields = reader.GetFieldNames(IndexReader.FieldOption.ALL).ToList();
                            fields.Sort(); // Sort alphabetically for better readability
                            
                            // List all fields for analysis
                            var fieldsList = string.Join("\n", fields.Select(f => $"- {f}"));
                            results.Add(new ValidationResult(
                                ValidationSeverity.Info,
                                "Complete list of index fields",
                                fieldsList
                            ));

                            results.Add(new ValidationResult(
                                ValidationSeverity.Info,
                                "Index field structure",
                                $"Index contains {fields.Count} unique field names"
                            ));
                            
                            // Check for required fields
                            var missingFields = _requiredFields
                                .Where(f => !fields.Contains(f))
                                .ToList();

                            if (missingFields.Any())
                            {
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Warning,
                                    "Missing required fields",
                                    $"Missing fields: {string.Join(", ", missingFields)}"
                                ));
                            }
                            else
                            {
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Info,
                                    "All required fields are present",
                                    $"Found all required fields: {string.Join(", ", _requiredFields)}"
                                ));
                            }

                            // Check for commit fields (these are always required)
                            var hasCommitFields = fields.Contains("$#COMMIT") && fields.Contains("$#DATA");
                            if (hasCommitFields)
                            {
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Info,
                                    "SenseNet commit fields are present",
                                    "Found both $#COMMIT and $#DATA fields"
                                ));
                            }
                            else
                            {
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Warning,
                                    "SenseNet commit fields are missing",
                                    "Missing $#COMMIT and/or $#DATA fields"
                                ));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Error,
                    "Failed to validate SenseNet field structure",
                    ex.Message
                ));
            }

            return results;
        }        private IEnumerable<ValidationResult> ValidateDocumentIntegrity()
        {
            var results = new List<ValidationResult>();
            var missingIdContentTypes = new Dictionary<string, int>();

            try
            {
                using (var directory = FSDirectory.Open(new DirectoryInfo(_indexPath)))
                {
                    if (IndexReader.IndexExists(directory))
                    {
                        using (var reader = IndexReader.Open(directory, true))
                        {
                            // Find commit documents
                            var commitTermDocs = reader.TermDocs(new Term("$#COMMIT", "$#COMMIT"));
                            int commitDocCount = 0;
                            while (commitTermDocs.Next())
                            {
                                commitDocCount++;
                            }
                            
                            results.Add(new ValidationResult(
                                ValidationSeverity.Info,
                                "Commit documents found",
                                $"Found {commitDocCount} commit document(s)"
                            ));

                            // Report sampling strategy
                            var totalDocs = reader.NumDocs();
                            var samplingStrategy = SampleSize == 0 
                                ? "Performing full validation of all documents" 
                                : $"Sampling {Math.Min(SampleSize, totalDocs)} documents out of {totalDocs} total documents";
                            
                            results.Add(new ValidationResult(
                                ValidationSeverity.Info,
                                "Validation Strategy",
                                samplingStrategy
                            ));

                            // Document validation
                            var docSampleSize = SampleSize == 0 ? totalDocs : Math.Min(SampleSize, totalDocs);
                            int validDocs = 0;
                            int invalidDocs = 0;
                            var maxDoc = reader.MaxDoc();
                            
                            // Space out the sampling evenly through the index
                            var samplingInterval = SampleSize == 0 ? 1 : 
                                maxDoc > docSampleSize ? maxDoc / docSampleSize : 1;

                            for (int i = 0; i < maxDoc && validDocs + invalidDocs < docSampleSize; i += samplingInterval)
                            {
                                if (!reader.IsDeleted(i))
                                {
                                    var doc = reader.Document(i);
                                    // Skip commit documents in sample validation
                                    if (doc.Get("$#COMMIT") == "$#COMMIT")
                                    {
                                        continue;
                                    }
                                    
                                    bool isValid = true;
                                    string invalidReason = "";
                                    
                                    // Check required fields for a SenseNet document
                                    if (string.IsNullOrEmpty(doc.Get("Id")))
                                    {
                                        isValid = false;
                                        // If Id is missing, try to identify content type
                                        var contentType = doc.Get("TypeId") ?? doc.Get("Type") ?? doc.Get("ContentType");
                                        var path = doc.Get("Path");
                                        var name = doc.Get("Name");
                                        var displayName = doc.Get("DisplayName");
                                        var extraInfo = string.Join(" | ", new[]
                                        {
                                            contentType != null ? $"Content Type: {contentType}" : null,
                                            path != null ? $"Path: {path}" : null,
                                            name != null ? $"Name: {name}" : null,
                                            displayName != null ? $"Display Name: {displayName}" : null
                                        }.Where(x => x != null));
                                        
                                        if (!string.IsNullOrEmpty(extraInfo))
                                        {
                                            invalidReason = $"Missing Id - {extraInfo}";
                                            
                                            // Track content type statistics
                                            var typeKey = contentType ?? "Unknown";
                                            if (!missingIdContentTypes.ContainsKey(typeKey))
                                                missingIdContentTypes[typeKey] = 0;
                                            missingIdContentTypes[typeKey]++;
                                        }
                                    }
                                    
                                    if (isValid)
                                    {
                                        validDocs++;
                                    }
                                    else
                                    {
                                        invalidDocs++;
                                        results.Add(new ValidationResult(
                                            ValidationSeverity.Warning,
                                            $"Document at position {i} has integrity issues",
                                            invalidReason
                                        ));
                                    }
                                }
                            }

                            if (invalidDocs == 0 && validDocs > 0)
                            {
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Info,
                                    "Document integrity check passed",
                                    $"Sampled {validDocs} documents, all have required fields"
                                ));
                            }
                            else if (invalidDocs > 0)
                            {
                                // Add summary of integrity issues
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Warning,
                                    "Some documents have integrity issues",
                                    $"Found {invalidDocs} document(s) with issues out of {validDocs + invalidDocs} sampled"
                                ));

                                // Add content type breakdown if we found any documents with missing Id
                                if (missingIdContentTypes.Any())
                                {
                                    var contentTypeBreakdown = string.Join("\n",
                                        missingIdContentTypes.OrderByDescending(kvp => kvp.Value)
                                            .Select(kvp => $"- {kvp.Key}: {kvp.Value} document(s)"));

                                    results.Add(new ValidationResult(
                                        ValidationSeverity.Warning,
                                        "Content type breakdown of documents missing Id",
                                        contentTypeBreakdown
                                    ));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Error,
                    "Failed to validate document integrity",
                    ex.Message
                ));
            }

            return results;
        }

        private IEnumerable<ValidationResult> ValidateSegmentsIntegrity()
        {
            var results = new List<ValidationResult>();

            try
            {
                using (var directory = FSDirectory.Open(new DirectoryInfo(_indexPath)))
                {
                    if (IndexReader.IndexExists(directory))
                    {
                        using (var indexReader = IndexReader.Open(directory, true))
                        {
                            // Cast to MultiReader if possible to get segment information
                            if (indexReader is MultiReader multiReader)
                            {
                                // Get the readers
                                var segmentReaders = multiReader.GetSequentialSubReaders();
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Info,
                                    "Segment structure information",
                                    $"Index contains {segmentReaders.Length} segments"
                                ));
                                
                                // Attempt to use each segment reader
                                int validSegments = 0;
                                int invalidSegments = 0;
                                
                                for (int i = 0; i < segmentReaders.Length; i++)
                                {
                                    var segmentReader = segmentReaders[i];
                                    try
                                    {
                                        var numDocs = segmentReader.NumDocs();
                                        var maxDoc = segmentReader.MaxDoc();
                                        validSegments++;
                                    }
                                    catch (Exception ex)
                                    {
                                        invalidSegments++;
                                        results.Add(new ValidationResult(
                                            ValidationSeverity.Error,
                                            $"Segment {i} appears to be corrupted",
                                            ex.Message
                                        ));
                                    }
                                }
                                
                                if (invalidSegments == 0)
                                {
                                    results.Add(new ValidationResult(
                                        ValidationSeverity.Info,
                                        "All segments appear to be valid",
                                        $"Successfully verified {validSegments} segments"
                                    ));
                                }
                                else
                                {
                                    results.Add(new ValidationResult(
                                        ValidationSeverity.Error,
                                        "Some segments appear to be corrupted",
                                        $"Found {invalidSegments} corrupted segment(s) out of {segmentReaders.Length} total"
                                    ));
                                }
                            }
                            else
                            {
                                // If it's not a MultiReader, there might be only one segment
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Info,
                                    "Index appears to have a single segment",
                                    "The index reader is not a MultiReader"
                                ));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Error,
                    "Failed to validate segments integrity",
                    ex.Message
                ));
            }
            
            return results;
        }

        private IEnumerable<ValidationResult> ValidateForOrphanedFiles()
        {
            var results = new List<ValidationResult>();
            try
            {
                var allFiles = IODirectory.GetFiles(_indexPath)?.Select(f => Path.GetFileName(f) ?? string.Empty).Where(f => !string.IsNullOrEmpty(f)).ToList() ?? new List<string>();
                // Identify known Lucene file patterns
                var segmentsFiles = allFiles.Where(f => f.StartsWith("segments")).ToList();
                var lockFiles = allFiles.Where(f => f == "write.lock").ToList();
                var knownExtensions = new[] { ".cfs", ".cfe", ".gen", ".fnm", ".fdt", ".fdx", ".tim", ".tis", ".frq", ".prx", ".nrm", ".tvx", ".tvd", ".tvf", ".del" };
                
                var knownPatternFiles = allFiles
                    .Where(f => knownExtensions.Any(ext => f.EndsWith(ext)))
                    .ToList();
                
                var potentialOrphans = allFiles
                    .Except(segmentsFiles)
                    .Except(lockFiles)
                    .Except(knownPatternFiles)
                    .ToList();
                
                if (potentialOrphans.Any())
                {
                    results.Add(new ValidationResult(
                        ValidationSeverity.Warning,
                        "Potential orphaned files found",
                        $"Files that don't match known Lucene patterns: {string.Join(", ", potentialOrphans)}"
                    ));
                }
                else
                {
                    results.Add(new ValidationResult(
                        ValidationSeverity.Info,
                        "No orphaned files detected",
                        "All files in the directory match known Lucene file patterns"
                    ));
                }
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Error,
                    "Failed to check for orphaned files",
                    ex.Message
                ));
            }
            
            return results;
        }
    }
}
