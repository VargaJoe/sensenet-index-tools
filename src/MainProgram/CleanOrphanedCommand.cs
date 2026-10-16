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

            var connectionStringOption = RuntimeSettings.SqlOption();

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

            var indexInput = new IndexInputOptions(command, indexPathOption);
            command.SetHandler(async (context) =>
            {
                bool verbose = false; // Declare at method scope
                try
                {
                    // Parse options
                    var indexPath = await indexInput.ResolveAsync(context);
                    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption)
                        ?? throw new ArgumentNullException(nameof(connectionStringOption), "Connection string is required.");
                    var repositoryPath = context.ParseResult.GetValueForOption(repositoryPathOption)
                        ?? throw new ArgumentNullException(nameof(repositoryPathOption), "Repository path is required.");
                    var recursive = context.ParseResult.GetValueForOption(recursiveOption);
                    verbose = context.ParseResult.GetValueForOption(verboseOption); // Assign to the outer scope variable
                    var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
                    var backup = context.ParseResult.GetValueForOption(backupOption);
                    var offline = context.ParseResult.GetValueForOption(offlineOption);
                    var backupPath = context.ParseResult.GetValueForOption(backupPathOption);
                    Console.WriteLine($"Starting orphaned index entries cleanup for path: {repositoryPath}");
                    ContentComparer.VerboseLogging = verbose;

                    // Validation checks
                    if (!IndexUtilities.IsValidLuceneIndex(indexPath))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {indexPath}");
                        Environment.Exit(1);
                        return;
                    }

                    if (!dryRun && !offline)
                    {
                        Console.Error.WriteLine("The --offline flag is required for modifying indexes. This protects live indexes from accidental modification.");
                        Environment.Exit(1);
                        return;
                    }

                    // Create a backup if requested
                    if (!dryRun && backup)
                    {
                        IndexUtilities.CreateBackup(indexPath, backupPath);
                    }

                    // Compare content to find orphaned entries
                    var comparer = new ContentComparer();
                    var results = comparer.CompareContent(indexPath, connectionString, repositoryPath, recursive, 0);

                    // Filter for orphaned entries (index-only items)
                    var orphanedEntries = OrphanedIndexCleaner.FindCandidates(results);

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
                        return;
                    }
                    if (orphanedEntries.Count > 0)
                    {
                        var removed = OrphanedIndexCleaner.Remove(indexPath, orphanedEntries);
                        Console.WriteLine($"Successfully removed {removed} orphaned documents.");
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

                return;
            });

            return command;
        }

    }
}
