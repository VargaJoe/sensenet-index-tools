using System.CommandLine;
using System.Data;
using System.Data.SqlClient;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage;
using SenseNet.Search;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29;
using IOFile = System.IO.File;
using IODirectory = System.IO.Directory;

namespace SenseNetIndexTools
{
    public class SubtreeIndexChecker
    {
        public static Command Create()
        {
            var command = new Command("check-subtree", "Check if content items from a database subtree exist in the index");

            // Add path option for the index
            var indexPathOption = new Option<string>(
                name: "--index-path", 
                description: "Path to the Lucene index directory");
            indexPathOption.IsRequired = true;

            // Add connection string option
            var connectionStringOption = new Option<string>(
                name: "--connection-string",
                description: "SQL Connection string to the SenseNet database");
            connectionStringOption.IsRequired = true;

            // Add repository path option
            var repositoryPathOption = new Option<string>(
                name: "--repository-path",
                description: "Path in the content repository to check (e.g., /Root/Sites/Default_Site)");
            repositoryPathOption.IsRequired = true;

            // Option for output file
            var outputOption = new Option<string?>(
                name: "--output",
                description: "Path to save the check report to a file");

            // Option for recursive check
            var recursiveOption = new Option<bool>(
                name: "--recursive",
                description: "Recursively check all content items under the specified path",
                getDefaultValue: () => true);

            // Option for detailed report
            var detailedOption = new Option<bool>(
                name: "--detailed",
                description: "Generate a detailed report with comprehensive information about mismatches",
                getDefaultValue: () => false);

            // Add options to the command
            command.AddOption(indexPathOption);
            command.AddOption(connectionStringOption);
            command.AddOption(repositoryPathOption);
            command.AddOption(outputOption);
            command.AddOption(recursiveOption);
            command.AddOption(detailedOption);

            // Set handler for the command
            command.SetHandler((string indexPath, string connectionString, string repositoryPath, string? output, bool recursive, bool detailed) =>
            {
                try
                {
                    // First verify this is a valid Lucene index
                    if (!Program.IsValidLuceneIndex(indexPath))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {indexPath}");
                        Environment.Exit(1);
                        return;
                    }

                    Console.WriteLine($"Opening index directory: {indexPath}");
                    Console.WriteLine($"Repository path to check: {repositoryPath}");
                    Console.WriteLine($"Recursive mode: {(recursive ? "Yes" : "No")}");

                    var report = PerformSubtreeCheck(indexPath, connectionString, repositoryPath, recursive, detailed);

                    // Display summary
                    Console.WriteLine();
                    Console.WriteLine(report.Summary);                    // Save to file if requested
                    if (!string.IsNullOrEmpty(output))
                    {
                        IOFile.WriteAllText(output, report.DetailedReport);
                        Console.WriteLine($"Detailed report saved to: {output}");
                    }
                    
                    // Display detailed information on console if requested and not too large
                    if (detailed && report.MismatchedItems.Count <= 20)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Mismatched Items:");
                        foreach (var item in report.MismatchedItems)
                        {
                            Console.WriteLine($"  {item}");
                        }
                    }
                    else if (report.MismatchedItems.Count > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Found {report.MismatchedItems.Count} mismatched items. " +
                            $"{(string.IsNullOrEmpty(output) ? "Use --output option to save a detailed report to a file." : "")}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error during subtree check: {ex.Message}");
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    Environment.Exit(1);
                }
            }, indexPathOption, connectionStringOption, repositoryPathOption, outputOption, recursiveOption, detailedOption);

