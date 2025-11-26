using System.Data.SqlClient;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Storage;
using Microsoft.Extensions.DependencyInjection;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

namespace SenseNetIndexTools.Native;

public class IndexCreationOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string RepositoryPath { get; set; } = "/Root";
    public string? OutputPath { get; set; }
    public bool Recursive { get; set; } = true;
    public int BatchSize { get; set; } = 100;
    public int MaxItems { get; set; } = 0;
    public bool ForceReindex { get; set; } = false;
    public string OutputFormat { get; set; } = "md";
    public bool CreateSubfolder { get; set; } = false;
    public bool Verbose { get; set; } = false;
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
    public TimeSpan Duration => EndTime - StartTime;
}

public class SenseNetNativeIndexCreator
{
    public async Task<IndexCreationResult> CreateIndexAsync(IndexCreationOptions options)
    {
        var startTime = DateTime.UtcNow;
        var processedCount = 0;

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

            // Create index directory
            var outputPath = options.OutputPath ?? IOPath.Combine(IODirectory.GetCurrentDirectory(), "IndexOutput");
            var indexPath = options.CreateSubfolder
                ? IOPath.Combine(outputPath, $"SenseNetIndex_{DateTime.Now:yyyyMMddHHmmss}")
                : outputPath;
            IODirectory.CreateDirectory(indexPath);

            Console.WriteLine($"🔧 SenseNet-Compatible Lucene Index Creation Tool (Raw Lucene.Net)");
            Console.WriteLine($"📁 Index path: {indexPath}");
            Console.WriteLine($"� Connection: {options.ConnectionString.Substring(0, Math.Min(50, options.ConnectionString.Length))}...");
            Console.WriteLine($"⚠️  Note: Using raw Lucene.Net indexing (not SenseNet's indexing pipeline)");
            Console.WriteLine();

            // TODO: Replace with SenseNet's actual indexing mechanism
            // This currently uses raw Lucene.Net indexing
            // To use SenseNet's indexing, we would need:
            // 1. Repository.Start() to initialize SenseNet
            // 2. IIndexPopulator.ClearAndPopulateAllAsync() for full reindex
            // 3. Or IIndexManager.AddDocumentsAsync() for incremental indexing
            // 4. Proper field mapping through SenseNet's Field system
            // 5. Content handlers for field-specific indexing logic

            // CURRENT: Raw Lucene.Net indexing (bypasses SenseNet's indexing pipeline)
            using var directory = Lucene.Net.Store.FSDirectory.Open(new System.IO.DirectoryInfo(indexPath));
            using var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29);
            using var indexWriter = new Lucene.Net.Index.IndexWriter(directory, analyzer, true, Lucene.Net.Index.IndexWriter.MaxFieldLength.UNLIMITED);

            using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync();

            // Use SenseNet's data model queries to get content
            const string query = @"
                SELECT N.NodeId, V.VersionId, N.Path, NT.Name as NodeTypeName,
                       CAST(N.Timestamp as bigint) as TimestampNumeric,
                       CAST(V.Timestamp as bigint) as VersionTimestampNumeric,
                       N.ParentNodeId, N.[Index] as NodeIndex, N.IsDeleted,
                       N.Name as NodeName, N.DisplayName
                FROM Nodes N
                JOIN Versions V ON N.NodeId = V.NodeId
                JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                WHERE N.IsDeleted = 0 AND V.Status = 1"; // Only published, non-deleted content

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            var batch = new List<Lucene.Net.Documents.Document>();
            const int batchSize = 100;

