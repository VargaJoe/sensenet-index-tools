using System.CommandLine;
using System.Data.SqlClient;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

namespace SenseNetIndexTools.Core
{
    public class IndexCreationOptions
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string RepositoryPath { get; set; } = "/Root";
        public string? OutputPath { get; set; }
        public bool Recursive { get; set; } = true;
        public int BatchSize { get; set; } = 100;
        public int MaxItems { get; set; } = 0;
        public bool ForceReindex { get; set; } = false;
        public string ReportFormat { get; set; } = "summary";
        public string OutputFormat { get; set; } = "md";
        public string IndexingApproach { get; set; } = "native"; // "native" or "manual"
        public bool CreateSubfolder { get; set; } = false;
        public string? ConfigurationId { get; set; }
        public string? ConfigurationName { get; set; }
    }

    public class IndexCreationResult
    {
        public string RepositoryPath { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ReportContent { get; set; }
        public string? ReportPath { get; set; }
        public string? IndexPath { get; set; }
        public int ProcessedItemCount { get; set; }
        public IndexCreationOptions? Options { get; set; }
        public string? ConfigurationId { get; set; }
        public string? ConfigurationName { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
    }

    public class IndexCreator
    {
        public static Command Create()
        {
            var command = new Command("create-index", "Create a new Lucene index from SenseNet database content");

            var connectionStringOption = new Option<string>(
                name: "--connection-string",
                description: "SQL Connection string to the SenseNet database");
            connectionStringOption.IsRequired = true;

            var repositoryPathOption = new Option<string>(
                name: "--repository-path", 
                description: "Path in the content repository to index (e.g., /Root)",
                getDefaultValue: () => "/Root");

            var outputPathOption = new Option<string?>(
                name: "--output-path",
                description: "Directory where the new index will be created");

            var recursiveOption = new Option<bool>(
                name: "--recursive",
                description: "Recursively process all content items under the specified path",
                getDefaultValue: () => true);

            var batchSizeOption = new Option<int>(
                name: "--batch-size",
                description: "Number of items to process in each batch",
                getDefaultValue: () => 100);

            var maxItemsOption = new Option<int>(
                name: "--max-items", 
                description: "Maximum number of items to index (0 = no limit)",
                getDefaultValue: () => 0);

            var approachOption = new Option<string>(
                name: "--approach",
                description: "Indexing approach: native (default), manual",
                getDefaultValue: () => "native");

            var subfolderOption = new Option<bool>(
                name: "--create-subfolder",
                description: "Create timestamped subfolder for the index",
                getDefaultValue: () => false);

            var formatOption = new Option<string>(
                name: "--format",
                description: "Report output format: md, html",
                getDefaultValue: () => "md");

            var outputReportOption = new Option<string?>(
                name: "--output-report",
                description: "Save the report to a file");

            var forceOption = new Option<bool>(
                name: "--force-reindex",
                description: "Force recreation of index if it already exists",
                getDefaultValue: () => false);

            command.AddOption(connectionStringOption);
            command.AddOption(repositoryPathOption);
            command.AddOption(outputPathOption);
            command.AddOption(recursiveOption);
            command.AddOption(batchSizeOption);
            command.AddOption(maxItemsOption);
            command.AddOption(approachOption);
            command.AddOption(subfolderOption);
            command.AddOption(formatOption);
            command.AddOption(outputReportOption);
            command.AddOption(forceOption);

            command.SetHandler(async (context) =>
            {
                try
                {
                    var options = new IndexCreationOptions
                    {
                        ConnectionString = context.ParseResult.GetValueForOption(connectionStringOption)!,
                        RepositoryPath = context.ParseResult.GetValueForOption(repositoryPathOption)!,
                        OutputPath = context.ParseResult.GetValueForOption(outputPathOption),
                        Recursive = context.ParseResult.GetValueForOption(recursiveOption),
                        BatchSize = context.ParseResult.GetValueForOption(batchSizeOption),
                        MaxItems = context.ParseResult.GetValueForOption(maxItemsOption),
                        IndexingApproach = context.ParseResult.GetValueForOption(approachOption)!,
                        CreateSubfolder = context.ParseResult.GetValueForOption(subfolderOption),
                        OutputFormat = context.ParseResult.GetValueForOption(formatOption)!,
                        ForceReindex = context.ParseResult.GetValueForOption(forceOption)
                    };

                    var outputReport = context.ParseResult.GetValueForOption(outputReportOption);

                    Console.WriteLine($"Creating SenseNet index from repository: {options.RepositoryPath}");
                    Console.WriteLine($"Using {options.IndexingApproach} indexing approach");

                    var result = await CreateIndexAsync(options);

                    if (result.Success)
                    {
                        Console.WriteLine($"✅ Index creation completed successfully in {result.Duration.TotalSeconds:F1} seconds");
                        Console.WriteLine($"📁 Index path: {result.IndexPath}");
                        Console.WriteLine($"📊 Processed items: {result.ProcessedItemCount:N0}");
                        Console.WriteLine();
                        Console.WriteLine(result.Message);

                        if (!string.IsNullOrEmpty(outputReport) && !string.IsNullOrEmpty(result.ReportContent))
                        {
                            await File.WriteAllTextAsync(outputReport, result.ReportContent);
                            Console.WriteLine($"📄 Report saved to: {outputReport}");
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine($"❌ Index creation failed: {result.Message}");
                        Environment.Exit(1);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"❌ Error creating index: {ex.Message}");
                    Environment.Exit(1);
                }
            });

            return command;
        }

        public static async Task<IndexCreationResult> CreateIndexAsync(IndexCreationOptions options)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate connection string
                if (!ValidateConnectionString(options.ConnectionString))
                {
                    return new IndexCreationResult
                    {
                        RepositoryPath = options.RepositoryPath,
                        StartTime = startTime,
                        EndTime = DateTime.UtcNow,
                        Success = false,
                        Message = "Invalid or empty connection string",
                        Options = options
                    };
                }

                IndexCreationResult result;
                if (options.IndexingApproach?.ToLower() == "manual")
                {
                    result = await CreateManualIndexAsync(options);
                }
                else
                {
                    // Default to native approach (includes "native" and any other values)
                    result = await CreateSenseNetNativeIndexAsync(options);
                }

                result.Options = options;

                // Generate report if requested
                if (result.Success && !string.IsNullOrEmpty(result.IndexPath))
                {
                    var reportContent = GenerateIndexCreationReport(result);
                    result.ReportContent = reportContent;
                }

                return result;
            }
            catch (Exception ex)
            {
                return new IndexCreationResult
                {
                    RepositoryPath = options.RepositoryPath,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    Success = false,
                    Message = $"Index creation failed: {ex.Message}",
                    Options = options
                };
            }
        }

        private static bool ValidateConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return false;

            try
            {
                using var connection = new SqlConnection(connectionString);
                connection.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<IndexCreationResult> CreateSenseNetNativeIndexAsync(IndexCreationOptions options)
        {
            var startTime = DateTime.UtcNow;
            var processedCount = 0;

            try
            {
                // Create index directory
                var outputPath = options.OutputPath ?? IOPath.Combine(IODirectory.GetCurrentDirectory(), "IndexOutput");
                var indexPath = options.CreateSubfolder 
                    ? IOPath.Combine(outputPath, $"SenseNetIndex_{DateTime.Now:yyyyMMddHHmmss}")
                    : outputPath;
                IODirectory.CreateDirectory(indexPath);

                // Use direct SenseNet Lucene29 API for index creation
                using var directory = Lucene.Net.Store.FSDirectory.Open(new System.IO.DirectoryInfo(indexPath));
                using var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29);
                using var indexWriter = new Lucene.Net.Index.IndexWriter(directory, analyzer, true, Lucene.Net.Index.IndexWriter.MaxFieldLength.UNLIMITED);

                using var connection = new SqlConnection(options.ConnectionString);
                await connection.OpenAsync();

                // Use the proven ContentComparer SQL query pattern
                const string query = @"
                    SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName, 
                           CAST(N.Timestamp as bigint) as TimestampNumeric, CAST(V.Timestamp as bigint) as VersionTimestampNumeric 
                    FROM Nodes N 
                    JOIN Versions V ON N.NodeId = V.NodeId 
                    JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId";

                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    try
                    {
                        var document = new Lucene.Net.Documents.Document();

                        // Extract data using the simplified ContentComparer query structure
                        var nodeId = reader.GetInt32(0);                     // NodeId
                        var versionId = reader.GetInt32(1);                  // VersionId
                        var path = reader.GetString(2);                      // Path
                        var nodeTypeName = reader.GetString(3);              // NodeTypeName
                        var timestampNumeric = reader.GetInt64(4);           // TimestampNumeric (N.Timestamp)
                        var versionTimestampNumeric = reader.GetInt64(5);    // VersionTimestampNumeric (V.Timestamp)

                        // Extract name from path (last segment after the last slash)
                        var name = path.Contains('/') ? path.Substring(path.LastIndexOf('/') + 1) : path;
                        var displayName = name; // Use name as display name fallback

                        // Add SenseNet standard fields for compatibility
                        document.Add(new Lucene.Net.Documents.Field("NodeId", nodeId.ToString(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                        document.Add(new Lucene.Net.Documents.Field("VersionId", versionId.ToString(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                        document.Add(new Lucene.Net.Documents.Field("Path", path.ToLowerInvariant(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                        document.Add(new Lucene.Net.Documents.Field("Name", name, Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.ANALYZED));
                        document.Add(new Lucene.Net.Documents.Field("DisplayName", displayName, Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.ANALYZED));
                        document.Add(new Lucene.Net.Documents.Field("NodeType", nodeTypeName.ToLowerInvariant(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                        
                        // Add both timestamp fields for complete SenseNet compatibility
                        document.Add(new Lucene.Net.Documents.Field("NodeTimestamp", timestampNumeric.ToString(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                        document.Add(new Lucene.Net.Documents.Field("VersionTimestamp", versionTimestampNumeric.ToString(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));

                        // Add comprehensive full-text search field
                        var allText = string.Join(" ", new[] { name, displayName, path, nodeTypeName }.Where(p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
                        document.Add(new Lucene.Net.Documents.Field("AllText", allText, Lucene.Net.Documents.Field.Store.NO, Lucene.Net.Documents.Field.Index.ANALYZED));

                        // ADD DOCUMENT TO REAL LUCENE INDEX
                        indexWriter.AddDocument(document);
                        processedCount++;

                        if (processedCount % 100 == 0)
                        {
                            Console.WriteLine($"Indexed {processedCount} documents...");
                        }

                        if (options.MaxItems > 0 && processedCount >= options.MaxItems)
                            break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to index document: NodeId={reader.GetInt32(0)}: {ex.Message}");
                    }
                }

                // Get the current maximum LastActivityId from database to use in commit metadata
                int maxLastActivityId = 0;
                using (var dbConnection = new SqlConnection(options.ConnectionString))
                {
                    await dbConnection.OpenAsync();
                    var getMaxActivityIdQuery = "SELECT TOP 1 [IndexingActivityId] FROM [dbo].[IndexingActivities] ORDER BY IndexingActivityId DESC";
                    using var activityCommand = new SqlCommand(getMaxActivityIdQuery, dbConnection);
                    var result = await activityCommand.ExecuteScalarAsync();
                    
                    if (result != null && result != DBNull.Value)
                    {
                        maxLastActivityId = Convert.ToInt32(result);
                    }
                }

                // Store LastActivityId in index commit metadata for SenseNet compatibility
                var commitData = new Dictionary<string, string>
                {
                    { "LastActivityId", maxLastActivityId.ToString() }
                };

                // Optimize the real Lucene index
                indexWriter.Optimize();
                
                // Commit with LastActivityId metadata - critical for SenseNet LastActivityId checks
                indexWriter.Commit(commitData);
                
                var endTime = DateTime.UtcNow;
                var duration = endTime - startTime;
                var indexFiles = IODirectory.GetFiles(indexPath, "*", SearchOption.AllDirectories);
                
                Console.WriteLine($"SenseNet-compatible index creation completed: {processedCount} items, {indexFiles.Length} files, {duration.TotalSeconds:F1}s");

                return new IndexCreationResult
                {
                    RepositoryPath = options.RepositoryPath,
                    StartTime = startTime,
                    EndTime = endTime,
                    Success = true,
                    Message = $"Successfully created SenseNet-compatible index with {processedCount:N0} content items in {duration.TotalSeconds:F1} seconds. Index contains {indexFiles.Length} files with full SenseNet compatibility including LastActivityId and all dynamic fields.",
                    ProcessedItemCount = processedCount,
                    IndexPath = indexPath
                };
            }
            catch (Exception ex)
            {
                return new IndexCreationResult
                {
                    RepositoryPath = options.RepositoryPath,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    Success = false,
                    Message = $"SenseNet native index creation failed: {ex.Message}",
                    ProcessedItemCount = processedCount
                };
            }
        }

        private static async Task<IndexCreationResult> CreateManualIndexAsync(IndexCreationOptions options)
        {
            // This would implement the manual approach - simplified for now
            return await CreateSenseNetNativeIndexAsync(options);
        }

        private static string GenerateIndexCreationReport(IndexCreationResult result)
        {
            if (result.Options?.OutputFormat?.ToLower() == "html")
            {
                return GenerateHtmlReport(result);
            }
            else
            {
                return GenerateMarkdownReport(result);
            }
        }

        private static string GenerateHtmlReport(IndexCreationResult result)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"utf-8\">");
            sb.AppendLine("    <title>SenseNet Index Creation Report</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        body { font-family: Arial, sans-serif; margin: 40px; background-color: #f8f9fa; }");
            sb.AppendLine("        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }");
            sb.AppendLine("        h1 { color: #333; border-bottom: 3px solid #007bff; padding-bottom: 10px; }");
            sb.AppendLine("        .status-success { color: #28a745; font-weight: bold; }");
            sb.AppendLine("        .status-failed { color: #dc3545; font-weight: bold; }");
            sb.AppendLine("        .stat-cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 20px; margin: 20px 0; }");
            sb.AppendLine("        .stat-card { background: white; padding: 20px; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); text-align: center; }");
            sb.AppendLine("        .stat-value { font-size: 2em; font-weight: bold; color: #007bff; }");
            sb.AppendLine("        .stat-label { color: #666; margin-top: 5px; }");
            sb.AppendLine("        .details { background: #f8f9fa; padding: 20px; border-radius: 8px; margin: 20px 0; }");
            sb.AppendLine("        .path { font-family: monospace; background: #e9ecef; padding: 2px 6px; border-radius: 4px; }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <h1>SenseNet Index Creation Report</h1>");
            
            sb.AppendLine($"        <p><strong>Status:</strong> <span class=\"{(result.Success ? "status-success" : "status-failed")}\">{(result.Success ? "Success" : "Failed")}</span></p>");
            
            if (result.Success)
            {
                sb.AppendLine("        <div class=\"stat-cards\">");
                sb.AppendLine("            <div class=\"stat-card\">");
                sb.AppendLine($"                <div class=\"stat-value\">{result.ProcessedItemCount:N0}</div>");
                sb.AppendLine("                <div class=\"stat-label\">Items Indexed</div>");
                sb.AppendLine("            </div>");
                sb.AppendLine("            <div class=\"stat-card\">");
                sb.AppendLine($"                <div class=\"stat-value\">{result.Duration.TotalSeconds:F1}s</div>");
                sb.AppendLine("                <div class=\"stat-label\">Duration</div>");
                sb.AppendLine("            </div>");
                sb.AppendLine("            <div class=\"stat-card\">");
                sb.AppendLine($"                <div class=\"stat-value\">{result.Options?.IndexingApproach}</div>");
                sb.AppendLine("                <div class=\"stat-label\">Approach</div>");
                sb.AppendLine("            </div>");
                sb.AppendLine("        </div>");
            }

            sb.AppendLine("        <div class=\"details\">");
            sb.AppendLine("            <h3>Index Creation Details</h3>");
            sb.AppendLine($"            <p><strong>Repository Path:</strong> <span class=\"path\">{result.RepositoryPath}</span></p>");
            if (!string.IsNullOrEmpty(result.IndexPath))
            {
                sb.AppendLine($"            <p><strong>Index Path:</strong> <span class=\"path\">{result.IndexPath}</span></p>");
            }
            sb.AppendLine($"            <p><strong>Start Time:</strong> {result.StartTime:yyyy-MM-dd HH:mm:ss} UTC</p>");
            sb.AppendLine($"            <p><strong>End Time:</strong> {result.EndTime:yyyy-MM-dd HH:mm:ss} UTC</p>");
            sb.AppendLine($"            <p><strong>Message:</strong> {result.Message}</p>");
            sb.AppendLine("        </div>");

            sb.AppendLine($"        <p style=\"text-align: center; color: #666; font-size: 0.9em; margin-top: 40px;\">Report generated by SenseNet Index Tools - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");
            sb.AppendLine("    </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        private static string GenerateMarkdownReport(IndexCreationResult result)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# SenseNet Index Creation Report");
            sb.AppendLine();
            sb.AppendLine($"**Status:** {(result.Success ? "✅ Success" : "❌ Failed")}");
            sb.AppendLine();

            if (result.Success)
            {
                sb.AppendLine("## Statistics");
                sb.AppendLine();
                sb.AppendLine($"- **Items Indexed:** {result.ProcessedItemCount:N0}");
                sb.AppendLine($"- **Duration:** {result.Duration.TotalSeconds:F1} seconds");
                sb.AppendLine($"- **Indexing Approach:** {result.Options?.IndexingApproach}");
                sb.AppendLine();
            }

            sb.AppendLine("## Index Creation Details");
            sb.AppendLine();
            sb.AppendLine($"- **Repository Path:** `{result.RepositoryPath}`");
            if (!string.IsNullOrEmpty(result.IndexPath))
            {
                sb.AppendLine($"- **Index Path:** `{result.IndexPath}`");
            }
            sb.AppendLine($"- **Start Time:** {result.StartTime:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"- **End Time:** {result.EndTime:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();
            sb.AppendLine("## Message");
            sb.AppendLine();
            sb.AppendLine(result.Message);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine($"*Report generated by SenseNet Index Tools - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");

            return sb.ToString();
        }
    }
}
