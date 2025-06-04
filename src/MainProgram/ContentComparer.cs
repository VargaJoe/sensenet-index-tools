using System.CommandLine;
using System.Data.SqlClient;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;

using System.Text;

namespace SenseNetIndexTools
{    public class ContentComparer
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
                description: "Path to save the comparison report to a markdown file");            command.AddOption(indexPathOption);
            command.AddOption(connectionStringOption);
            command.AddOption(repositoryPathOption);
            command.AddOption(recursiveOption);
            command.AddOption(depthOption);
            command.AddOption(orderByOption);
            command.AddOption(verboseOption);
            command.AddOption(outputOption);
            command.SetHandler((string indexPath, string connectionString, string repositoryPath, bool recursive, int depth, string orderBy, bool verbose, string? output) =>
            {
                try
                {
                    VerboseLogging = verbose;
                    
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

                    // Combine and group items by normalized path, type, NodeId/IndexNodeId AND version for side-by-side display
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
                    GenerateReport(groupedItems, output);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error comparing items: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    Environment.Exit(1);
                }
            }, indexPathOption, connectionStringOption, repositoryPathOption, recursiveOption, depthOption, orderByOption, verboseOption, outputOption);

            return command;
        }

        private static void GenerateReport(List<ContentItem> items, string? outputPath)
        {
            var matchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "Match");
            var timestampMismatchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "Timestamp mismatch");
            var idMismatchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "ID mismatch");
            var dbOnlyCount = items.Count(i => i.InDatabase && !i.InIndex);
            var indexOnlyCount = items.Count(i => !i.InDatabase && i.InIndex);

            // Build report in markdown format
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Content Comparison Report");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- Total unique items: {items.Count} (unique by path, ID, and version)");
            sb.AppendLine($"- Perfect matches: {matchCount}");
            sb.AppendLine($"- Timestamp mismatches: {timestampMismatchCount}");
            sb.AppendLine($"- ID mismatches: {idMismatchCount}");
            sb.AppendLine($"- Database only: {dbOnlyCount}");
            sb.AppendLine($"- Index only: {indexOnlyCount}");
            sb.AppendLine();
              sb.AppendLine("## Comparison Results");
            sb.AppendLine();
            sb.AppendLine("| Status | DB NodeId | DB VerID | DB Timestamp | Index NodeId | Index VerID | Index Timestamp | Path | Type |");
            sb.AppendLine("|---------|-----------|----------|--------------|--------------|-------------|----------------|------|------|");            foreach (var item in items.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
            {
                var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                var dbTimestampValue = item.InDatabase ? $"{item.TimestampNumeric}" : "-";
                var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                // Display raw numeric timestamp value from index without DateTime conversion
                var idxTimestamp = item.InIndex ? item.IndexTimestamp : "-";
                sb.AppendLine($"| {item.Status} | {dbNodeId} | {dbVerID} | {dbTimestampValue} | {idxNodeId} | {idxVerID} | {idxTimestamp} | {item.Path} | {item.NodeType} |");
            }
            
            // Always show summary on console
            Console.WriteLine($"\nFound {items.Count} unique content items (by path, ID, and version):");
            Console.WriteLine($"Perfect matches: {matchCount}");
            Console.WriteLine($"Timestamp mismatches: {timestampMismatchCount}");
            Console.WriteLine($"ID mismatches: {idMismatchCount}");
            Console.WriteLine($"Database only: {dbOnlyCount}");
            Console.WriteLine($"Index only: {indexOnlyCount}");

            // Save to file if output path is specified
            if (!string.IsNullOrEmpty(outputPath))
            {
                File.WriteAllText(outputPath, sb.ToString());
                Console.WriteLine($"\nDetailed report saved to: {outputPath}");
            }
            else
            {                // Show items on console in a simpler format
                Console.WriteLine("\nDB_NodeId\tDB_VerID\tDB_Timestamp\tIdx_NodeId\tIdx_VerID\tIdx_Timestamp\tPath\tNodeType\tStatus");
                Console.WriteLine(new string('-', 150));

                foreach (var item in items)
                {
                    Console.WriteLine(item.ToString());
                }
            }
        }

        private static List<ContentItem> GetContentItemsFromDatabase(string connectionString, string path, bool recursive, int depth)
        {
            var items = new List<ContentItem>();
            string sanitizedPath = path.Replace("'", "''");
            
            Console.WriteLine($"DATABASE QUERY: Path={path}, Recursive={recursive}, Depth={depth}");
            
            string sql = recursive
                ? @"SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName, 
                    CAST(N.Timestamp as bigint) as TimestampValue,
                    CAST(V.Timestamp as bigint) as VersionTimestampValue
                    FROM Nodes N
                    JOIN Versions V ON N.NodeId = V.NodeId
                    JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                    WHERE (LOWER(N.Path) = LOWER(@path) OR LOWER(N.Path) LIKE LOWER(@pathPattern))
                    ORDER BY N.Path"
                : @"SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName, 
                    CAST(N.Timestamp as bigint) as TimestampValue,
                    CAST(V.Timestamp as bigint) as VersionTimestampValue
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
                            
                            // Get the timestamp as a bigint value directly from SQL
                            long timestampValue = 0;
                            var timestampValueOrdinal = reader.GetOrdinal("TimestampValue");
                            if (!reader.IsDBNull(timestampValueOrdinal))
                            {
                                timestampValue = reader.GetInt64(timestampValueOrdinal);
                                if (VerboseLogging && loadedCount <= 10)
                                {
                                    Console.WriteLine($"DB TIMESTAMP DEBUG: NodeId={nodeId}, Timestamp bigint value: {timestampValue}");
                                }
                            }

                            long versionTimestampValue = 0;
                            var versionTimestampValueOrdinal = reader.GetOrdinal("VersionTimestampValue");
                            if (!reader.IsDBNull(versionTimestampValueOrdinal))
                            {
                                versionTimestampValue = reader.GetInt64(versionTimestampValueOrdinal);
                                if (VerboseLogging && loadedCount <= 10)
                                {
                                    Console.WriteLine($"DB VERSION TIMESTAMP DEBUG: NodeId={nodeId}, VersionTimestamp bigint value: {versionTimestampValue}");
                                }
                            }

                        
                            loadedCount++;
                            if (loadedCount % logInterval == 0)
                            {
                                Console.WriteLine($"DB LOAD PROGRESS: Loaded {loadedCount} items from database");
                            }
                            
                            // Normalize node type when adding from database
                            var nodeType = reader.GetString(reader.GetOrdinal("NodeTypeName")).ToLowerInvariant();                            items.Add(new ContentItem
                            {
                                NodeId = nodeId,
                                VersionId = versionId,
                                // All timestamp handling done with TimestampNumeric
                                TimestampNumeric = timestampValue, // Store the bigint value for comparison
                                VersionTimestampNumeric = versionTimestampValue, // Store version timestamp value
                                Path = nodePath,
                                NodeType = nodeType, // Node type already normalized to lowercase
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
                            var versionTimeStamp = doc.Get("VersionTimestampNumeric") ?? string.Empty;
                            
                            // Debug output for timestamp values
                            if (VerboseLogging && batchCount <= 10)
                            {
                                Console.WriteLine($"INDEX TIMESTAMP DEBUG: NodeId={nodeId}, Path={docPath}");
                                Console.WriteLine($"  Raw timestamp value: '{timestamp}'");
                                if (long.TryParse(timestamp, out long numericTs))
                                {
                                    Console.WriteLine($"  Parsed as numeric value: {numericTs}");
                                }
                                else
                                {
                                    Console.WriteLine($"  Failed to parse timestamp as numeric value");
                                    // Try to get timestamp from other fields
                                    var allFields = doc.GetFields().Select(f => $"{f.Name}: {f.StringValue}").ToList();
                                    Console.WriteLine($"  Available fields: \n    {string.Join("\n    ", allFields)}");
                                }
                            }
                            
                            batchCount++;
                            // Log every 20000th item as a sample
                            if (VerboseLogging && ((totalProcessed + batchCount) % 20000 == 0 || batchCount <= 5))
                            {
                                Console.WriteLine($"INDEX SAMPLE: NodeId={nodeId}, VersionId={versionId}, Timestamp={timestamp}, Path='{docPath}', NormalizedPath='{NormalizePath(docPath)}', Type='{type}'");
                            }

                            items.Add(new ContentItem
                            {
                                NodeId = 0,  // We'll update this if we find a matching DB item
                                VersionId = 0, // We'll update this if we find a matching DB item
                                // All timestamp handling done with TimestampNumeric
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

        public List<ContentItem> CompareContent(string indexPath, string connectionString, string repositoryPath, bool recursive, int depth)
        {
            if (!Program.IsValidLuceneIndex(indexPath))
            {
                throw new InvalidOperationException($"The directory does not appear to be a valid Lucene index: {indexPath}");
            }

            Console.WriteLine($"STARTING COMPARISON: IndexPath={indexPath}, RepositoryPath={repositoryPath}, Recursive={recursive}, Depth={depth}");
            
            // Get items from database and mark as database items 
            Console.WriteLine("LOADING DATABASE ITEMS...");
            var dbItems = GetContentItemsFromDatabase(connectionString, repositoryPath, recursive, depth)
                .Select(i => { i.InDatabase = true; return i; });
            
            var dbItemsCount = dbItems.Count();
            Console.WriteLine($"DATABASE ITEMS LOADED: {dbItemsCount} items");

            // Get items from index and mark as index items
            Console.WriteLine("LOADING INDEX ITEMS...");
            var indexItems = GetContentItemsFromIndex(indexPath, repositoryPath, recursive, depth)
                .Select(i => { i.InIndex = true; return i; });
            
            var indexItemsCount = indexItems.Count();
            Console.WriteLine($"INDEX ITEMS LOADED: {indexItemsCount} items");            // Combine and group items by path for side-by-side comparison
            Console.WriteLine("MERGING DATABASE AND INDEX ITEMS...");
            var pathComparer = StringComparer.OrdinalIgnoreCase; // Use case-insensitive comparison for paths
            
            // Create dictionaries for faster lookup - use dictionaries with NodeId as a secondary key
            var dbItemsByNormalizedPathAndId = new Dictionary<string, Dictionary<string, ContentItem>>(pathComparer);
            var pathNormalizationCount = 0;
            
            foreach (var item in dbItems)
            {
                var normalizedPath = NormalizePath(item.Path);
                if (normalizedPath != item.Path.ToLowerInvariant().TrimEnd('/'))
                {
                    pathNormalizationCount++;
                }
                
                if (!dbItemsByNormalizedPathAndId.ContainsKey(normalizedPath))
                {
                    dbItemsByNormalizedPathAndId[normalizedPath] = new Dictionary<string, ContentItem>();
                }
                
                string nodeIdKey = item.NodeId.ToString();
                dbItemsByNormalizedPathAndId[normalizedPath][nodeIdKey] = item;
                
                if (VerboseLogging && dbItemsByNormalizedPathAndId[normalizedPath].Count > 1)
                {
                    Console.WriteLine($"INFO: Multiple versions found in database for path: '{normalizedPath}'");
                    Console.WriteLine($"  Adding version: NodeId={item.NodeId}, VersionId={item.VersionId}, Path='{item.Path}'");
                }
            }
            
            Console.WriteLine($"PATH NORMALIZATION: {pathNormalizationCount} database paths were normalized beyond simple case conversion and trailing slash removal");
            
            var results = new List<ContentItem>();
            var matchedCount = 0;
            var indexOnlyCount = 0;
            var pathMismatchSamples = 0;
            const int MaxPathMismatchSamples = 10;

            // Process index items and match against database items
            foreach (var indexItem in indexItems)
            {
                var indexNormalizedPath = NormalizePath(indexItem.Path);
                bool matched = false;

                if (dbItemsByNormalizedPathAndId.TryGetValue(indexNormalizedPath, out var pathItems))
                {
                    // Try to match by NodeId first if available
                    if (!string.IsNullOrEmpty(indexItem.IndexNodeId) &&
                        pathItems.TryGetValue(indexItem.IndexNodeId, out var matchedDbItem) &&
                        string.Equals(matchedDbItem.VersionId.ToString(), indexItem.IndexVersionId)) // Also check version match
                    {
                        // Found exact match by path, ID AND version
                        matchedCount++;

                        // Log sample matches
                        if (VerboseLogging && (matchedCount <= 5 || matchedCount % 10000 == 0))
                        {
                            Console.WriteLine($"MATCH FOUND: Index path '{indexItem.Path}' -> DB path '{matchedDbItem.Path}' (NodeIds: Index={indexItem.IndexNodeId}, DB={matchedDbItem.NodeId})");
                        }

                        // Merge the data - copy index info to database item
                        matchedDbItem.InIndex = true;
                        matchedDbItem.IndexNodeId = indexItem.IndexNodeId;
                        matchedDbItem.IndexVersionId = indexItem.IndexVersionId;
                        matchedDbItem.IndexTimestamp = indexItem.IndexTimestamp;
                        matchedDbItem.IndexVersionTimestamp = indexItem.IndexVersionTimestamp;

                        // Mark as processed by removing the specific NodeId entry
                        pathItems.Remove(indexItem.IndexNodeId);
                        if (pathItems.Count == 0)
                        {
                            // If no more items at this path, remove the path entry
                            dbItemsByNormalizedPathAndId.Remove(indexNormalizedPath);
                        }

                        // Add to results
                        results.Add(matchedDbItem);
                        matched = true;
                    }
                    else if (!string.IsNullOrEmpty(indexItem.IndexNodeId) && pathItems.Count > 0)
                    {
                        // We have a NodeId match but version mismatch, or just a path match
                        // In either case, this is a distinct item and should be shown separately

                        // Check if we have a NodeId match but version mismatch (different version of same content)
                        if (pathItems.TryGetValue(indexItem.IndexNodeId, out var sameIdDifferentVersion))
                        {
                            if (VerboseLogging)
                            {
                                Console.WriteLine($"VERSION MISMATCH AT SAME PATH AND ID: Found index item at path '{indexItem.Path}' with ID {indexItem.IndexNodeId}");
                                Console.WriteLine($"  Index version: {indexItem.IndexVersionId}, DB version: {sameIdDifferentVersion.VersionId}");
                            }
                        }
                        else if (VerboseLogging)
                        {
                            Console.WriteLine($"DIFFERENT ID AT SAME PATH: Found index item at path '{indexItem.Path}' with ID {indexItem.IndexNodeId}");
                            Console.WriteLine($"  This path already has {pathItems.Count} item(s) in the database with different IDs");
                        }

                        // Add as a separate index-only item
                        indexOnlyCount++;
                        results.Add(indexItem);
                        matched = true;
                    }
                }

                if (!matched)
                {
                    // Before definitively marking as orphaned, check if any database items have 
                    // a matching NodeId AND VersionId, regardless of path - this handles path renames
                    var matchByIdOnly = dbItems.FirstOrDefault(db =>
                        db.NodeId.ToString() == indexItem.IndexNodeId &&
                        !db.InIndex && // Make sure we haven't already matched this DB item
                        db.VersionId.ToString() == indexItem.IndexVersionId); // Must match BOTH ID and version

                    if (matchByIdOnly != null)
                    {
                        // We found a match by both NodeId and VersionId but the paths differ - this is likely a renamed item
                        if (VerboseLogging)
                        {
                            Console.WriteLine($"ID-ONLY MATCH: Index item path='{indexItem.Path}' matched to DB item with different path='{matchByIdOnly.Path}'");
                            Console.WriteLine($"  This suggests the item was renamed in the database but the index wasn't updated.");
                            Console.WriteLine($"  Matched by NodeId={matchByIdOnly.NodeId} and VersionId={matchByIdOnly.VersionId}");
                        }

                        // Merge the data - copy index info to database item
                        matchByIdOnly.InIndex = true;
                        matchByIdOnly.IndexNodeId = indexItem.IndexNodeId;
                        matchByIdOnly.IndexVersionId = indexItem.IndexVersionId;
                        matchByIdOnly.IndexTimestamp = indexItem.IndexTimestamp;

                        // Remove this item from any path collections
                        string matchIdKey = matchByIdOnly.NodeId.ToString();
                        string matchNormalizedPath = NormalizePath(matchByIdOnly.Path);
                        if (dbItemsByNormalizedPathAndId.TryGetValue(matchNormalizedPath, out var matchPathItems) &&
                            matchPathItems.ContainsKey(matchIdKey))
                        {
                            matchPathItems.Remove(matchIdKey);
                            if (matchPathItems.Count == 0)
                            {
                                dbItemsByNormalizedPathAndId.Remove(matchNormalizedPath);
                            }
                        }

                        // Add to results
                        results.Add(matchByIdOnly);
                        matched = true;
                        matchedCount++;
                    }
                    else
                    {
                        // Item exists only in the index - truly orphaned
                        indexOnlyCount++;

                        // Log sample mismatches
                        if (VerboseLogging && pathMismatchSamples < MaxPathMismatchSamples)
                        {
                            pathMismatchSamples++;
                            Console.WriteLine($"PATH MISMATCH SAMPLE {pathMismatchSamples}:");
                            Console.WriteLine($"  Index path: '{indexItem.Path}'");
                            Console.WriteLine($"  Normalized: '{indexNormalizedPath}'");
                            Console.WriteLine($"  NodeId: {indexItem.IndexNodeId}");

                            // Find similar paths
                            var similarPaths = dbItemsByNormalizedPathAndId.Keys
                                .Where(k => k.Contains(indexNormalizedPath, StringComparison.OrdinalIgnoreCase) ||
                                           indexNormalizedPath.Contains(k, StringComparison.OrdinalIgnoreCase))
                                .Take(3)
                                .ToList();

                            if (similarPaths.Any())
                            {
                                Console.WriteLine("  Similar paths in database:");
                                foreach (var similarPath in similarPaths)
                                {
                                    var firstSimilarDbItem = dbItemsByNormalizedPathAndId[similarPath].Values.First();
                                    Console.WriteLine($"    Path: '{firstSimilarDbItem.Path}'");
                                    Console.WriteLine($"    Normalized: '{similarPath}'");
                                    Console.WriteLine($"    NodeId: {firstSimilarDbItem.NodeId}");
                                }
                            }

                            // Check if NodeId matches any database items
                            var matchingNodeIds = dbItems.Where(db => db.NodeId.ToString() == indexItem.IndexNodeId).Take(3).ToList();
                            if (matchingNodeIds.Any())
                            {
                                Console.WriteLine("  POTENTIAL MATCHES BY NODEID:");
                                foreach (var match in matchingNodeIds)
                                {
                                    Console.WriteLine($"    Path: '{match.Path}'");
                                    Console.WriteLine($"    Normalized: '{NormalizePath(match.Path)}'");
                                    Console.WriteLine($"    NodeId: {match.NodeId}");
                                }
                            }

                            Console.WriteLine();
                        }

                        // Add to results
                        results.Add(indexItem);
                    }
                }
            }
            
            // Add remaining database items (those not found in the index)
            int remainingDbItems = 0;
            foreach (var pathDict in dbItemsByNormalizedPathAndId)
            {
                foreach (var item in pathDict.Value.Values)
                {
                    results.Add(item);
                    remainingDbItems++;
                }
            }
            Console.WriteLine($"ADDING REMAINING DATABASE ITEMS: {remainingDbItems} items");
            
            // Filter by depth if specified
            if (depth > 0)
            {
                var basePath = repositoryPath.TrimEnd('/');
                var baseDepth = basePath.Count(c => c == '/');
                var beforeCount = results.Count;
                
                results = results.Where(item => {
                    var itemDepth = item.Path.Count(c => c == '/') - baseDepth;
                    return itemDepth <= depth;
                }).ToList();
                
                Console.WriteLine($"DEPTH FILTERING: Filtered out {beforeCount - results.Count} items exceeding depth {depth}");
            }
              // Summary            Console.WriteLine();
            Console.WriteLine("COMPARISON SUMMARY:");
            Console.WriteLine($"- Database items: {dbItemsCount}");
            Console.WriteLine($"- Index items: {indexItemsCount}");
            Console.WriteLine($"- Matched items: {matchedCount}");
            Console.WriteLine($"- Database-only items: {remainingDbItems}");
            Console.WriteLine($"- Index-only items: {indexOnlyCount}");
            Console.WriteLine($"- Total unique items: {results.Count} (unique by path, ID, and version)");
            
            return results.OrderBy(i => i.Path).ToList();
        }    }
}