            return command;
        }

        private static CheckReport PerformSubtreeCheck(
            string indexPath, 
            string connectionString, 
            string repositoryPath,
            bool recursive, 
            bool detailed)
        {
            var report = new CheckReport
            {
                StartTime = DateTime.Now,
                RepositoryPath = repositoryPath,
                Recursive = recursive,
                Summary = string.Empty,
                DetailedReport = string.Empty
            };

            try
            {
                // Get content items from database
                var contentItems = GetContentItemsFromDatabase(connectionString, repositoryPath, recursive);
                report.DatabaseItemsCount = contentItems.Count;

                if (contentItems.Count == 0)
                {
                    report.Summary = $"No content items found at path '{repositoryPath}' in the database.";
                    report.EndTime = DateTime.Now;
                    return report;
                }

                Console.WriteLine($"Found {contentItems.Count} content items in the database.");

                // Check items in the index
                using (var directory = FSDirectory.Open(new DirectoryInfo(indexPath)))
                {
                    if (IndexReader.IndexExists(directory))
                    {
                        using (var reader = IndexReader.Open(directory, true))
                        {
                            report.IndexDocCount = reader.NumDocs();
                            
                            Console.WriteLine($"Index contains {report.IndexDocCount} documents.");
                            Console.WriteLine("Checking database items against index...");

                            int processedCount = 0;
                            foreach (var item in contentItems)
                            {
                                processedCount++;
                                if (processedCount % 100 == 0)
                                {
                                    Console.Write($"\rProcessed {processedCount}/{contentItems.Count} items...");
                                }
                                // Check if the item exists in the index
                                bool foundInIndex = CheckItemInIndex(reader, item.NodeId, item.VersionId, item.Path);
                                
                                if (!foundInIndex)
                                {
                                    report.MismatchedItems.Add(new MismatchedItem
                                    {
                                        NodeId = item.NodeId,
                                        VersionId = item.VersionId,
                                        Path = item.Path,
                                        NodeType = item.NodeType,
                                        Reason = "Item exists in database but not in index"
                                    });
                                }
                                else
                                {
                                    report.MatchedItemsCount++;
                                }
                            }

                            Console.WriteLine();
                            Console.WriteLine($"Check completed. Found {report.MismatchedItems.Count} mismatched items.");
                        }
                    }
                    else
                    {
                        report.Summary = "Index does not exist or cannot be opened.";
                        return report;
                    }
                }

                // Generate summary
                report.EndTime = DateTime.Now;
                TimeSpan duration = report.EndTime - report.StartTime;
                
                report.Summary = $"Check completed in {duration.TotalSeconds:F2} seconds.\n" +
                    $"Database items: {report.DatabaseItemsCount}\n" +
                    $"Index documents: {report.IndexDocCount}\n" +
                    $"Matched items: {report.MatchedItemsCount}\n" +
                    $"Mismatched items: {report.MismatchedItems.Count}\n" +
                    $"Matching percentage: {(report.DatabaseItemsCount > 0 ? (double)report.MatchedItemsCount / report.DatabaseItemsCount * 100 : 0):F2}%";

                // Generate detailed report
                if (detailed)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("# Subtree Index Check Report");
                    sb.AppendLine();
                    sb.AppendLine($"- **Start Time**: {report.StartTime}");
                    sb.AppendLine($"- **End Time**: {report.EndTime}");
                    sb.AppendLine($"- **Duration**: {duration.TotalSeconds:F2} seconds");
                    sb.AppendLine($"- **Repository Path**: {report.RepositoryPath}");
                    sb.AppendLine($"- **Recursive**: {report.Recursive}");
                    sb.AppendLine($"- **Database Items**: {report.DatabaseItemsCount}");
                    sb.AppendLine($"- **Index Documents**: {report.IndexDocCount}");
                    sb.AppendLine($"- **Matched Items**: {report.MatchedItemsCount}");
                    sb.AppendLine($"- **Mismatched Items**: {report.MismatchedItems.Count}");
                    sb.AppendLine($"- **Matching Percentage**: {(report.DatabaseItemsCount > 0 ? (double)report.MatchedItemsCount / report.DatabaseItemsCount * 100 : 0):F2}%");
                    sb.AppendLine();

                    if (report.MismatchedItems.Count > 0)
                    {
                        sb.AppendLine("## Mismatched Items");
                        sb.AppendLine();
                        sb.AppendLine("| NodeId | VersionId | Path | NodeType | Reason |");
                        sb.AppendLine("|--------|-----------|------|----------|--------|");
                        
                        foreach (var item in report.MismatchedItems)
                        {
                            sb.AppendLine($"| {item.NodeId} | {item.VersionId} | {item.Path} | {item.NodeType} | {item.Reason} |");
                        }
                    }

                    report.DetailedReport = sb.ToString();
                }

                return report;
            }
            catch (Exception ex)
            {
                report.EndTime = DateTime.Now;
                report.Summary = $"Error during check: {ex.Message}";
                throw;
            }
        }

        private static List<ContentItem> GetContentItemsFromDatabase(string connectionString, string path, bool recursive)
        {
            var items = new List<ContentItem>();
            
            // Sanitize path for SQL query
            string sanitizedPath = path.Replace("'", "''");
              // Build the SQL query
            string sql;
            if (recursive)
            {
                sql = @"
                    SELECT N.NodeId, V.Id as VersionId, N.Path, NT.Name as NodeTypeName 
                    FROM Nodes N
                    JOIN Versions V ON N.NodeId = V.NodeId
                    JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                    WHERE (N.Path = @path OR N.Path LIKE @pathPattern)
                    ORDER BY N.Path";
            }
            else
            {
                sql = @"
                    SELECT N.NodeId, V.Id as VersionId, N.Path, NT.Name as NodeTypeName 
                    FROM Nodes N
                    JOIN Versions V ON N.NodeId = V.NodeId
                    JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                    WHERE N.Path = @path
                    ORDER BY N.Path";
            }

            // Execute the query
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
                        {                            items.Add(new ContentItem
                            {
                                NodeId = reader.GetInt32(reader.GetOrdinal("NodeId")),
                                VersionId = reader.GetInt32(reader.GetOrdinal("VersionId")),
                                Path = reader.GetString(reader.GetOrdinal("Path")),
                                NodeType = reader.GetString(reader.GetOrdinal("NodeTypeName"))
                            });
                        }
                    }
                }
            }
            
            return items;
        }

        private static bool CheckItemInIndex(IndexReader reader, int nodeId, int versionId, string path = null)
        {
            // List of search methods to try
            var searchMethods = new List<(string Method, Func<bool> Search)>
            {
                // Method 1: Search by Path
                ("Path", () => {
                    var pathTerm = new Term("Path", path);
                    var pathDocs = reader.TermDocs(pathTerm);
                    return pathDocs.Next();
                }),
                
                // Method 2: Search by VersionId
                ("VersionId", () => {
                    var versionTerm = new Term("VersionId", NumericUtils.IntToPrefixCoded(versionId));
                    var versionDocs = reader.TermDocs(versionTerm);
                    return versionDocs.Next();
                }),

                // Method 3: Direct scan as last resort for small indexes
                ("DirectScan", () => {
                    if (reader.MaxDoc() > 10000) 
                    {
                        return false; // Skip for large indexes
                    }
                    
                    Console.WriteLine($"Attempting direct scan for item with VersionId {versionId} (last resort)");
                    
                    for (int i = 0; i < reader.MaxDoc(); i++)
                    {
                        if (reader.IsDeleted(i)) continue;
                        
                        var doc = reader.Document(i);
                        var fields = doc.GetFields();
                        foreach (var field in fields)
                        {
                            if (field.Name == "VersionId" && field.StringValue() == NumericUtils.IntToPrefixCoded(versionId))
                            {
                                Console.WriteLine($"Found item in document #{i} by direct scan");
                                return true;
                            }
                        }
                    }
                    return false;
                })
            };
            
            // Try each search method
            foreach (var (method, search) in searchMethods)
            {
                try
                {
                    if (search())
                    {
                        Console.WriteLine($"Found item (VersionId={versionId}) using {method}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during {method} search: {ex.Message}");
                }
            }
            
            Console.WriteLine($"MISSING: Item not found in index: VersionId={versionId}, Path={path}");
            return false;
        }        private class ContentItem
        {
            public int NodeId { get; set; }
            public int VersionId { get; set; }
            public required string Path { get; set; }
            public required string NodeType { get; set; }
        }            private class MismatchedItem
        {
            public int NodeId { get; set; }
            public int VersionId { get; set; }
            public required string Path { get; set; }
            public required string NodeType { get; set; }
            public required string Reason { get; set; }

            public override string ToString()
            {
                return $"NodeId: {NodeId}, VersionId: {VersionId}, Path: {Path}, NodeType: {NodeType}, Reason: {Reason}";
            }
        }private class CheckReport
        {
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public required string RepositoryPath { get; set; }
            public bool Recursive { get; set; }
            public int DatabaseItemsCount { get; set; }
            public int IndexDocCount { get; set; }            public int MatchedItemsCount { get; set; }
            public List<MismatchedItem> MismatchedItems { get; set; } = new List<MismatchedItem>();
            public Dictionary<string, int> ContentTypeStats { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> MismatchesByType { get; set; } = new Dictionary<string, int>();
            public required string Summary { get; set; }
            public required string DetailedReport { get; set; }
        }
    }
}
