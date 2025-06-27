using System.CommandLine;
using System.Data.SqlClient;
using System.Text;
using System.Web;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;

namespace SenseNetIndexTools
{
    public class ContentComparer
    {
        public static bool VerboseLogging { get; set; } = false;


        public static Command Create()
        {
            var command = new Command("compare", "Compare content items between database and index");

            var indexPathOption = new Option<string>(
                name: "--index-path",
                description: "Path to the Lucene index directory");
            indexPathOption.IsRequired = true;

            var connectionStringOption = new Option<string>(
                name: "--connection-string",
                description: "SQL Connection string to the SenseNet database");
            connectionStringOption.IsRequired = true;

            var repositoryPathOption = new Option<string>(
                name: "--repository-path",
                description: "Path in the content repository to check (e.g., /Root/Sites/Default_Site)");
            repositoryPathOption.IsRequired = true;

            var recursiveOption = new Option<bool>(
                name: "--recursive",
                description: "Recursively list all content items under the specified path",
                getDefaultValue: () => true);

            var depthOption = new Option<int>(
                name: "--depth",
                description: "Limit listing to specified depth (1=direct children only, 0=all descendants)",
                getDefaultValue: () => 0);

            var orderByOption = new Option<string>(
                name: "--order-by",
                description: "Order results by: 'path' (default), 'id', 'version', 'type'",
                getDefaultValue: () => "path");
            orderByOption.FromAmong("path", "id", "version", "type");

            var verboseOption = new Option<bool>(
                name: "--verbose",
                description: "Enable detailed logging of path normalization and matching process",
                getDefaultValue: () => false);

            var outputOption = new Option<string?>(
                name: "--output",
                description: "Path to save the comparison report to a file");

            var formatOption = new Option<string>(
                name: "--format",
                description: "Output format: 'md' (Markdown) or 'html' (HTML format suitable for web viewing)",
                getDefaultValue: () => "md");
            formatOption.FromAmong("md", "html");

            command.AddOption(indexPathOption);
            command.AddOption(connectionStringOption);
            command.AddOption(repositoryPathOption);
            command.AddOption(recursiveOption);
            command.AddOption(depthOption);
            command.AddOption(orderByOption);
            command.AddOption(outputOption);
            command.AddOption(formatOption);
            command.SetHandler((string indexPath, string connectionString, string repositoryPath, bool recursive, int depth, string orderBy, string? output, string format) =>
            {
                try
                {
                    // Note: Verbose logging can be enabled by setting VerboseLogging = true in code
                    // We can't add it as a parameter due to System.CommandLine limitations (max 8 parameters)
                    VerboseLogging = false;
                    if (!Program.IsValidLuceneIndex(indexPath))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {indexPath}");
                        Environment.Exit(1);
                        return;
                    }

                    Console.WriteLine($"Repository path: {repositoryPath}");
                    Console.WriteLine($"Recursive mode: {(recursive ? "Yes" : "No")}");
                    Console.WriteLine($"Depth limit: {depth}");

                    // Get items from database and mark as database items 
                    var dbItems = GetContentItemsFromDatabase(connectionString, repositoryPath, recursive, depth)
                        .Select(i => { i.InDatabase = true; return i; });

                    // Get items from index
                    var indexItems = GetContentItemsFromIndex(indexPath, repositoryPath, recursive, depth)
                        .Select(i => { i.InIndex = true; return i; });
                    // Combine and group items by normalized path and type for side-by-side display
                    var items = dbItems.Union(indexItems)
                        .GroupBy(i => new { 
                            Path = NormalizePath(i.Path),
                            Type = i.NodeType ?? "unknown", // NodeType is already normalized to lowercase
                            // Create a unique identifier that distinctly identifies each item by both ID and version
                            // This ensures items with same path but different IDs or versions are treated as separate entries
                            Id = i.InDatabase ? i.NodeId.ToString() : i.IndexNodeId ?? "unknown",
                            Version = i.InDatabase ? i.VersionId.ToString() : i.IndexVersionId ?? "unknown"
                        })
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
                                dbItem.IndexTimestamp = indexItem.IndexTimestamp;
                                dbItem.IndexVersionTimestamp = indexItem.IndexVersionTimestamp;
                                return dbItem;
                            }
                            
                            return dbItem ?? indexItem!;
                        });

