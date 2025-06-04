using System.CommandLine;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using IODirectory = System.IO.Directory;
using BooleanClause = Lucene.Net.Search.BooleanClause;

namespace SenseNetIndexTools
{
    public class IndexLister
    {
        public static Command Create()
        {
            var command = new Command("list-index", "List content items from the index");

            // Add path option for the index
            var indexPathOption = new Option<string>(
                name: "--index-path",
                description: "Path to the Lucene index directory");
            indexPathOption.IsRequired = true;

            // Add repository path option
            var repositoryPathOption = new Option<string>(
                name: "--repository-path",
                description: "Path in the content repository to check (e.g., /Root/Sites/Default_Site)");
            repositoryPathOption.IsRequired = true;

            // Add recursive option
            var recursiveOption = new Option<bool>(
                name: "--recursive",
                description: "Recursively list all content items under the specified path",
                getDefaultValue: () => true);

            // Add depth option
            var depthOption = new Option<int>(
                name: "--depth",
                description: "Limit listing to specified depth (1=direct children only, 0=all descendants)",
                getDefaultValue: () => 0);

            command.AddOption(indexPathOption);
            command.AddOption(repositoryPathOption);
            command.AddOption(recursiveOption);
            command.AddOption(depthOption);
            command.SetHandler((context) =>
            {
                var indexPath = context.ParseResult.GetValueForOption(indexPathOption) ?? string.Empty;
                var repositoryPath = context.ParseResult.GetValueForOption(repositoryPathOption) ?? string.Empty;
                var recursive = context.ParseResult.GetValueForOption(recursiveOption);
                var depth = context.ParseResult.GetValueForOption(depthOption);

                if (string.IsNullOrEmpty(indexPath) || string.IsNullOrEmpty(repositoryPath))
                {
                    Console.Error.WriteLine("Index path and repository path are required.");
                    return Task.CompletedTask;
                }

                return ListIndexItems(indexPath, repositoryPath, recursive, depth);
            });

            return command;
        }
        private static Task ListIndexItems(string indexPath, string path, bool recursive, int depth = 0)
        {
            if (!IODirectory.Exists(indexPath))
            {
                Console.Error.WriteLine($"Index directory not found: {indexPath}");
                return Task.CompletedTask;
            }

            try
            {
                using var directory = FSDirectory.Open(new DirectoryInfo(indexPath));
                using var reader = IndexReader.Open(directory, true);
                using var searcher = new IndexSearcher(reader); Console.WriteLine($"Opening index with {reader.MaxDoc()} total documents");
                Console.WriteLine($"Searching for path: {path} (Recursive: {recursive}, Depth: {depth})");

                // Convert path to lowercase for SenseNet indexes which store paths in lowercase
                var normalizedPath = path.ToLowerInvariant(); Query query;
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
                }                // Create a list to hold all the items we want to display
                var items = new List<(string Id, string VersionId, string Path, string Type)>();

                int pathSegmentCount = normalizedPath.Split('/').Length - 1;

                // Implement paging for large indexes
                const int PageSize = 10000; // Process 10000 documents at a time
                int totalProcessed = 0;

                // Create a collector to find all matching documents
                var initialCollector = TopScoreDocCollector.Create(1000000, true); // Use a large limit
                searcher.Search(query, initialCollector);
                var topDocs = initialCollector.TopDocs();
                int totalHits = topDocs.TotalHits;

                Console.WriteLine($"Found {totalHits} items in index matching the query...");

                // Process in batches to avoid memory issues
                while (totalProcessed < totalHits)
                {
                    var collector = TopScoreDocCollector.Create(1000000, true); // Use a large limit
                    searcher.Search(query, collector);
                    var searchHits = collector.TopDocs(totalProcessed, Math.Min(PageSize, totalHits - totalProcessed)).ScoreDocs;

                    if (searchHits.Length == 0) break; // No more results

                    foreach (var hitDoc in searchHits)
                    {
                        var doc = searcher.Doc(hitDoc.Doc);
                        var docPath = doc.Get("Path") ?? "?";

                        // Skip if we're filtering by depth and this item exceeds our depth
                        if (depth == 1 && recursive)
                        {
                            // Count path segments to determine depth
                            int docPathSegments = docPath.Split('/').Length - 1;
                            if (docPathSegments > pathSegmentCount + 1)
                                continue;
                        }

                        // Get values with fallbacks for different field names
                        var id = doc.Get("Id") ?? doc.Get("NodeId") ?? "?";
                        var versionId = doc.Get("VersionId") ?? "?";
                        var type = doc.Get("Type") ?? doc.Get("NodeType") ?? "?";
                        items.Add((id, versionId, docPath, type));
                    }

                    totalProcessed += searchHits.Length;
                }
                // Sort the items by path (case-insensitive)
                items = items.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToList();

                Console.WriteLine($"\nFound {items.Count} items in index under path {path}:");

                if (items.Count > 0)
                {
                    Console.WriteLine("ID\tVersionId\tPath\tType");
                    Console.WriteLine(new string('-', 80));

                    foreach (var item in items)
                    {
                        Console.WriteLine($"{item.Id}\t{item.VersionId}\t{item.Path}\t{item.Type}");
                    }
                }
                else
                {
                    // If no results, show some general index stats and suggestions
                    Console.WriteLine("Could not find any items with the specified path.");
                    Console.WriteLine($"Total documents in index: {reader.MaxDoc()}");
                    Console.WriteLine("Suggestions:");
                    Console.WriteLine("1. Check if the path is correct");
                    Console.WriteLine("2. Try searching with a parent path");
                    Console.WriteLine("3. The index might use different field names for paths");

                    // Show a sample document to help diagnose field names
                    if (reader.MaxDoc() > 0)
                    {
                        Console.WriteLine("\nSample document fields for reference:");
                        var sampleDoc = reader.Document(0);
                        foreach (var field in sampleDoc.GetFields())
                        {
                            Console.WriteLine($"  - {field.Name()}: {field.StringValue()}");
                        }
                    }
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error reading index: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return Task.CompletedTask;
            }
        }
    }
}
