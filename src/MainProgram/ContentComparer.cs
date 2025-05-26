using System.CommandLine;
using System.Data.SqlClient;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using IODirectory = System.IO.Directory;

namespace SenseNetIndexTools
{
    public class ContentComparer
    {
        public class ContentItem
        {
            public int NodeId { get; set; }
            public int VersionId { get; set; }
            public string Path { get; set; } = string.Empty;
            public string NodeType { get; set; } = string.Empty;
            public bool InDatabase { get; set; }
            public bool InIndex { get; set; }
            public string? IndexNodeId { get; set; }
            public string? IndexVersionId { get; set; }
            
            public string Status => 
                InDatabase && InIndex 
                    ? (NodeId.ToString() != IndexNodeId || VersionId.ToString() != IndexVersionId)
                        ? "ID Mismatch"
                        : "Match"
                    : InDatabase 
                        ? "DB Only" 
                        : "Index Only";
        }

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

            var outputOption = new Option<string?>(
                name: "--output",
                description: "Path to save the comparison report to a markdown file");

            command.AddOption(indexPathOption);
            command.AddOption(connectionStringOption);
            command.AddOption(repositoryPathOption);
            command.AddOption(recursiveOption);
            command.AddOption(depthOption);
            command.AddOption(orderByOption);
            command.AddOption(outputOption);
            command.SetHandler((string indexPath, string connectionString, string repositoryPath, bool recursive, int depth, string orderBy, string? output) =>
            {
                try
                {
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
                        .Select(i => { i.InIndex = true; return i; });                    // Combine and group items by path for side-by-side display
                    var items = dbItems.Union(indexItems)
                        .GroupBy(i => i.Path.ToLowerInvariant())
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
                        "type" => items.OrderBy(i => i.NodeType, StringComparer.OrdinalIgnoreCase)
                                      .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList(),
                        _ => items.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList()
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
            }, indexPathOption, connectionStringOption, repositoryPathOption, recursiveOption, depthOption, orderByOption, outputOption);

            return command;
        }

        private static void GenerateReport(List<ContentItem> items, string? outputPath)
        {
            var matchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "Match");
            var mismatchCount = items.Count(i => i.InDatabase && i.InIndex && i.Status == "ID Mismatch");
            var dbOnlyCount = items.Count(i => i.InDatabase && !i.InIndex);
            var indexOnlyCount = items.Count(i => !i.InDatabase && i.InIndex);

            // Build report in markdown format
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Content Comparison Report");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- Total unique paths: {items.Count}");
            sb.AppendLine($"- Perfect matches: {matchCount}");
            sb.AppendLine($"- ID mismatches: {mismatchCount}");
            sb.AppendLine($"- Database only: {dbOnlyCount}");
            sb.AppendLine($"- Index only: {indexOnlyCount}");
            sb.AppendLine();
            
            sb.AppendLine("## Comparison Results");
            sb.AppendLine();
            sb.AppendLine("| Status | DB NodeId | DB VerID | Index NodeId | Index VerID | Path | Type |");
            sb.AppendLine("|---------|-----------|----------|--------------|-------------|------|------|");

