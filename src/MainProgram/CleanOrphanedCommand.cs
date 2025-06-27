using System.CommandLine;
using System.CommandLine.Invocation;
using System.Data.SqlClient;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;

namespace SenseNetIndexTools
{
    public class CleanOrphanedCommand
    {
        public static Command Create()
        {
            var command = new Command("clean-orphaned", "Delete orphaned index entries (items that exist in the index but not in the database)");

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
                description: "Recursively process all content items under the specified path",
                getDefaultValue: () => true);

            var verboseOption = new Option<bool>(
                name: "--verbose",
                description: "Enable detailed logging of the cleanup process",
                getDefaultValue: () => false);

            var dryRunOption = new Option<bool>(
                name: "--dry-run",
                description: "Only show what would be deleted without making any changes",
                getDefaultValue: () => true);

            var backupOption = new Option<bool>(
                name: "--backup",
                description: "Create a backup of the index before making changes",
                getDefaultValue: () => true);

            var offlineOption = new Option<bool>(
                name: "--offline",
                description: "Confirm that the index is not in use and can be safely modified",
                getDefaultValue: () => false);

            var backupPathOption = new Option<string?>(
                name: "--backup-path",
                description: "Custom path for storing backups");

            command.AddOption(indexPathOption);
            command.AddOption(connectionStringOption);
            command.AddOption(repositoryPathOption);
            command.AddOption(recursiveOption);
            command.AddOption(verboseOption);
            command.AddOption(dryRunOption);
            command.AddOption(backupOption);
            command.AddOption(offlineOption);
            command.AddOption(backupPathOption);

