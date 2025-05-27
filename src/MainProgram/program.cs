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
            var rootCommand = new RootCommand("SenseNet Index Maintenance Suite - Tools for managing SenseNet Lucene indices");

            var pathOption = new Option<string>(
                name: "--path",
                description: "Path to the Lucene index directory");
            pathOption.IsRequired = true;

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
            rootCommand.AddCommand(getCommand);
            rootCommand.AddCommand(setCommand);
            rootCommand.AddCommand(initCommand);            rootCommand.AddCommand(validateCommand);
            rootCommand.AddCommand(IndexLister.Create());
            rootCommand.AddCommand(SubtreeIndexChecker.Create());
            rootCommand.AddCommand(DatabaseLister.Create());
            rootCommand.AddCommand(ContentComparer.Create());

            getCommand.SetHandler(async (string path) =>
            {
                try
                {
                    Console.WriteLine($"Opening index directory: {path}");

                    // First verify this is a valid Lucene index
                    if (!IsValidLuceneIndex(path))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {path}");
                        Environment.Exit(1);
                        return;
                    }

                    // First try using SenseNet API method
                    try
                    {
                        var directory = new IndexDirectory(path);
                        Console.WriteLine("Created IndexDirectory object successfully.");

                        var engine = new Lucene29LocalIndexingEngine(directory);
                        Console.WriteLine("Created Lucene29LocalIndexingEngine object successfully.");

                        Console.WriteLine("Attempting to read activity status using SenseNet API...");
                        var status = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
                        Console.WriteLine($"Last activity ID: {status.LastActivityId}");
                        if (status.Gaps?.Any() == true)
                            Console.WriteLine($"Activity gaps: {string.Join(", ", status.Gaps)}");

                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"SenseNet API method failed: {ex.Message}");
                        Console.WriteLine("Falling back to direct Lucene.NET access method...");
                    }

                    // Fall back to direct Lucene.NET access
                    try
                    {
                        using (var directory = FSDirectory.Open(new DirectoryInfo(path)))
                        {
                            if (IndexReader.IndexExists(directory))
                            {
                                using (var reader = IndexReader.Open(directory, true))
                                {
                                    var commitUserData = reader.GetCommitUserData();
                                    if (commitUserData != null && commitUserData.ContainsKey("LastActivityId"))
                                    {
                                        var lastActivityId = commitUserData["LastActivityId"];
                                        Console.WriteLine($"Last activity ID (from commit user data): {lastActivityId}");
                                    }
                                    else
                                    {
                                        Console.WriteLine("No LastActivityId found in commit user data.");
                                    }
                                    reader.Close();
                                }
                            }
                            else
                            {
                                Console.WriteLine("Index does not exist or cannot be opened.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not read LastActivityId from index: {ex.Message}");
                        Console.WriteLine("The index may not have a LastActivityId set or may not be a SenseNet index.");
                        Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error accessing index: {ex.Message}");
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    Environment.Exit(1);
                }
            }, pathOption);

            setCommand.SetHandler(async (string path, long id, bool backup, string? backupPath, bool offline) =>
            {
                try
                {
                    if (!IsValidLuceneIndex(path))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {path}");
                        Environment.Exit(1);
                        return;
                    }

                    if (!offline)
                    {
                        Console.Error.WriteLine("The --offline flag is required for modifying indexes. This protects live indexes from accidental modification.");
                        Environment.Exit(1);
                        return;
                    }

                    if (backup)
                    {
                        CreateBackup(path, backupPath);
                    }

                    // First try using SenseNet API method
                    bool useSenseNetApi = false;

                    try
                    {
                        var directory = new IndexDirectory(path);
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

                        useSenseNetApi = true;
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
                        using (var directory = FSDirectory.Open(new DirectoryInfo(path)))
                        {
                            if (IndexReader.IndexExists(directory))
                            {
                                // We need to use IndexReader first to check if the index is locked
                                bool isLocked = IndexWriter.IsLocked(directory);
                                if (isLocked)
                                {
                                    Console.WriteLine("Index is locked. Attempting to unlock...");
                                    IndexWriter.Unlock(directory);
                                    Console.WriteLine("Index unlocked successfully.");
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
            }, pathOption, idOption, backupOption, backupPathOption, offlineOption);

            // New command specifically for initializing a non-SenseNet index
            initCommand.SetHandler(async (string path, long id, bool backup, string? backupPath, bool offline) =>
            {
                try
                {
                    // First verify this is a valid Lucene index
                    if (!IsValidLuceneIndex(path))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {path}");
                        Environment.Exit(1);
                        return;
                    }

                    if (!offline)
                    {
                        Console.Error.WriteLine("The --offline flag is required for modifying indexes. This protects live indexes from accidental modification.");
                        Environment.Exit(1);
                        return;
                    }

                    if (backup)
                    {
                        CreateBackup(path, backupPath);
                    }

                    Console.WriteLine($"Opening index directory: {path}");

                    // First check if LastActivityId already exists
                    bool alreadyInitialized = false;

                    try
                    {
                        using (var directory = FSDirectory.Open(new DirectoryInfo(path)))
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
                        var directory = new IndexDirectory(path);
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
                        using (var directory = FSDirectory.Open(new DirectoryInfo(path)))
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
            }, pathOption, idOption, backupOption, backupPathOption, offlineOption);

            return await rootCommand.InvokeAsync(args);
        }

        // Helper method to create a backup of the index
        public static void CreateBackup(string path, string? backupPath = null)
        {
            // Get the index directory name
            var indexDirInfo = new DirectoryInfo(path);
            var indexDirName = indexDirInfo.Name;

            // Create a "Backups" directory next to the index directory, not inside it
            var parentDir = indexDirInfo.Parent?.FullName ?? ".";
            var backupsRootDir = backupPath ?? Path.Combine(parentDir, "IndexBackups");

            // Make sure the backups root directory exists
            if (!IODirectory.Exists(backupsRootDir))
            {
                IODirectory.CreateDirectory(backupsRootDir);
            }

            // Create a backup directory with timestamp and index name
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPathFinal = Path.Combine(backupsRootDir, $"{indexDirName}_backup_{timestamp}");

            Console.WriteLine($"Creating backup at {backupPathFinal}");
            IODirectory.CreateDirectory(backupPathFinal);

            // Copy all files from the index directory to the backup
            foreach (var file in IODirectory.GetFiles(path))
            {
                File.Copy(file, Path.Combine(backupPathFinal, Path.GetFileName(file)));
            }

            Console.WriteLine("Backup completed successfully.");
        }

        // Helper method to check if a directory is a valid Lucene index
        public static bool IsValidLuceneIndex(string path)
        {
            if (!IODirectory.Exists(path))
            {
                Console.Error.WriteLine($"Directory does not exist: {path}");
                return false;
            }

            // Look for common Lucene index files
            var files = IODirectory.GetFiles(path);

            // Check for segments file which is typically present in Lucene indices
            if (!files.Any(f => Path.GetFileName(f).StartsWith("segments")))
            {
                Console.Error.WriteLine("No segments file found. This doesn't appear to be a valid Lucene index.");
                return false;
            }
            // If we have segments files, consider it a valid index
            // Lucene indices can use either compound .cfs files or individual component files (.fdt, .fdx, etc.)
            return true;
        }
    }
}
