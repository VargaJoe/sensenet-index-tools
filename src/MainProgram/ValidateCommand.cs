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

            // Option for backup
            var backupOption = new Option<bool>(
                name: "--backup",
                description: "Create a backup of the index before validation",
                getDefaultValue: () => true);

            // Option for backup path
            var backupPathOption = new Option<string?>(
                name: "--backup-path",
                description: "Custom path for storing backups");

            // Add all options to the command
            validateCommand.AddOption(pathOption);
            validateCommand.AddOption(detailedOption);
            validateCommand.AddOption(outputOption);
            validateCommand.AddOption(backupOption);
            validateCommand.AddOption(backupPathOption);

            // Set the handler for the command
            validateCommand.SetHandler(async (string path, bool detailed, string? output, bool backup, string? backupPath) =>
            {                try
                {
                    // Verify this is a valid Lucene index
                    if (!Program.IsValidLuceneIndex(path))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {path}");
                        Environment.Exit(1);
                        return;
                    }

                    if (backup)
                    {
                        Program.CreateBackup(path, backupPath);
                    }

                    Console.WriteLine($"Validating index at: {path}");

                    // Run validation logic
                    var validator = new IndexValidator(path);
                    var results = validator.Validate(detailed);

                    // Output results to console
                    Console.WriteLine($"\nValidation completed with {results.Count(r => r.Severity == ValidationSeverity.Error)} errors and {results.Count(r => r.Severity == ValidationSeverity.Warning)} warnings.");
                    
                    foreach (var result in results.OrderByDescending(r => r.Severity))
                    {
                        var color = result.Severity == ValidationSeverity.Error ? ConsoleColor.Red : 
                                   result.Severity == ValidationSeverity.Warning ? ConsoleColor.Yellow : 
                                   ConsoleColor.Green;
                                   
                        var originalColor = Console.ForegroundColor;
                        Console.ForegroundColor = color;
                        Console.WriteLine($"[{result.Severity}] {result.Message}");
                        Console.ForegroundColor = originalColor;
                        
                        // Show details if provided
                        if (!string.IsNullOrEmpty(result.Details))
                        {
                            Console.WriteLine($"  Details: {result.Details}");
                        }
                    }

                    // Save to file if output option provided
                    if (!string.IsNullOrEmpty(output))
                    {
                        SaveValidationReport(results, output);
                        Console.WriteLine($"\nValidation report saved to: {output}");
                    }

                    // Exit with error code if validation failed
                    if (results.Any(r => r.Severity == ValidationSeverity.Error))
                    {
                        Environment.Exit(1);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error during index validation: {ex.Message}");
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    Environment.Exit(1);
                }
            }, pathOption, detailedOption, outputOption, backupOption, backupPathOption);

            return validateCommand;
        }

        private static void SaveValidationReport(IEnumerable<ValidationResult> results, string outputPath)
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

                // First write field-related information
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

                // Then write all other validation details
                writer.WriteLine("## Validation Details");
                foreach (var result in results.OrderByDescending(r => r.Severity))
                {
                    // Skip the field list as we already displayed it
                    if (result.Message == "Complete list of index fields")
                        continue;

                    writer.WriteLine($"### [{result.Severity}] {result.Message}");
                    if (!string.IsNullOrEmpty(result.Details))
                    {
                        writer.WriteLine($"Details: {result.Details}");
                    }
                    writer.WriteLine();
                }
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

        public IndexValidator(string indexPath)
        {
            _indexPath = indexPath;
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
            var segmentsFiles = IODirectory.GetFiles(_indexPath)
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

                            // Check for common SenseNet fields
                            var senseNetFields = new[] { 
                                "Id", "VersionId", "NodeTimestamp", "VersionTimestamp", 
                                "Path", "Version", "IsLastPublic", "IsLastDraft" 
                            };
                            
                            var missingSenseNetFields = senseNetFields
                                .Where(f => !fields.Contains(f))
                                .ToList();
                            
                            if (missingSenseNetFields.Any())
                            {
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Warning,
                                    "Missing SenseNet-specific fields",
                                    $"Missing fields: {string.Join(", ", missingSenseNetFields)}"
                                ));
                            }
                            else
                            {
                                results.Add(new ValidationResult(
                                    ValidationSeverity.Info,
                                    "All SenseNet-specific fields are present",
                                    $"Found all required fields: {string.Join(", ", senseNetFields)}"
                                ));
                            }

                            // Check for commit fields
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
            var missingNodeIdContentTypes = new Dictionary<string, int>();

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

                            // Sample a few documents to check core field integrity
                            var docSampleSize = Math.Min(10, reader.NumDocs());
                            int validDocs = 0;
                            int invalidDocs = 0;
                            var maxDoc = reader.MaxDoc();
                            
                            // Space out the sampling evenly through the index
                            var samplingInterval = maxDoc > docSampleSize ? maxDoc / docSampleSize : 1;
                            
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
                                    if (string.IsNullOrEmpty(doc.Get("VersionId")))
                                    {
                                        isValid = false;
                                        invalidReason = "Missing VersionId";
                                    }
                                    else if (string.IsNullOrEmpty(doc.Get("Id")))  // Changed from NodeId to Id
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
                                            if (!missingNodeIdContentTypes.ContainsKey(typeKey))
                                                missingNodeIdContentTypes[typeKey] = 0;
                                            missingNodeIdContentTypes[typeKey]++;
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

                                // Add content type breakdown if we found any documents with missing NodeId
                                if (missingNodeIdContentTypes.Any())
                                {
                                    var contentTypeBreakdown = string.Join("\n",
                                        missingNodeIdContentTypes.OrderByDescending(kvp => kvp.Value)
                                            .Select(kvp => $"- {kvp.Key}: {kvp.Value} document(s)"));

                                    results.Add(new ValidationResult(
                                        ValidationSeverity.Warning,
                                        "Content type breakdown of documents missing NodeId",
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
                // Get all files in the index directory
                var allFiles = IODirectory.GetFiles(_indexPath).Select(Path.GetFileName).ToList();
                
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