            command.SetHandler((context) =>
            {
                bool verbose = false; // Declare at method scope
                try
                {
                    // Parse options
                    var indexPath = context.ParseResult.GetValueForOption(indexPathOption)
                        ?? throw new ArgumentNullException(nameof(indexPathOption), "Index path is required.");
                    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption)
                        ?? throw new ArgumentNullException(nameof(connectionStringOption), "Connection string is required.");
                    var repositoryPath = context.ParseResult.GetValueForOption(repositoryPathOption)
                        ?? throw new ArgumentNullException(nameof(repositoryPathOption), "Repository path is required.");
                    var recursive = context.ParseResult.GetValueForOption(recursiveOption);
                    verbose = context.ParseResult.GetValueForOption(verboseOption); // Assign to the outer scope variable
                    var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
                    var backup = context.ParseResult.GetValueForOption(backupOption);
                    var offline = context.ParseResult.GetValueForOption(offlineOption);
                    var backupPath = context.ParseResult.GetValueForOption(backupPathOption);                    Console.WriteLine($"Starting orphaned index entries cleanup for path: {repositoryPath}");

                    // Validation checks
                    if (!Program.IsValidLuceneIndex(indexPath))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {indexPath}");
                        Environment.Exit(1);
                        return Task.CompletedTask;
                    }

                    if (!dryRun && !offline)
                    {
                        Console.Error.WriteLine("The --offline flag is required for modifying indexes. This protects live indexes from accidental modification.");
                        Environment.Exit(1);
                        return Task.CompletedTask;
                    }

                    // Create a backup if requested
                    if (!dryRun && backup)
                    {
                        Program.CreateBackup(indexPath, backupPath);
                    }

                    // Compare content to find orphaned entries
                    var comparer = new ContentComparer();
                    var results = comparer.CompareContent(indexPath, connectionString, repositoryPath, recursive, 0);                    // Filter for orphaned entries (index-only items)
                    var orphanedEntries = results.Where(r => !r.InDatabase && r.InIndex).ToList();

                    Console.WriteLine($"\nFound {orphanedEntries.Count} orphaned index entries:");
                    foreach (var entry in orphanedEntries)
                    {
                        Console.WriteLine($"- Path: {entry.Path}");
                        Console.WriteLine($"  NodeId: {entry.IndexNodeId}");
                        Console.WriteLine($"  Type: {entry.NodeType}");
                        Console.WriteLine();
                    }

                    if (dryRun)
                    {
                        Console.WriteLine("\nDRY RUN - No changes were made. Use --dry-run=false --offline to perform the cleanup.");
                        return Task.CompletedTask;
                    }
                    if (orphanedEntries.Count > 0)
                    {
                        // Perform the cleanup using IndexWriter
                        using (var directory = FSDirectory.Open(new DirectoryInfo(indexPath)))
                        using (var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29))
                        {
                            // Check if index is locked and unlock if necessary
                            bool isLocked = IndexWriter.IsLocked(directory);
                            if (isLocked)
                            {
                                Console.WriteLine("Index is locked. Attempting to unlock...");
                                IndexWriter.Unlock(directory);
                                Console.WriteLine("Index unlocked successfully.");
                            }

                            using (var writer = new IndexWriter(directory, analyzer, false, IndexWriter.MaxFieldLength.UNLIMITED))
                            {
                                int successfulDeletions = 0;
                                Console.WriteLine($"\nBeginning deletion of {orphanedEntries.Count} orphaned entries...");

                                foreach (var entry in orphanedEntries)
                                {
                                    if (verbose)
                                    {
                                        Console.WriteLine($"\nDELETING: Path='{entry.Path}', NodeId={entry.IndexNodeId}");
                                    }

                                    try
                                    {
                                        // Get initial count of matching documents
                                        var initialMatchCount = 0;
                                        using (var searcher = new IndexSearcher(writer.GetReader()))
                                        {
                                            var query = new TermQuery(new Term("Path", entry.Path));
                                            initialMatchCount = searcher.Search(query, 1).TotalHits;
                                        }

                                        DeleteOrphanedEntry(writer, entry, verbose);

                                        // Verify deletion
                                        using (var searcher = new IndexSearcher(writer.GetReader()))
                                        {
                                            var query = new TermQuery(new Term("Path", entry.Path));
                                            var finalMatchCount = searcher.Search(query, 1).TotalHits;

                                            if (finalMatchCount < initialMatchCount)
                                            {
                                                successfulDeletions++;
                                                if (verbose)
                                                {
                                                    Console.WriteLine($"Deletion verified: Removed {initialMatchCount - finalMatchCount} documents for path '{entry.Path}'");
                                                }
                                            }
                                            else
                                            {
                                                Console.WriteLine($"Warning: Failed to delete documents for path '{entry.Path}' (Before: {initialMatchCount}, After: {finalMatchCount})");
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error while deleting entry {entry.Path}: {ex.Message}");
                                        if (verbose)
                                        {
                                            Console.WriteLine(ex.StackTrace);
                                        }
                                    }
                                }

                                // Final cleanup and optimization
                                writer.Commit();
                                writer.Optimize();

                                if (successfulDeletions > 0)
                                {
                                    Console.WriteLine($"\nSuccessfully removed {successfulDeletions} orphaned entries from the index.");
                                    if (successfulDeletions < orphanedEntries.Count)
                                    {
                                        Console.WriteLine($"Warning: {orphanedEntries.Count - successfulDeletions} entries could not be deleted.");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("\nWarning: No entries were successfully deleted from the index.");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    if (verbose)
                    {
                        Console.Error.WriteLine(ex.StackTrace);
                    }
                    Environment.Exit(1);
                }

                return Task.CompletedTask;
            });

            return command;
        }

        private static void DeleteOrphanedEntry(IndexWriter writer, ContentItem entry, bool verbose)
        {
            using (var searcher = new IndexSearcher(writer.GetReader()))
            {
                // We must always search with lowercase path as that's how SenseNet stores it
                var pathQuery = new TermQuery(new Term("Path", entry.Path.ToLowerInvariant()));

                // Create NodeId query with prefix-coded value to match both possible field names
                var nodeIdQuery = new BooleanQuery();
                if (!string.IsNullOrEmpty(entry.IndexNodeId) && int.TryParse(entry.IndexNodeId, out var nodeId))
                {
                    var prefixCodedNodeId = Lucene.Net.Util.NumericUtils.IntToPrefixCoded(nodeId);
                    nodeIdQuery.Add(new TermQuery(new Term("NodeId", prefixCodedNodeId)), BooleanClause.Occur.SHOULD);
                    nodeIdQuery.Add(new TermQuery(new Term("Id", prefixCodedNodeId)), BooleanClause.Occur.SHOULD);
                }

                // Create version query with prefix-coded value - this is required to identify the exact document
                TermQuery? versionIdQuery = null;
                if (!string.IsNullOrEmpty(entry.IndexVersionId) && int.TryParse(entry.IndexVersionId, out var versionId))
                {
                    var prefixCodedVersionId = Lucene.Net.Util.NumericUtils.IntToPrefixCoded(versionId);
                    versionIdQuery = new TermQuery(new Term("VersionId", prefixCodedVersionId));
                }

                // Build query to match exact document: NodeId + Path + VersionId
                var fullQuery = new BooleanQuery();
                fullQuery.Add(pathQuery, BooleanClause.Occur.MUST);          // Always include path
                fullQuery.Add(nodeIdQuery, BooleanClause.Occur.MUST);        // Always include NodeId
                if (versionIdQuery != null)
                {
                    fullQuery.Add(versionIdQuery, BooleanClause.Occur.MUST); // Include VersionId when available
                }

                var hits = searcher.Search(fullQuery, int.MaxValue);
                if (verbose)
                {
                    Console.WriteLine($"Found {hits.TotalHits} documents matching criteria:");
                    Console.WriteLine($"  Path: {entry.Path.ToLowerInvariant()}");
                    Console.WriteLine($"  NodeId: {entry.IndexNodeId}");
                    Console.WriteLine($"  VersionId: {entry.IndexVersionId}");
                }

                if (hits.TotalHits > 0)
                {
                    // Delete matching documents - should only be one if criteria are correct
                    for (int i = 0; i < hits.TotalHits; i++)
                    {
                        int docId = hits.ScoreDocs[i].Doc;
                        Document doc = searcher.Doc(docId);

                        // Log what we're about to delete
                        if (verbose)
                        {
                            Console.WriteLine($"\nDeleting document {i + 1}/{hits.TotalHits}:");
                            Console.WriteLine($"  DocId: {docId}");
                            Console.WriteLine($"  NodeId: {doc.Get("NodeId") ?? doc.Get("Id")}");
                            Console.WriteLine($"  Path: {doc.Get("Path")}");
                            Console.WriteLine($"  Version: {doc.Get("Version_")}");
                            Console.WriteLine($"  VersionId: {doc.Get("VersionId")}");
                        }

                        // Delete by NodeId + Path + VersionId to ensure we only delete the exact document
                        writer.DeleteDocuments(fullQuery);
                        writer.Commit();

                        // Verify deletion
                        using (var verifySearcher = new IndexSearcher(writer.GetReader()))
                        {
                            var verifyHits = verifySearcher.Search(fullQuery, 1);
                            if (verifyHits.TotalHits > 0)
                            {
                                Console.WriteLine($"WARNING: Document still exists after deletion attempt!");
                            }
                            else if (verbose)
                            {
                                Console.WriteLine($"Successfully deleted document with NodeId={entry.IndexNodeId}, VersionId={entry.IndexVersionId}, Path={entry.Path}");
                            }
                        }
                    }
                }
                else if (verbose)
                {
                    Console.WriteLine($"No documents found matching the exact criteria");
                }
            }
        }
    }
}
