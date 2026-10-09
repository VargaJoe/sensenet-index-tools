using System.CommandLine;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System.Collections.Generic;
using SenseNet.Search;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29;
using IODirectory = System.IO.Directory;

namespace SenseNetIndexTools
{
    public partial class Program
    {
        private const string COMMITFIELDNAME = "$#COMMIT";
        private const string COMMITDATAFIELDNAME = "$#DATA";

        public static async Task<int> Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "healthcheck")
            {
                try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) }; return (await client.GetAsync("http://127.0.0.1:8080/healthz")).IsSuccessStatusCode ? 0 : 1; }
                catch { return 1; }
            }
            Console.SetOut(new SecretRedactor.Writer(Console.Out));
            Console.SetError(new SecretRedactor.Writer(Console.Error));
            var rootCommand = new RootCommand("SenseNet Index Maintenance Suite - Tools for managing SenseNet Lucene indices");

            var pathOption = new Option<string>(
                name: "--path",
                description: "Path to the Lucene index directory (optional when --auto-copy-index is used)");

            var idOption = new Option<long>(
                name: "--id",
                description: "The new LastActivityId value to set");
            idOption.IsRequired = true;

            var backupOption = new Option<bool>(
                name: "--backup",
                description: "Create a backup of the index before making changes",
                getDefaultValue: () => true);

            var backupPathOption = new Option<string?>(
                name: "--backup-path",
                description: "Custom path for storing backups. If not specified, backups will be stored in an 'IndexBackups' folder at the same level as the index parent folder");

            var offlineOption = new Option<bool>(
                name: "--offline",
                description: "Confirm that the index is not in use and can be safely modified. Required for write operations to protect live indexes.",
                getDefaultValue: () => false);

            var getCommand = new Command("lastactivityid-get", "Get current LastActivityId from index");
            var setCommand = new Command("lastactivityid-set", "Set LastActivityId in index");
            var initCommand = new Command("lastactivityid-init", "Initialize LastActivityId in a non-SenseNet Lucene index");
            var validateCommand = SenseNetIndexTools.ValidateCommand.Create();

            getCommand.AddOption(pathOption);

            setCommand.AddOption(pathOption);
            setCommand.AddOption(idOption);
            setCommand.AddOption(backupOption);
            setCommand.AddOption(backupPathOption);
            setCommand.AddOption(offlineOption); // Add offline flag to set command

            initCommand.AddOption(pathOption);
            initCommand.AddOption(idOption);
            initCommand.AddOption(backupOption);
            initCommand.AddOption(backupPathOption);
            initCommand.AddOption(offlineOption); // Add offline flag to init command
            var getInput = new IndexInputOptions(getCommand, pathOption);
            var setInput = new IndexInputOptions(setCommand, pathOption);
            var initInput = new IndexInputOptions(initCommand, pathOption);
            idOption.AddValidator(result => {
                var value = result.GetValueForOption(idOption);
                if (value < 0 || value > int.MaxValue) result.ErrorMessage = "LastActivityId must be between 0 and 2147483647.";
            });
            rootCommand.AddCommand(SnapshotCommand.Create());
            rootCommand.AddCommand(VerifyRepositoryCommand.Create());
            rootCommand.AddCommand(getCommand);
            rootCommand.AddCommand(setCommand);
            rootCommand.AddCommand(initCommand);
            rootCommand.AddCommand(validateCommand);
            rootCommand.AddCommand(IndexLister.Create());
            rootCommand.AddCommand(SubtreeIndexChecker.Create());
            rootCommand.AddCommand(DatabaseLister.Create());
            rootCommand.AddCommand(ContentComparer.Create());
            rootCommand.AddCommand(CleanOrphanedCommand.Create());
            rootCommand.AddCommand(RebuildIndexCommand.Create());

            getCommand.SetHandler(async (context) =>
            {
                var path = context.ParseResult.GetValueForOption(pathOption);
                string? actualPath = null;
                try
                {
                    actualPath = await getInput.ResolveAsync(context);

                    Console.WriteLine($"Opening index directory: {actualPath}");

                    // First verify this is a valid Lucene index
                    if (!IndexUtilities.IsValidLuceneIndex(actualPath))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {actualPath}");
                        Environment.Exit(1);
                        return;
                    }

                    var status = ActivityStatusReader.Read(actualPath);
                    Console.WriteLine($"Last activity ID: {status.LastActivityId}");
                    if (status.Gaps.Length > 0) Console.WriteLine($"Activity gaps: {string.Join(", ", status.Gaps)}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error accessing index: {ex.Message}");
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    Environment.Exit(1);
                }
            });

            setCommand.SetHandler(async (context) =>
            {
                var path = context.ParseResult.GetValueForOption(pathOption);
                var id = context.ParseResult.GetValueForOption(idOption);
                var backup = context.ParseResult.GetValueForOption(backupOption);
                var backupPath = context.ParseResult.GetValueForOption(backupPathOption);
                var offline = context.ParseResult.GetValueForOption(offlineOption);
                try
                {
                    var actualPath = await setInput.ResolveAsync(context);

                    if (!IndexUtilities.IsValidLuceneIndex(actualPath))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {actualPath}");
                        Environment.Exit(1);
                        return;
                    }

                    if (!offline)
                    {
                        Console.Error.WriteLine("The --offline flag is required for modifying indexes. This protects live indexes from accidental modification.");
                        Environment.Exit(1);
                        return;
                    }

                    RuntimeSettings.EnsureWritableCopy(actualPath);
                    backupPath ??= RuntimeSettings.Load().BackupDirectory;
                    if (backup)
                    {
                        IndexUtilities.CreateBackup(actualPath, backupPath);
                    }

                    // First try using SenseNet API method
                    try
                    {
                        var directory = new IndexDirectory(actualPath);
                        var engine = new Lucene29LocalIndexingEngine(directory);

                        // Get current status to preserve gaps
                        var currentStatus = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);

                        // Create a new status, preserving gaps if they exist
                        var newStatus = new IndexingActivityStatus
                        {
                            LastActivityId = (int)id,
                            Gaps = currentStatus?.Gaps ?? Array.Empty<int>()
                        };

                        Console.WriteLine("Writing updated activity status to index using SenseNet API...");
                        await engine.WriteActivityStatusToIndexAsync(newStatus, CancellationToken.None);
                        Console.WriteLine($"Successfully set LastActivityId to {id}");

                        // Verify the change
                        var verificationStatus = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
                        if (verificationStatus.LastActivityId == (int)id)
                            Console.WriteLine("Verification successful: LastActivityId was properly updated.");
                        else
                            Console.WriteLine($"Warning: Verification returned different value: {verificationStatus.LastActivityId}");

                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to write LastActivityId using SenseNet API: {ex.Message}");
                        Console.WriteLine("Falling back to direct Lucene.NET access method...");
                    }

                    // Fall back to direct Lucene.NET access
                    try
                    {
                        Console.WriteLine("Using direct Lucene.NET access to update LastActivityId...");

                        // Open the index with write access
                        using (var directory = FSDirectory.Open(new DirectoryInfo(actualPath)))
                        {
                            if (IndexReader.IndexExists(directory))
                            {
                                // We need to use IndexReader first to check if the index is locked
                                bool isLocked = IndexWriter.IsLocked(directory);
                                if (isLocked)
                                {
                                throw new InvalidOperationException("Index is locked. Stop the writer and inspect the lock before modifying it.");
                                }

                                // Get existing commit user data first
                                Dictionary<string, string> commitUserData = new Dictionary<string, string>();
                                try
                                {
                                    using (var reader = IndexReader.Open(directory, true))
                                    {
                                        var existingData = reader.GetCommitUserData();
                                        if (existingData != null)
                                        {
                                            foreach (var entry in existingData)
                                            {
                                                commitUserData[entry.Key] = entry.Value;
                                            }
                                        }
                                        reader.Close();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Warning: Could not read existing commit data: {ex.Message}");
                                    // Continue with empty commit data if we can't read the existing one
                                }

                                // Update the LastActivityId
                                commitUserData["LastActivityId"] = id.ToString();
                                Console.WriteLine($"Preparing to write LastActivityId = {id} to index...");

                                // Open the index writer with create=false (don't overwrite the existing index)
                                using (var indexWriter = new IndexWriter(directory,
                                                                       new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29),
                                                                       false, // don't create a new index
                                                                       IndexWriter.MaxFieldLength.UNLIMITED))
                                {
                                    // Create and add commit document
                                    var value = Guid.NewGuid().ToString();
                                    var doc = new Document();
                                    doc.Add(new Field(COMMITFIELDNAME, COMMITFIELDNAME,
                                        Field.Store.YES,
                                        Field.Index.NOT_ANALYZED,
                                        Field.TermVector.NO));
                                    doc.Add(new Field(COMMITDATAFIELDNAME, value,
                                        Field.Store.YES,
                                        Field.Index.NOT_ANALYZED,
                                        Field.TermVector.NO));

                                    // Update the document by term to ensure it replaces any existing one
                                    indexWriter.UpdateDocument(new Term(COMMITFIELDNAME, COMMITFIELDNAME), doc);

                                    // Commit the changes with the updated user data
                                    Console.WriteLine($"Committing LastActivityId = {id} to index...");
                                    indexWriter.Commit(commitUserData);
                                    Console.WriteLine("Commit successful.");

                                    // Ensure changes are written to disk
                                    indexWriter.Close();
                                    Console.WriteLine("IndexWriter closed successfully.");
                                }

                                Console.WriteLine($"Successfully updated LastActivityId to {id} in commit user data.");

                                // Verify the change - use a new reader after closing the writer
                                Console.WriteLine("Verifying change with a new reader...");
                                using (var reader = IndexReader.Open(directory, true))
                                {
                                    var verifyCommitUserData = reader.GetCommitUserData();
                                    if (verifyCommitUserData != null && verifyCommitUserData.ContainsKey("LastActivityId"))
                                    {
                                        var lastActivityId = verifyCommitUserData["LastActivityId"];
                                        Console.WriteLine($"Verification: LastActivityId = {lastActivityId}");

                                        if (lastActivityId == id.ToString())
                                            Console.WriteLine("Verification successful: LastActivityId was properly updated.");
                                        else
                                            Console.WriteLine($"Warning: LastActivityId value is different from what was expected: {lastActivityId} vs {id}");
                                    }
                                    else
                                    {
                                        Console.WriteLine("Warning: LastActivityId not found in commit user data during verification.");
                                    }
                                    reader.Close();
                                }
                            }
                            else
                            {
                                Console.WriteLine("Index does not exist or cannot be opened.");
                                Environment.Exit(1);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to write LastActivityId: {ex.Message}");
                        Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                        Environment.Exit(1);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error setting last activity ID: {ex.Message}");
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    Environment.Exit(1);
                }
            });

            // New command specifically for initializing a non-SenseNet index
            initCommand.SetHandler(async (context) =>
            {
                var path = context.ParseResult.GetValueForOption(pathOption);
                var id = context.ParseResult.GetValueForOption(idOption);
                var backup = context.ParseResult.GetValueForOption(backupOption);
                var backupPath = context.ParseResult.GetValueForOption(backupPathOption);
                var offline = context.ParseResult.GetValueForOption(offlineOption);
                try
                {
                    var actualPath = await initInput.ResolveAsync(context);

                    // First verify this is a valid Lucene index
                    if (!IndexUtilities.IsValidLuceneIndex(actualPath))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {actualPath}");
                        Environment.Exit(1);
                        return;
                    }

                    if (!offline)
                    {
                        Console.Error.WriteLine("The --offline flag is required for modifying indexes. This protects live indexes from accidental modification.");
                        Environment.Exit(1);
                        return;
                    }

                    RuntimeSettings.EnsureWritableCopy(actualPath);
                    backupPath ??= RuntimeSettings.Load().BackupDirectory;
                    if (backup)
                    {
                        IndexUtilities.CreateBackup(actualPath, backupPath);
                    }

                    Console.WriteLine($"Opening index directory: {actualPath}");

                    // First check if LastActivityId already exists
                    bool alreadyInitialized = false;

                    try
                    {
                        using (var directory = FSDirectory.Open(new DirectoryInfo(actualPath)))
                        {
                            if (IndexReader.IndexExists(directory))
                            {
                                using (var reader = IndexReader.Open(directory, true))
                                {
                                    var commitUserData = reader.GetCommitUserData();
                                    if (commitUserData != null && commitUserData.ContainsKey("LastActivityId"))
                                    {
                                        alreadyInitialized = true;
                                        var lastActivityId = commitUserData["LastActivityId"];
                                        Console.WriteLine($"Index already has a LastActivityId: {lastActivityId}");
                                        Console.WriteLine("Use the 'set' command to modify an existing LastActivityId.");
                                    }
                                    reader.Close();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error checking existing LastActivityId: {ex.Message}");
                        Console.WriteLine("Will proceed with initialization...");
                    }

                    if (alreadyInitialized)
                    {
                        return;
                    }

                    // Try SenseNet API first
                    bool useSenseNetApi = false;

                    try
                    {
                        var directory = new IndexDirectory(actualPath);
                        var engine = new Lucene29LocalIndexingEngine(directory);

                        // Try to initialize with SenseNet API
                        Console.WriteLine("Attempting to initialize LastActivityId using SenseNet API...");
                        var newStatus = new IndexingActivityStatus
                        {
                            LastActivityId = (int)id,
                            Gaps = Array.Empty<int>()
                        };

                        await engine.WriteActivityStatusToIndexAsync(newStatus, CancellationToken.None);
                        Console.WriteLine("Successfully initialized activity status with SenseNet API.");

                        // Verify
                        var verificationStatus = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
                        Console.WriteLine($"Verification successful: LastActivityId = {verificationStatus.LastActivityId}");

                        useSenseNetApi = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"SenseNet API initialization failed: {ex.Message}");
                        Console.WriteLine("Falling back to direct Lucene.NET access method...");
                    }

                    if (useSenseNetApi)
                    {
                        return;
                    }

                    // Fall back to direct Lucene.NET access
                    try
                    {
                        Console.WriteLine("Using direct Lucene.NET access to initialize LastActivityId...");

                        // Open the index with write access
                        using (var directory = FSDirectory.Open(new DirectoryInfo(actualPath)))
                        {
                            if (IndexReader.IndexExists(directory))
                            {
                                // Create a new commit document
                                using (var indexWriter = new IndexWriter(directory,
                                                                        new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29),
                                                                        false, // don't create a new index
                                                                        IndexWriter.MaxFieldLength.UNLIMITED))
                                {
                                    // Create commit document for storing LastActivityId
                                    var commitUserData = new Dictionary<string, string> { ["LastActivityId"] = id.ToString() };
                                    var value = Guid.NewGuid().ToString();
                                    var doc = new Document();
                                    doc.Add(new Field(COMMITFIELDNAME, COMMITFIELDNAME,
                                        Field.Store.YES,
                                        Field.Index.NOT_ANALYZED,
                                        Field.TermVector.NO));
                                    doc.Add(new Field(COMMITDATAFIELDNAME, value,
                                        Field.Store.YES,
                                        Field.Index.NOT_ANALYZED,
                                        Field.TermVector.NO));

                                    // Add the commit document to the index
                                    indexWriter.UpdateDocument(new Term(COMMITFIELDNAME, COMMITFIELDNAME), doc);

                                    // Commit the changes with the updated user data
                                    Console.WriteLine($"Committing LastActivityId = {id} to index...");
                                    indexWriter.Commit(commitUserData);
                                    Console.WriteLine("Commit successful.");

                                    // Ensure changes are written to disk
                                    indexWriter.Close();
                                    Console.WriteLine("IndexWriter closed successfully.");
                                }

                                Console.WriteLine($"Successfully initialized LastActivityId to {id} in commit user data.");

                                // Verify the change
                                Console.WriteLine("Verifying change with a new reader...");
                                using (var reader = IndexReader.Open(directory, true))
                                {
                                    var verifyCommitUserData = reader.GetCommitUserData();
                                    if (verifyCommitUserData != null && verifyCommitUserData.ContainsKey("LastActivityId"))
                                    {
                                        var lastActivityId = verifyCommitUserData["LastActivityId"];
                                        Console.WriteLine($"Verification: LastActivityId = {lastActivityId}");

                                        if (lastActivityId == id.ToString())
                                            Console.WriteLine("Verification successful: LastActivityId was properly initialized.");
                                        else
                                            Console.WriteLine($"Warning: LastActivityId value is different from what was expected: {lastActivityId} vs {id}");
                                    }
                                    else
                                    {
                                        Console.WriteLine("Warning: LastActivityId not found in commit user data during verification.");
                                    }
                                    reader.Close();
                                }
                            }
                            else
                            {
                                Console.WriteLine("Index does not exist or cannot be opened.");
                                Environment.Exit(1);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to initialize LastActivityId: {ex.Message}");
                        Console.Error.WriteLine("This index might not be compatible with SenseNet's activity tracking mechanism.");
                        Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                        Environment.Exit(1);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error initializing LastActivityId: {ex.Message}");
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    Environment.Exit(1);
                }
            });

            return await rootCommand.InvokeAsync(args);
        }
    }
}