            foreach (var item in items.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
            {
                var dbNodeId = item.InDatabase ? item.NodeId.ToString() : "-";
                var dbVerID = item.InDatabase ? item.VersionId.ToString() : "-";
                var idxNodeId = item.InIndex ? item.IndexNodeId : "-";
                var idxVerID = item.InIndex ? item.IndexVersionId : "-";
                
                sb.AppendLine($"| {item.Status} | {dbNodeId} | {dbVerID} | {idxNodeId} | {idxVerID} | {item.Path} | {item.NodeType} |");
            }

            // Always show summary on console
            Console.WriteLine($"\nFound {items.Count} unique paths:");
            Console.WriteLine($"Perfect matches: {matchCount}");
            Console.WriteLine($"ID mismatches: {mismatchCount}");
            Console.WriteLine($"Database only: {dbOnlyCount}");
            Console.WriteLine($"Index only: {indexOnlyCount}");

            // Save to file if output path is specified
            if (!string.IsNullOrEmpty(outputPath))
            {
                File.WriteAllText(outputPath, sb.ToString());
                Console.WriteLine($"\nDetailed report saved to: {outputPath}");
            }
            else
            {
                // Show items on console in a simpler format
                Console.WriteLine("\nDB_NodeId\tDB_VerID\tIdx_NodeId\tIdx_VerID\tPath\tNodeType\tStatus");
                Console.WriteLine(new string('-', 120));

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

            string sql = recursive
                ? @"SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName 
                    FROM Nodes N
                    JOIN Versions V ON N.NodeId = V.NodeId
                    JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                    WHERE (LOWER(N.Path) = LOWER(@path) OR LOWER(N.Path) LIKE LOWER(@pathPattern))
                    ORDER BY N.Path"
                : @"SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName 
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
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            items.Add(new ContentItem
                            {
                                NodeId = reader.GetInt32(reader.GetOrdinal("NodeId")),
                                VersionId = reader.GetInt32(reader.GetOrdinal("VersionId")),
                                Path = reader.GetString(reader.GetOrdinal("Path")),
                                NodeType = reader.GetString(reader.GetOrdinal("NodeTypeName")),
                                InDatabase = true,
                                InIndex = false
                            });
                        }
                    }
                }
            }
            
            return items;
        }

        private static List<ContentItem> GetContentItemsFromIndex(string indexPath, string path, bool recursive, int depth)
        {
            var items = new List<ContentItem>();

            using (var directory = FSDirectory.Open(new DirectoryInfo(indexPath)))
            {
                using (var reader = IndexReader.Open(directory, true))
                using (var searcher = new IndexSearcher(reader))
                {
                    // Convert path to lowercase for SenseNet indexes which store paths in lowercase
                    var normalizedPath = path.ToLowerInvariant();
                      Query query;
                    if (!recursive)
                    {
                        // Direct match only - exact path
                        query = new TermQuery(new Term("Path", normalizedPath));
                    }
                    else
                    {
                        var boolQuery = new BooleanQuery();
                        // Root path match
                        boolQuery.Add(new TermQuery(new Term("Path", normalizedPath)), BooleanClause.Occur.SHOULD);

                        // Child path matches
                        var childQuery = new PrefixQuery(new Term("Path", normalizedPath.TrimEnd('/') + "/"));
                        boolQuery.Add(childQuery, BooleanClause.Occur.SHOULD);

                        query = boolQuery;
                    }

                    var collector = TopScoreDocCollector.Create(10000, true);
                    searcher.Search(query, collector);
                    var searchHits = collector.TopDocs().ScoreDocs;
                    
                    foreach (var hitDoc in searchHits)
                    {
                        var doc = searcher.Doc(hitDoc.Doc);
                        
                        var nodeId = doc.Get("Id") ?? doc.Get("NodeId") ?? "0";
                        var versionId = doc.Get("Version_") ?? doc.Get("VersionId") ?? "0";
                        var docPath = doc.Get("Path") ?? string.Empty;
                        var type = doc.Get("Type") ?? doc.Get("NodeType") ?? "Unknown";

                        items.Add(new ContentItem
                        {
                            NodeId = 0,  // We'll update this if we find a matching DB item
                            VersionId = 0, // We'll update this if we find a matching DB item
                            Path = docPath,
                            NodeType = type,
                            InDatabase = false,
                            InIndex = true,
                            IndexNodeId = nodeId,
                            IndexVersionId = versionId
                        });
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
                    var indexItem = g.FirstOrDefault(i => i.InIndex);

                    if (dbItem != null && indexItem != null)
                    {
                        // Found in both - merge the index data into the DB item
                        dbItem.InIndex = true;
                        dbItem.IndexNodeId = indexItem.IndexNodeId;
                        dbItem.IndexVersionId = indexItem.IndexVersionId;
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
    }
}
