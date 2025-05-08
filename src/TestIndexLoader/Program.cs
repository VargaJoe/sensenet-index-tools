using System;
using System.IO;
using System.Linq;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using SenseNet.Search;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29;
using IODirectory = System.IO.Directory;

namespace TestIndexLoader
{
    class Program
    {
        static void Main(string[] args)
        {
            // Check if path is provided
            if (args.Length == 0)
            {
                Console.WriteLine("Please provide the path to the Lucene index directory as a command-line argument.");
                return;
            }

            string indexPath = args[0];
            
            // Check if directory exists
            if (!IODirectory.Exists(indexPath))
            {
                Console.WriteLine($"Directory not found: {indexPath}");
                return;
            }

            try
            {
                Console.WriteLine($"Trying to load Lucene index from: {indexPath}");
                
                // Check if the directory has basic Lucene index files
                ValidateLuceneIndex(indexPath);
                
                // Try to open the index with SenseNet's IndexDirectory
                var indexDirectory = new IndexDirectory(indexPath);
                Console.WriteLine("Successfully created IndexDirectory object.");
                
                // Try to create the indexing engine
                var engine = new Lucene29LocalIndexingEngine(indexDirectory);
                Console.WriteLine("Successfully created Lucene29LocalIndexingEngine object.");

                // Try to read activity status
                try
                {
                    var status = engine.ReadActivityStatusFromIndexAsync(default).GetAwaiter().GetResult();
                    Console.WriteLine($"Successfully read IndexingActivityStatus: LastActivityId = {status.LastActivityId}");
                    
                    if (status.Gaps?.Any() == true)
                    {
                        Console.WriteLine($"Found {status.Gaps.Length} gaps in activity tracking: {string.Join(", ", status.Gaps)}");
                    }
                    else
                    {
                        Console.WriteLine("No gaps found in activity tracking.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to read activity status: {ex.Message}");
                    Console.WriteLine("Trying to initialize a new activity status...");
                    
                    try
                    {
                        var newStatus = new IndexingActivityStatus
                        {
                            LastActivityId = 1,
                            Gaps = Array.Empty<int>()
                        };
                        
                        engine.WriteActivityStatusToIndexAsync(newStatus, default).GetAwaiter().GetResult();
                        Console.WriteLine("Successfully initialized activity status with LastActivityId = 1");
                        
                        // Verify
                        var verificationStatus = engine.ReadActivityStatusFromIndexAsync(default).GetAwaiter().GetResult();
                        Console.WriteLine($"Verification successful: LastActivityId = {verificationStatus.LastActivityId}");
                    }
                    catch (Exception initEx)
                    {
                        Console.WriteLine($"Failed to initialize activity status: {initEx.Message}");
                        Console.WriteLine("This index may not be compatible with SenseNet's activity tracking mechanism.");
                    }
                }

                // Try to open the index directly with Lucene.NET
                Console.WriteLine("\nAttempting to open index directly with Lucene.NET...");
                using (var directory = FSDirectory.Open(new DirectoryInfo(indexPath)))
                {
                    if (IndexReader.IndexExists(directory))
                    {
                        Console.WriteLine("Index exists and can be opened with Lucene.NET directly.");
                        
                        using (var reader = IndexReader.Open(directory, true))
                        {
                            Console.WriteLine($"Successfully opened index with Lucene.NET: {reader.NumDocs()} documents, {reader.MaxDoc()} maximum documents");
                            
                            // Check for SenseNet-specific fields
                            var fields = reader.GetFieldNames(IndexReader.FieldOption.ALL).ToList();
                            Console.WriteLine($"Found {fields.Count} fields in the index:");
                            foreach (var field in fields.Take(10))
                            {
                                Console.WriteLine($"  - {field}");
                            }
                            
                            if (fields.Count > 10)
                            {
                                Console.WriteLine($"  - ... and {fields.Count - 10} more fields");
                            }
                            
                            // Check for SenseNet-specific commit data
                            var commitUserData = reader.GetCommitUserData();
                            if (commitUserData != null && commitUserData.Count > 0)
                            {
                                Console.WriteLine("\nFound commit user data:");
                                foreach (var entry in commitUserData)
                                {
                                    Console.WriteLine($"  - {entry.Key}: {entry.Value}");
                                }
                            }
                            else
                            {
                                Console.WriteLine("\nNo commit user data found.");
                            }
                            
                            // Check for special SenseNet documents
                            var searcher = new IndexSearcher(reader);
                            var query = new TermQuery(new Term("$#COMMIT", "$#COMMIT"));
                            var hits = searcher.Search(query, 1);
                            
                            if (hits.TotalHits > 0)
                            {
                                Console.WriteLine("\nFound SenseNet commit documents in the index.");
                                // Get the first hit
                                var doc = searcher.Doc(hits.ScoreDocs[0].Doc);
                                Console.WriteLine("SenseNet commit document fields:");
                                foreach (var field in doc.GetFields())
                                {
                                    Console.WriteLine($"  - {field.Name}: {field.StringValue}");
                                }
                            }
                            else
                            {
                                Console.WriteLine("\nNo SenseNet commit documents found in the index.");
                            }
                            
                            searcher.Close();
                        }
                    }
                    else
                    {
                        Console.WriteLine("Index does not exist or cannot be opened with Lucene.NET directly.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing index: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private static void ValidateLuceneIndex(string path)
        {
            var files = IODirectory.GetFiles(path);
            
            // Check for segments file which is typically present in Lucene indices
            if (!files.Any(f => Path.GetFileName(f).StartsWith("segments")))
            {
                throw new InvalidOperationException("No segments file found. This doesn't appear to be a valid Lucene index.");
            }
            
            // Check for compound file system (CFS) files
            if (!files.Any(f => Path.GetFileName(f).EndsWith(".cfs")))
            {
                throw new InvalidOperationException("No .cfs files found. This doesn't appear to be a valid Lucene index.");
            }
            
            Console.WriteLine("Index directory contains segments and .cfs files (basic validation passed).");
        }
    }
}