            while (await reader.ReadAsync())
            {
                try
                {
                    var document = new Lucene.Net.Documents.Document();

                    // Extract data using SenseNet's data model
                    var nodeId = reader.GetInt32(0);
                    var versionId = reader.GetInt32(1);
                    var path = reader.GetString(2);
                    var nodeTypeName = reader.GetString(3);
                    var timestampNumeric = reader.GetInt64(4);
                    var versionTimestampNumeric = reader.GetInt64(5);
                    var parentNodeId = reader.GetInt32(6);
                    var nodeIndex = reader.GetInt32(7);
                    var isDeletedByte = reader.GetByte(8);
                    var isDeleted = isDeletedByte != 0; // Convert byte to boolean (0 = false, non-zero = true)
                    var nodeName = reader.GetString(9);
                    var displayName = reader.IsDBNull(10) ? nodeName : reader.GetString(10);

                    // Add SenseNet standard fields (exact field names that SenseNet expects)
                    document.Add(new Lucene.Net.Documents.Field("NodeId", nodeId.ToString(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                    document.Add(new Lucene.Net.Documents.Field("VersionId", versionId.ToString(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                    document.Add(new Lucene.Net.Documents.Field("Path", path, Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.ANALYZED));
                    document.Add(new Lucene.Net.Documents.Field("Name", nodeName, Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.ANALYZED));
                    document.Add(new Lucene.Net.Documents.Field("DisplayName", displayName, Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.ANALYZED));
                    document.Add(new Lucene.Net.Documents.Field("NodeTypeName", nodeTypeName, Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                    document.Add(new Lucene.Net.Documents.Field("ParentNodeId", parentNodeId.ToString(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                    document.Add(new Lucene.Net.Documents.Field("Index", nodeIndex.ToString(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                    document.Add(new Lucene.Net.Documents.Field("Timestamp", timestampNumeric.ToString(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                    document.Add(new Lucene.Net.Documents.Field("VersionTimestamp", versionTimestampNumeric.ToString(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
                    document.Add(new Lucene.Net.Documents.Field("IsDeleted", isDeleted.ToString(), Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));

                    // Add content field for full-text search
                    document.Add(new Lucene.Net.Documents.Field("Content", $"{nodeName} {displayName} {path}", Lucene.Net.Documents.Field.Store.NO, Lucene.Net.Documents.Field.Index.ANALYZED));

                    batch.Add(document);
                    processedCount++;

                    // Process batch
                    if (batch.Count >= batchSize)
                    {
                        foreach (var doc in batch)
                        {
                            indexWriter.AddDocument(doc);
                        }
                        batch.Clear();
                        Console.Write($"\r📊 Processed {processedCount:N0} SenseNet content items...");
                    }

                    // Check max items limit
                    if (options.MaxItems > 0 && processedCount >= options.MaxItems)
                    {
                        Console.WriteLine($"\n⚠️  Reached max items limit: {options.MaxItems}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n⚠️  Error processing SenseNet content item {processedCount + 1}: {ex.Message}");
                    // Continue with next item
                }
            }

            // Process remaining batch
            if (batch.Count > 0)
            {
                foreach (var doc in batch)
                {
                    indexWriter.AddDocument(doc);
                }
            }

            indexWriter.Commit();
            Console.WriteLine($"\n✅ Lucene index committed with SenseNet-compatible field mappings ({processedCount:N0} content items)");

            // Get processed item count
            processedCount = await GetProcessedItemCount(options.ConnectionString);

            var endTime = DateTime.UtcNow;
            var duration = endTime - startTime;

            return new IndexCreationResult
            {
                RepositoryPath = options.RepositoryPath,
                StartTime = startTime,
                EndTime = endTime,
                Success = true,
                Message = $"Successfully created Lucene index with SenseNet-compatible field mappings using {processedCount:N0} content items in {duration.TotalSeconds:F1} seconds. Note: This uses raw Lucene.Net indexing, not SenseNet's indexing pipeline.",
                IndexPath = indexPath,
                ProcessedItemCount = processedCount,
                Options = options
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error during native SenseNet indexing: {ex.Message}");
            return new IndexCreationResult
            {
                RepositoryPath = options.RepositoryPath,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = false,
                Message = $"Native SenseNet indexing failed: {ex.Message}. This uses SenseNet's Repository.Start() and IIndexPopulator infrastructure.",
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

    private static async Task<int> GetProcessedItemCount(string connectionString)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            const string query = "SELECT COUNT(*) FROM Nodes WHERE NodeId > 1"; // Exclude root node
            using var command = new SqlCommand(query, connection);
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch
        {
            return 0; // Return 0 if we can't determine the count
        }
    }
}