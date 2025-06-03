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
                try
                {                    var indexPath = context.ParseResult.GetValueForOption(indexPathOption)
                        ?? throw new ArgumentNullException(nameof(indexPathOption), "Index path is required.");
                    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption)
                        ?? throw new ArgumentNullException(nameof(connectionStringOption), "Connection string is required.");
                    var repositoryPath = context.ParseResult.GetValueForOption(repositoryPathOption)
                        ?? throw new ArgumentNullException(nameof(repositoryPathOption), "Repository path is required.");
                    var recursive = context.ParseResult.GetValueForOption(recursiveOption);
                    var verbose = context.ParseResult.GetValueForOption(verboseOption);
                    var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
                    var backup = context.ParseResult.GetValueForOption(backupOption);
                    var offline = context.ParseResult.GetValueForOption(offlineOption);
                    var backupPath = context.ParseResult.GetValueForOption(backupPathOption);

                    Console.WriteLine($"Starting orphaned index entries cleanup for path: {repositoryPath}");
                    ContentComparer.VerboseLogging = verbose;

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
                    var results = comparer.CompareContent(indexPath, connectionString, repositoryPath, recursive, 0);

                    // Filter for orphaned entries (index-only items)
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
                        using (var writer = new IndexWriter(directory, analyzer, false, IndexWriter.MaxFieldLength.UNLIMITED))
                        {
                            foreach (var entry in orphanedEntries)
                            {                                // Create a boolean query to match the exact document
                                var booleanQuery = new BooleanQuery();
                                
                                // IMPORTANT: We're using only path-based matching because the Id field
                                // format in the index might be different from what we have
                                // This is safer and works reliably
                                
                                // Match by path (SenseNet stores paths in lowercase)
                                booleanQuery.Add(new TermQuery(new Term("Path", entry.Path.ToLowerInvariant())), BooleanClause.Occur.MUST);

                                // Delete matching documents
                                if (verbose)
                                {
                                    Console.WriteLine($"Deleting documents with Path={entry.Path.ToLowerInvariant()} (path-only matching)");
                                }
                                writer.DeleteDocuments(booleanQuery);
                            }

                            writer.Commit();
                            writer.Optimize();

                            Console.WriteLine($"Successfully removed {orphanedEntries.Count} orphaned entries from the index.");
                        }
                    }
                    
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error cleaning orphaned entries: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    Environment.Exit(1);
                    return Task.CompletedTask;
                }
            });

            return command;
        }
    }
}