                    // Filter by depth if specified
                    if (depth > 0)
                    {
                        var basePath = repositoryPath.TrimEnd('/');
                        var baseDepth = basePath.Count(c => c == '/');
                        items = items.Where(item => {
                            var itemDepth = item.Path.Count(c => c == '/') - baseDepth;
                            return itemDepth <= depth;
                        });
                    }

                    // Sort items based on order-by option
                    var groupedItems = orderBy switch
                    {
                        "id" => items.OrderBy(i => i.NodeId).ToList(),
                        "version" => items.OrderBy(i => i.VersionId).ToList(),
                        "type" => items.OrderBy(i => i.NodeType ?? "unknown") // NodeType is already normalized to lowercase
                                    .ThenBy(i => NormalizePath(i.Path))
                                    .ToList(),
                        _ => items.OrderBy(i => NormalizePath(i.Path)).ToList()
                    };
                    // Display results on console
                    GenerateReport(groupedItems, output, format);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error comparing items: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    Environment.Exit(1);
                }
            }, indexPathOption, connectionStringOption, repositoryPathOption, recursiveOption, depthOption, orderByOption, outputOption, formatOption);

            return command;
        }

        private static void GenerateReport(List<ContentItem> items, string? outputPath, string format)
        {
            string reportContent;

            if (format.ToLower() == "html")
            {
                reportContent = GenerateHtmlReport(items);
            }
            else
            {
                reportContent = GenerateMarkdownReport(items);
            }

            var matchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "Match");
            var idMismatchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "ID mismatch");
            var timestampMismatchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "Timestamp mismatch");
            var dbOnlyCount = items.Count(i => i.InDatabase && !i.InIndex);
            var indexOnlyCount = items.Count(i => !i.InDatabase && i.InIndex);

            // Build report in markdown format            // Always show summary on console
            Console.WriteLine($"\nFound {items.Count} unique content items (by path, ID, and version):");
            Console.WriteLine($"Perfect matches: {matchCount}");
            Console.WriteLine($"ID mismatches: {idMismatchCount}");
            Console.WriteLine($"Timestamp mismatches: {timestampMismatchCount}");
            Console.WriteLine($"Database only: {dbOnlyCount}");
            Console.WriteLine($"Index only: {indexOnlyCount}");

            // Save to file if output path is specified
            if (!string.IsNullOrEmpty(outputPath))
            {
                File.WriteAllText(outputPath, reportContent);
                Console.WriteLine($"\nDetailed report saved to: {outputPath}");
            }
            else
            {
                // Show items on console in a simpler format
                Console.WriteLine("\nDB_NodeId\tDB_VerID\tIdx_NodeId\tIdx_VerID\tDB_Timestamp\tIdx_Timestamp\tPath\tNodeType\tStatus");
                Console.WriteLine(new string('-', 140));

                foreach (var item in items)
                {
                    Console.WriteLine(item.ToString());
                }
            }
        }

        private static string GenerateMarkdownReport(List<ContentItem> items)
        {
            var matchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "Match");
            var idMismatchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "ID mismatch");
            var timestampMismatchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "Timestamp mismatch");
            var dbOnlyCount = items.Count(i => i.InDatabase && !i.InIndex);
            var indexOnlyCount = items.Count(i => !i.InDatabase && i.InIndex);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Content Comparison Report");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- Total unique items: {items.Count} (unique by path, ID, and version)");
            sb.AppendLine($"- Perfect matches: {matchCount}");
            sb.AppendLine($"- ID mismatches: {idMismatchCount}");
            sb.AppendLine($"- Timestamp mismatches: {timestampMismatchCount}");
            sb.AppendLine($"- Database only: {dbOnlyCount}");
            sb.AppendLine($"- Index only: {indexOnlyCount}");
            sb.AppendLine();
            sb.AppendLine("## Comparison Results");
            sb.AppendLine();
            sb.AppendLine("| Status | DB NodeId | DB VerID | Index NodeId | Index VerID | DB Timestamp | Index Timestamp | Path | Type |");
            sb.AppendLine("|---------|-----------|----------|--------------|-------------|--------------|-----------------|------|------|");

            foreach (var item in items.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
            {
                var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                var dbTimestamp = item.InDatabase && item.TimestampNumeric > 0 ? item.TimestampNumeric.ToString() : "-";
                var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                var idxTimestamp = item.InIndex ? (item.IndexTimestamp ?? "-") : "-";
                
                sb.AppendLine($"| {item.Status} | {dbNodeId} | {dbVerID} | {idxNodeId} | {idxVerID} | {dbTimestamp} | {idxTimestamp} | {item.Path} | {item.NodeType} |");
            }
            return sb.ToString();
        }

        private static List<ContentItem> GetContentItemsFromDatabase(string connectionString, string path, bool recursive, int depth)
        {
            var items = new List<ContentItem>();
            string sanitizedPath = path.Replace("'", "''");

            Console.WriteLine($"DATABASE QUERY: Path={path}, Recursive={recursive}, Depth={depth}");            string sql = recursive
                ? @"SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName, 
                           CAST(N.Timestamp as bigint) as TimestampNumeric, 
                           CAST(V.Timestamp as bigint) as VersionTimestampNumeric
                    FROM Nodes N
                    JOIN Versions V ON N.NodeId = V.NodeId
                    JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                    WHERE (LOWER(N.Path) = LOWER(@path) OR LOWER(N.Path) LIKE LOWER(@pathPattern))
                    ORDER BY N.Path"
                : @"SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName, 
                           CAST(N.Timestamp as bigint) as TimestampNumeric, 
                           CAST(V.Timestamp as bigint) as VersionTimestampNumeric
                    FROM Nodes N
                    JOIN Versions V ON N.NodeId = V.NodeId
                    JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                    WHERE LOWER(N.Path) = LOWER(@path)
                    ORDER BY N.Path";

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@path", sanitizedPath);
                    if (recursive)
                    {
                        command.Parameters.AddWithValue("@pathPattern", sanitizedPath + "/%");
                    }

                    int loadedCount = 0;
                    int logInterval = 5000; // Log every 5000 items
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var nodePath = reader.GetString(reader.GetOrdinal("Path"));
                            var nodeId = reader.GetInt32(reader.GetOrdinal("NodeId"));
                            var versionId = reader.GetInt32(reader.GetOrdinal("VersionId"));

                            loadedCount++;
                            if (loadedCount % logInterval == 0)
                            {
                                Console.WriteLine($"DB LOAD PROGRESS: Loaded {loadedCount} items from database");
                            }

                            // Log every 20000th item as a sample
                            if (VerboseLogging && loadedCount % 20000 == 0)
                            {
                                Console.WriteLine($"DB SAMPLE: NodeId={nodeId}, VersionId={versionId}, Path='{nodePath}', NormalizedPath='{NormalizePath(nodePath)}'");
                            }

                            // Normalize node type when adding from database
                            var nodeType = reader.GetString(reader.GetOrdinal("NodeTypeName")).ToLowerInvariant();
                            items.Add(new ContentItem
                            {
                                NodeId = nodeId,
                                VersionId = versionId,
                                Path = nodePath,
                                NodeType = nodeType, // Node type already normalized to lowercase
                                TimestampNumeric = reader.GetInt64(reader.GetOrdinal("TimestampNumeric")),
                                VersionTimestampNumeric = reader.GetInt64(reader.GetOrdinal("VersionTimestampNumeric")),
                                InDatabase = true,
                                InIndex = false
                            });
                        }
                    }

                    Console.WriteLine($"DB LOAD COMPLETE: Loaded {items.Count} items from database");

                    // Log a few samples of what we loaded
                    if (VerboseLogging)
                    {
                        for (int i = 0; i < Math.Min(5, items.Count); i++)
                        {
                            Console.WriteLine($"DB ITEM SAMPLE {i+1}: NodeId={items[i].NodeId}, Path='{items[i].Path}', NormalizedPath='{NormalizePath(items[i].Path)}'");
                        }
                    }
                }
            }
            
            return items;
        }

        private static List<ContentItem> GetContentItemsFromIndex(string indexPath, string path, bool recursive, int depth)
        {
            var items = new List<ContentItem>();
            Console.WriteLine($"INDEX QUERY: Path={path}, Recursive={recursive}, Depth={depth}");

            using (var directory = FSDirectory.Open(new DirectoryInfo(indexPath)))
            {
                using (var reader = IndexReader.Open(directory, true))
                using (var searcher = new IndexSearcher(reader))
                {
                    // Convert path to lowercase for SenseNet indexes which store paths in lowercase
                    var normalizedPath = path.ToLowerInvariant();
                    Console.WriteLine($"INDEX NORMALIZED QUERY PATH: '{path}' -> '{normalizedPath}'");

                    Query query;
                    if (!recursive)
                    {
                        // Direct match only - exact path
                        query = new TermQuery(new Term("Path", normalizedPath));
                        Console.WriteLine($"INDEX QUERY TYPE: Exact match on path");
                    }
                    else
                    {
                        var boolQuery = new BooleanQuery();
                        // Root path match
                        boolQuery.Add(new TermQuery(new Term("Path", normalizedPath)), BooleanClause.Occur.SHOULD);

                        // Child path matches
                        var childPathPrefix = normalizedPath.TrimEnd('/') + "/";
                        Console.WriteLine($"INDEX QUERY TYPE: Recursive, including path prefix: '{childPathPrefix}'");
                        var childQuery = new PrefixQuery(new Term("Path", childPathPrefix));
                        boolQuery.Add(childQuery, BooleanClause.Occur.SHOULD);

                        query = boolQuery;
                    }

                    // Implement paging for large indexes
                    const int PageSize = 10000; // Process 10000 documents at a time
                    int totalProcessed = 0;
                    
                    // Create a collector to find all matching documents
                    var initialCollector = TopScoreDocCollector.Create(1000000, true); // Use a large limit
                    searcher.Search(query, initialCollector);
                    var topDocs = initialCollector.TopDocs();
                    int totalHits = topDocs.TotalHits;
                    
                    Console.WriteLine($"INDEX QUERY RESULTS: Found {totalHits} items in index matching the query");
                    
                    // Process in batches to avoid memory issues
                    while (totalProcessed < totalHits)
                    {
                        var collector = TopScoreDocCollector.Create(1000000, true); // Use a large limit
                        searcher.Search(query, collector);
                        var searchHits = collector.TopDocs(totalProcessed, Math.Min(PageSize, totalHits - totalProcessed)).ScoreDocs;
                        
                        if (searchHits.Length == 0) break; // No more results

                        Console.WriteLine($"INDEX PROCESSING BATCH: {totalProcessed}-{totalProcessed + searchHits.Length} of {totalHits}");

                        int batchCount = 0;
                        foreach (var hitDoc in searchHits)
                        {
                            var doc = searcher.Doc(hitDoc.Doc);
                              var nodeId = doc.Get("Id") ?? doc.Get("NodeId") ?? "0";
                            var versionId = doc.Get("Version_") ?? doc.Get("VersionId") ?? "0";
                            var docPath = doc.Get("Path") ?? string.Empty;
                            var type = (doc.Get("Type") ?? doc.Get("NodeType") ?? "Unknown").ToLowerInvariant();
                            var timestamp = doc.Get("NodeTimestamp") ?? string.Empty;
                            var versionTimeStamp = doc.Get("VersionTimestamp") ?? string.Empty;

                            batchCount++;
                            // Log every 20000th item as a sample
                            if (VerboseLogging && ((totalProcessed + batchCount) % 20000 == 0 || batchCount <= 5))
                            {
                                Console.WriteLine($"INDEX SAMPLE: NodeId={nodeId}, VersionId={versionId}, Path='{docPath}', NormalizedPath='{NormalizePath(docPath)}', Type='{type}'");
                            }

                            items.Add(new ContentItem
                            {
                                NodeId = 0,  // We'll update this if we find a matching DB item
                                VersionId = 0, // We'll update this if we find a matching DB item
                                Path = docPath,
                                NodeType = type, // Node type already normalized to lowercase
                                InDatabase = false,
                                InIndex = true,
                                IndexNodeId = nodeId,
                                IndexVersionId = versionId,
                                IndexTimestamp = timestamp,
                                IndexVersionTimestamp = versionTimeStamp
                            });
                        }
                        
                        totalProcessed += searchHits.Length;
                    }

                    Console.WriteLine($"INDEX LOAD COMPLETE: Loaded {items.Count} items from index");

                    // Log a few samples of what we loaded
                    if (VerboseLogging)
                    {
                        for (int i = 0; i < Math.Min(5, items.Count); i++)
                        {
                            Console.WriteLine($"INDEX ITEM SAMPLE {i+1}: NodeId={items[i].IndexNodeId}, Path='{items[i].Path}', NormalizedPath='{NormalizePath(items[i].Path)}'");
                        }
                    }
                }
            }

            return items;
        }

        public List<ContentItem> CompareContent(string indexPath, string connectionString, string repositoryPath, bool recursive, int depth)
        {
            if (!Program.IsValidLuceneIndex(indexPath))
            {
                throw new InvalidOperationException($"The directory does not appear to be a valid Lucene index: {indexPath}");
            }

            // Get items from database and mark as database items 
            var dbItems = GetContentItemsFromDatabase(connectionString, repositoryPath, recursive, depth)
                .Select(i => { i.InDatabase = true; return i; });

            // Get items from index and mark as index items
            var indexItems = GetContentItemsFromIndex(indexPath, repositoryPath, recursive, depth)
                .Select(i => { i.InIndex = true; return i; });

            // Combine and group items by path for side-by-side comparison
            var items = dbItems.Union(indexItems)
                .GroupBy(i => i.Path.ToLowerInvariant())
                .Select(g =>
                {
                    var dbItem = g.FirstOrDefault(i => i.InDatabase);
                    var indexItem = g.FirstOrDefault(i => i.InIndex);                    if (dbItem != null && indexItem != null)
                    {
                        // Found in both - merge the index data into the DB item
                        dbItem.InIndex = true;
                        dbItem.IndexNodeId = indexItem.IndexNodeId;
                        dbItem.IndexVersionId = indexItem.IndexVersionId;
                        dbItem.IndexTimestamp = indexItem.IndexTimestamp;
                        dbItem.IndexVersionTimestamp = indexItem.IndexVersionTimestamp;
                        return dbItem;
                    }
                    
                    return dbItem ?? indexItem!;
                });

            // Filter by depth if specified
            if (depth > 0)
            {
                var basePath = repositoryPath.TrimEnd('/');
                var baseDepth = basePath.Count(c => c == '/');
                items = items.Where(item => {
                    var itemDepth = item.Path.Count(c => c == '/') - baseDepth;
                    return itemDepth <= depth;
                });
            }

            return items.ToList();
        }

        private static string GenerateHtmlReport(List<ContentItem> items)
        {
            var matchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "Match");
            var idMismatchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "ID mismatch");
            var timestampMismatchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "Timestamp mismatch");
            var dbOnlyCount = items.Count(i => i.InDatabase && !i.InIndex);
            var indexOnlyCount = items.Count(i => !i.InDatabase && i.InIndex);

            var sb = new StringBuilder();
            
            // Start HTML document
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.AppendLine("<title>SenseNet Content Comparison Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(@"
                body { 
                    font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif; 
                    line-height: 1.6; 
                    max-width: 1400px; 
                    margin: 0 auto; 
                    padding: 20px;
                    color: #333;
                    background-color: #fafbfc;
                }
                .container {
                    background: white;
                    border-radius: 6px;
                    box-shadow: 0 1px 3px rgba(0,0,0,0.1);
                    padding: 30px;
                }
                h1, h2 { 
                    border-bottom: 1px solid #eee; 
                    padding-bottom: 0.3em; 
                    margin-top: 1.5em;
                    color: #24292e;
                }
                h1 {
                    margin-top: 0;
                    font-size: 2rem;
                }
                table { 
                    border-collapse: collapse; 
                    width: 100%; 
                    margin: 1em 0; 
                    font-size: 14px;
                }
                th, td { 
                    padding: 12px 8px; 
                    text-align: left; 
                    border-bottom: 1px solid #ddd; 
                }
                th { 
                    background: #f6f8fa;
                    font-weight: 600; 
                    color: #586069;
                }
                tr:hover {
                    background-color: #f6f8fa;
                }
                .summary-section {
                    background: #f6f8fa;
                    padding: 20px;
                    border-radius: 6px;
                    margin-bottom: 30px;
                    border-left: 4px solid #0366d6;
                }
                .stats-container {
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
                    gap: 20px;
                    margin-top: 20px;
                }
                .stat-card {
                    background: white;
                    padding: 20px;
                    border-radius: 6px;
                    box-shadow: 0 1px 3px rgba(0,0,0,0.1);
                    text-align: center;
                }
                .stat-title {
                    color: #586069;
                    font-size: 14px;
                    font-weight: 600;
                    margin-bottom: 8px;
                }
                .stat-value {
                    font-size: 28px;
                    font-weight: bold;
                    margin-bottom: 5px;
                }
                .stat-match { color: #28a745; }
                .stat-mismatch { color: #d73a49; }
                .stat-timestamp { color: #f66a0a; }
                .stat-info { color: #6f42c1; }
                .status-match { 
                    color: #28a745; 
                    font-weight: 600;
                }
                .status-mismatch { 
                    color: #d73a49; 
                    font-weight: 600;
                }
                .status-timestamp { 
                    color: #f66a0a; 
                    font-weight: 600;
                }
                .status-db-only { 
                    color: #6f42c1; 
                    font-weight: 600;
                }
                .status-index-only { 
                    color: #0366d6; 
                    font-weight: 600;
                }
                .path-cell {
                    font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', Menlo, monospace;
                    font-size: 12px;
                    max-width: 300px;
                    word-break: break-all;
                }
                .type-cell {
                    font-weight: 500;
                    color: #6f42c1;
                }
                footer {
                    margin-top: 30px;
                    padding-top: 20px;
                    border-top: 1px solid #eee;
                    text-align: center;
                    font-size: 14px;
                    color: #586069;
                }
                .section-title {
                    display: flex;
                    align-items: center;
                    gap: 10px;
                }
                .badge {
                    display: inline-block;
                    padding: 3px 8px;
                    border-radius: 12px;
                    font-size: 12px;
                    font-weight: 600;
                    color: white;
                }
                .badge-total { background-color: #586069; }
            ");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div class=\"container\">");
            
            // Report header
            sb.AppendLine("<h1>SenseNet Content Comparison Report</h1>");
            
            // Summary section with statistics
            sb.AppendLine("<div class=\"summary-section\">");
            sb.AppendLine("<h2>Summary</h2>");
            sb.AppendLine("<div class=\"stats-container\">");
            
            sb.AppendLine("<div class=\"stat-card\">");
            sb.AppendLine("<div class=\"stat-title\">Total Items</div>");
            sb.AppendLine($"<div class=\"stat-value\">{items.Count:N0}</div>");
            sb.AppendLine("<small>Unique by path, ID, version</small>");
            sb.AppendLine("</div>");
            
            sb.AppendLine("<div class=\"stat-card\">");
            sb.AppendLine("<div class=\"stat-title\">Perfect Matches</div>");
            sb.AppendLine($"<div class=\"stat-value stat-match\">{matchCount:N0}</div>");
            sb.AppendLine($"<small>{(items.Count > 0 ? (matchCount * 100.0 / items.Count):0.0):F1}% of total</small>");
            sb.AppendLine("</div>");
            
            sb.AppendLine("<div class=\"stat-card\">");
            sb.AppendLine("<div class=\"stat-title\">ID Mismatches</div>");
            sb.AppendLine($"<div class=\"stat-value stat-mismatch\">{idMismatchCount:N0}</div>");
            sb.AppendLine($"<small>{(items.Count > 0 ? (idMismatchCount * 100.0 / items.Count):0.0):F1}% of total</small>");
            sb.AppendLine("</div>");
            
            sb.AppendLine("<div class=\"stat-card\">");
            sb.AppendLine("<div class=\"stat-title\">Timestamp Mismatches</div>");
            sb.AppendLine($"<div class=\"stat-value stat-timestamp\">{timestampMismatchCount:N0}</div>");
            sb.AppendLine($"<small>{(items.Count > 0 ? (timestampMismatchCount * 100.0 / items.Count):0.0):F1}% of total</small>");
            sb.AppendLine("</div>");
            
            sb.AppendLine("<div class=\"stat-card\">");
            sb.AppendLine("<div class=\"stat-title\">Database Only</div>");
            sb.AppendLine($"<div class=\"stat-value stat-info\">{dbOnlyCount:N0}</div>");
            sb.AppendLine($"<small>{(items.Count > 0 ? (dbOnlyCount * 100.0 / items.Count):0.0):F1}% of total</small>");
            sb.AppendLine("</div>");
            
            sb.AppendLine("</div>"); // End of stats-container
            sb.AppendLine("</div>"); // End of summary-section
            
            // Detailed results table
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine("<div class=\"section-title\">");
            sb.AppendLine("<h2>Detailed Comparison Results</h2>");
            sb.AppendLine($"<span class=\"badge badge-total\">{items.Count} items</span>");
            sb.AppendLine("</div>");
            
            sb.AppendLine("<table>");
            sb.AppendLine("<thead>");
            sb.AppendLine("<tr>");
            sb.AppendLine("<th>Status</th>");
            sb.AppendLine("<th>DB NodeId</th>");
            sb.AppendLine("<th>DB VerID</th>");
            sb.AppendLine("<th>Index NodeId</th>");
            sb.AppendLine("<th>Index VerID</th>");
            sb.AppendLine("<th>DB Timestamp</th>");
            sb.AppendLine("<th>Index Timestamp</th>");
            sb.AppendLine("<th>Path</th>");
            sb.AppendLine("<th>Type</th>");
            sb.AppendLine("</tr>");
            sb.AppendLine("</thead>");
            sb.AppendLine("<tbody>");
            
            foreach (var item in items.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
            {
                var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                var dbTimestamp = item.InDatabase && item.TimestampNumeric > 0 ? item.TimestampNumeric.ToString() : "-";
                var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                var idxTimestamp = item.InIndex ? (item.IndexTimestamp ?? "-") : "-";
                
                var statusClass = item.Status switch
                {
                    "Match" => "status-match",
                    "ID mismatch" => "status-mismatch",
                    "Timestamp mismatch" => "status-timestamp",
                    "DB Only" => "status-db-only",
                    "Index Only" => "status-index-only",
                    _ => ""
                };
                
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td class=\"{statusClass}\">{item.Status}</td>");
                sb.AppendLine($"<td>{dbNodeId}</td>");
                sb.AppendLine($"<td>{dbVerID}</td>");
                sb.AppendLine($"<td>{idxNodeId}</td>");
                sb.AppendLine($"<td>{idxVerID}</td>");
                sb.AppendLine($"<td>{dbTimestamp}</td>");
                sb.AppendLine($"<td>{idxTimestamp}</td>");
                sb.AppendLine($"<td class=\"path-cell\">{System.Web.HttpUtility.HtmlEncode(item.Path)}</td>");
                sb.AppendLine($"<td class=\"type-cell\">{item.NodeType}</td>");
                sb.AppendLine("</tr>");
            }
            
            sb.AppendLine("</tbody>");
            sb.AppendLine("</table>");
            sb.AppendLine("</div>");
            
            // Footer
            sb.AppendLine("<footer>");
            sb.AppendLine($"<p>Generated by SenseNet Index Maintenance Suite on {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
            sb.AppendLine("</footer>");

            // Close HTML document
            sb.AppendLine("</div>"); // End container
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        private static string NormalizePath(string path)
        {
            var original = path;
            var normalized = path.Replace('\\', '/').ToLowerInvariant().TrimEnd('/');

            // Log normalization only when it actually changes something and verbose logging is enabled
            if (original != normalized && VerboseLogging)
            {
                Console.WriteLine($"PATH NORMALIZATION: '{original}' -> '{normalized}'");
            }

            // Special handling for content type paths
            if (normalized.StartsWith("/root/system/schema/contenttypes/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                var basePathParts = new List<string>();

                // Always include the full path for content types
                for (int i = 0; i < parts.Length; i++)
                {
                    basePathParts.Add(parts[i]);
                }

                var contentTypePath = "/" + string.Join("/", basePathParts);
                if (contentTypePath != normalized)
                {
                    // Only log content type normalization when verbose logging is enabled
                    if (VerboseLogging)
                    {
                        Console.WriteLine($"CONTENT TYPE PATH NORMALIZED: '{normalized}' -> '{contentTypePath}'");
                    }
                    return contentTypePath;
                }
            }

            return normalized;
        }
    }
}
