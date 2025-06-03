using System;
using System.Linq;
using SenseNetIndexTools;
using Lucene.Net.Store;
using Lucene.Net.Index;
using Lucene.Net.Search;

// Basic test script to verify ContentComparer's handling of duplicate paths with different IDs
namespace TestDuplicatePaths
{
    class Program
    {
        static void Main(string[] args)
        {
            string indexPath = @"./IndexBackups/KelerTestIndex202505271035_backup_20250527_144228";
            string path = "/root/content";
            
            Console.WriteLine("Starting test for duplicate paths with different IDs...");
            
            ContentComparer.VerboseLogging = true;
            
            // Get items from index only
            var indexItems = GetItemsFromIndex(indexPath, path);
            
            // Group them by path to find duplicates
            var duplicates = indexItems
                .GroupBy(i => i.Path.ToLowerInvariant())
                .Where(g => g.Count() > 1)
                .ToList();
                
            Console.WriteLine($"Found {duplicates.Count} paths with multiple items:");
            
            foreach (var group in duplicates)
            {
                Console.WriteLine($"\nPath: {group.Key}");
                Console.WriteLine("Items:");
                
                foreach (var item in group)
                {
                    Console.WriteLine($"  NodeId: {item.IndexNodeId}, VersionId: {item.IndexVersionId}, Type: {item.NodeType}");
                }
            }
            
            Console.WriteLine("\nTest completed.");
        }
        
        static System.Collections.Generic.List<ContentComparer.ContentItem> GetItemsFromIndex(string indexPath, string path)
        {
            var items = new System.Collections.Generic.List<ContentComparer.ContentItem>();
            
            Console.WriteLine($"Loading index items from: {indexPath}");
            Console.WriteLine($"For path: {path}");
            
            using (var directory = FSDirectory.Open(new System.IO.DirectoryInfo(indexPath)))
            {
                using (var reader = IndexReader.Open(directory, true))
                using (var searcher = new IndexSearcher(reader))
                {
                    // Convert path to lowercase for SenseNet indexes which store paths in lowercase
                    var normalizedPath = path.ToLowerInvariant();
                    
                    // Create a query for the path
                    var boolQuery = new BooleanQuery();
                    boolQuery.Add(new TermQuery(new Term("Path", normalizedPath)), BooleanClause.Occur.SHOULD);
                    
                    // Child path matches
                    var childPathPrefix = normalizedPath.TrimEnd('/') + "/";
                    var childQuery = new PrefixQuery(new Term("Path", childPathPrefix));
                    boolQuery.Add(childQuery, BooleanClause.Occur.SHOULD);
                    
                    // Search for matching documents
                    var hits = searcher.Search(boolQuery, 1000);
                    Console.WriteLine($"Found {hits.TotalHits} documents in index");
                    
                    foreach (var hit in hits.ScoreDocs)
                    {
                        var doc = searcher.Doc(hit.Doc);
                        
                        var nodeId = doc.Get("Id") ?? doc.Get("NodeId") ?? "0";
                        var versionId = doc.Get("Version_") ?? doc.Get("VersionId") ?? "0";
                        var docPath = doc.Get("Path") ?? string.Empty;
                        var type = (doc.Get("Type") ?? doc.Get("NodeType") ?? "Unknown").ToLowerInvariant();
                        
                        items.Add(new ContentComparer.ContentItem
                        {
                            NodeId = 0,
                            VersionId = 0,
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
            
            Console.WriteLine($"Loaded {items.Count} items from index");
            return items;
        }
    }
}
