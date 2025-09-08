using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using IODirectory = System.IO.Directory;

namespace SenseNetIndexTools
{
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
        }

        private IEnumerable<ValidationResult> ValidateDocumentIntegrity()
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
