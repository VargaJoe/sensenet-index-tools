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

        private static async Task<string> AutoCopyIndexFromKubernetes(
            string? kubeconfig, 
            string? deployment, 
            string namespaceName, 
            string indexPathInPod,
            string localPath)
        {
            if (string.IsNullOrEmpty(kubeconfig) || string.IsNullOrEmpty(deployment))
            {
                return localPath; // No auto-copy requested
            }

            Console.WriteLine($"Auto-copying index from Kubernetes deployment: {deployment}");
            Console.WriteLine($"Namespace: {namespaceName}");
            Console.WriteLine($"Kubeconfig: {kubeconfig}");

            // Create temp directory
            var tempDir = Path.Combine(Path.GetTempPath(), $"sensenet-index-{DateTime.Now:yyyyMMddHHmmss}");
            System.IO.Directory.CreateDirectory(tempDir);
            Console.WriteLine($"Created temporary directory: {tempDir}");

            try
            {
                // Build kubectl command to get pod name
                var kubeconfigArg = string.IsNullOrEmpty(kubeconfig) ? "" : $"--kubeconfig \"{kubeconfig}\"";
                var getPodsCommand = $"kubectl {kubeconfigArg} get pods -l app={deployment} -n {namespaceName} --no-headers -o custom-columns=\":metadata.name\"";

                Console.WriteLine($"Finding pod with command: {getPodsCommand}");
                var podResult = await RunCommandAsync("cmd", $"/c {getPodsCommand}");
                
                if (podResult.ExitCode != 0)
                {
                    throw new Exception($"Failed to get pods: {podResult.Error}");
                }

                var podLines = podResult.Output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var podName = podLines.FirstOrDefault()?.Trim();
                
                if (string.IsNullOrEmpty(podName))
                {
                    throw new Exception("No pod found for the deployment");
                }

                // If multiple pods, prefer the one that contains the deployment name
                if (podLines.Length > 1)
                {
                    var preferredPod = podLines.FirstOrDefault(p => p.Contains(deployment));
                    if (!string.IsNullOrEmpty(preferredPod))
                    {
                        podName = preferredPod.Trim();
                    }
                }

                Console.WriteLine($"Selected pod: {podName}");

                // List index directory to find the latest folder
                var listCommand = $"kubectl {kubeconfigArg} exec {podName} -n {namespaceName} -- ls -la {indexPathInPod}";
                Console.WriteLine($"Listing index directory: {listCommand}");
                
                var listResult = await RunCommandAsync("cmd", $"/c {listCommand}");
                if (listResult.ExitCode != 0)
                {
                    // Try with container specification if the pod has multiple containers
                    listCommand = $"kubectl {kubeconfigArg} exec {podName} -n {namespaceName} -c sensenet -- ls -la {indexPathInPod}";
                    Console.WriteLine($"Retrying with container specification: {listCommand}");
                    listResult = await RunCommandAsync("cmd", $"/c {listCommand}");
                    
                    if (listResult.ExitCode != 0)
                    {
                        Console.WriteLine($"List command output: {listResult.Output}");
                        Console.WriteLine($"List command error: {listResult.Error}");
                        throw new Exception($"Failed to list index directory: {listResult.Error}");
                    }
                }

                Console.WriteLine($"List output: {listResult.Output}");

                // Parse the output to find the latest dated folder
                var lines = listResult.Output.Split('\n');
                var indexFolders = new List<string>();
                
                foreach (var line in lines)
                {
                    // Skip lines that don't contain directory info
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("drwxr"))
                        continue;
                        
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 9)
                    {
                        var folderName = parts[8];
                        // Look for folders that start with 20 (year) and are 14-15 chars (YYYYMMDDHHMMSS format)
                        if (folderName.Length >= 14 && folderName.Length <= 15 && folderName.StartsWith("20") && folderName.All(char.IsDigit))
                        {
                            indexFolders.Add(folderName);
                            Console.WriteLine($"Found index folder: {folderName}");
                        }
                    }
                }

                if (!indexFolders.Any())
                {
                    throw new Exception("No dated index folders found");
                }

                var latestFolder = indexFolders.OrderByDescending(f => f).First();
                Console.WriteLine($"Latest index folder: {latestFolder}");

                // Copy the index directly using kubectl cp
                var fullIndexPath = $"{indexPathInPod.TrimEnd('/')}/{latestFolder}";

                // kubectl cp on Windows doesn't work well with absolute paths
                // Change to the temp directory's parent and use relative path
                var parentDir = Path.GetDirectoryName(tempDir);
                if (string.IsNullOrEmpty(parentDir))
                {
                    throw new Exception("Unable to determine parent directory for temp path");
                }
                var relativeDir = Path.GetFileName(tempDir);

                // Change to parent directory for kubectl cp to work with relative paths
                var originalDir = System.IO.Directory.GetCurrentDirectory();
                try
                {
                    System.IO.Directory.SetCurrentDirectory(parentDir);
                    var copyCommand = $"kubectl {kubeconfigArg} cp \"{namespaceName}/{podName}:{fullIndexPath}\" \"{relativeDir}\"";

                    Console.WriteLine($"Executing kubectl cp from directory {parentDir}: {copyCommand}");
                    var copyResult = await RunCommandAsync("cmd", $"/c {copyCommand}");

                    if (copyResult.ExitCode != 0)
                    {
                        Console.Error.WriteLine($"kubectl cp failed: {copyResult.Error}");
                        Console.Error.WriteLine($"kubectl cp output: {copyResult.Output}");
                        throw new Exception($"Failed to copy index from pod: {copyResult.Error}");
                    }
                }
                finally
                {
                    System.IO.Directory.SetCurrentDirectory(originalDir);
                }

                Console.WriteLine("Index copy completed successfully");
                return tempDir;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Auto-copy failed: {ex.Message}");
                throw;
            }
        }

        private static async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(string command, string arguments)
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            return (process.ExitCode, output, error);
        }

        public static async Task<int> Main(string[] args)
        {
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

            // Kubernetes options
            var kubeconfigOption = new Option<string?>(
                name: "--kubeconfig",
                description: "Path to kubeconfig file for Kubernetes cluster access");

            var namespaceOption = new Option<string>(
                name: "--namespace",
                description: "Kubernetes namespace containing the SenseNet deployment",
                getDefaultValue: () => "default");

            var deploymentOption = new Option<string?>(
                name: "--deployment",
                description: "Name of the SenseNet deployment in Kubernetes");

            var indexPathInPodOption = new Option<string>(
                name: "--index-path-in-pod",
                description: "Path to the index directory inside the pod",
                getDefaultValue: () => "/app/App_Data/LocalIndex/");

            var autoCopyIndexOption = new Option<bool>(
                name: "--auto-copy-index",
                description: "Automatically copy index from Kubernetes pod to local temporary directory",
                getDefaultValue: () => false);

            var getCommand = new Command("lastactivityid-get", "Get current LastActivityId from index");
            var setCommand = new Command("lastactivityid-set", "Set LastActivityId in index");
            var initCommand = new Command("lastactivityid-init", "Initialize LastActivityId in a non-SenseNet Lucene index");
            var validateCommand = SenseNetIndexTools.ValidateCommand.Create();

            getCommand.AddOption(pathOption);
            getCommand.AddOption(kubeconfigOption);
            getCommand.AddOption(namespaceOption);
            getCommand.AddOption(deploymentOption);
            getCommand.AddOption(indexPathInPodOption);
            getCommand.AddOption(autoCopyIndexOption);
            
            setCommand.AddOption(pathOption);
            setCommand.AddOption(idOption);
            setCommand.AddOption(backupOption);
            setCommand.AddOption(backupPathOption);
            setCommand.AddOption(offlineOption); // Add offline flag to set command
            setCommand.AddOption(kubeconfigOption);
            setCommand.AddOption(namespaceOption);
            setCommand.AddOption(deploymentOption);
            setCommand.AddOption(indexPathInPodOption);
            setCommand.AddOption(autoCopyIndexOption);
            
            initCommand.AddOption(pathOption);
            initCommand.AddOption(idOption);
            initCommand.AddOption(backupOption);
            initCommand.AddOption(backupPathOption);
            initCommand.AddOption(offlineOption); // Add offline flag to init command
            initCommand.AddOption(kubeconfigOption);
            initCommand.AddOption(namespaceOption);
            initCommand.AddOption(deploymentOption);
            initCommand.AddOption(indexPathInPodOption);
            initCommand.AddOption(autoCopyIndexOption);
            rootCommand.AddCommand(getCommand);
            rootCommand.AddCommand(setCommand);
            rootCommand.AddCommand(initCommand);
            rootCommand.AddCommand(validateCommand);
            rootCommand.AddCommand(IndexLister.Create());
            rootCommand.AddCommand(SubtreeIndexChecker.Create());
            rootCommand.AddCommand(DatabaseLister.Create());
            rootCommand.AddCommand(ContentComparer.Create());
            rootCommand.AddCommand(CleanOrphanedCommand.Create());

            getCommand.SetHandler(async (context) =>
            {
                var path = context.ParseResult.GetValueForOption(pathOption);
                var kubeconfig = context.ParseResult.GetValueForOption(kubeconfigOption);
                var namespaceName = context.ParseResult.GetValueForOption(namespaceOption)!;
                var deployment = context.ParseResult.GetValueForOption(deploymentOption);
                var indexPathInPod = context.ParseResult.GetValueForOption(indexPathInPodOption)!;
                var autoCopyIndex = context.ParseResult.GetValueForOption(autoCopyIndexOption);

                if (autoCopyIndex && (string.IsNullOrEmpty(deployment) || string.IsNullOrEmpty(kubeconfig)))
                {
                    Console.Error.WriteLine("--deployment and --kubeconfig are required when using --auto-copy-index");
                    Environment.Exit(1);
                    return;
                }

                if (!autoCopyIndex && string.IsNullOrEmpty(path))
                {
                    Console.Error.WriteLine("--path is required when not using --auto-copy-index");
                    Environment.Exit(1);
                    return;
                }

                try
                {
                    var actualPath = autoCopyIndex ? 
                        await AutoCopyIndexFromKubernetes(kubeconfig, deployment, namespaceName, indexPathInPod, path ?? "auto-index") : 
                        path!;

                    Console.WriteLine($"Opening index directory: {actualPath}");

                    // First verify this is a valid Lucene index
                    if (!IndexUtilities.IsValidLuceneIndex(actualPath))
                    {
                        Console.Error.WriteLine($"The directory does not appear to be a valid Lucene index: {actualPath}");
                        Environment.Exit(1);
                        return;
                    }

                    // First try using SenseNet API method
                    try
                    {
                        var directory = new IndexDirectory(actualPath);
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
                        using (var directory = FSDirectory.Open(new DirectoryInfo(actualPath)))
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
            });

            setCommand.SetHandler(async (context) =>
            {
                var path = context.ParseResult.GetValueForOption(pathOption);
                var id = context.ParseResult.GetValueForOption(idOption);
                var backup = context.ParseResult.GetValueForOption(backupOption);
                var backupPath = context.ParseResult.GetValueForOption(backupPathOption);
                var offline = context.ParseResult.GetValueForOption(offlineOption);
                var kubeconfig = context.ParseResult.GetValueForOption(kubeconfigOption);
                var namespaceName = context.ParseResult.GetValueForOption(namespaceOption)!;
                var deployment = context.ParseResult.GetValueForOption(deploymentOption);
                var indexPathInPod = context.ParseResult.GetValueForOption(indexPathInPodOption)!;
                var autoCopyIndex = context.ParseResult.GetValueForOption(autoCopyIndexOption);

                if (autoCopyIndex && (string.IsNullOrEmpty(deployment) || string.IsNullOrEmpty(kubeconfig)))
                {
                    Console.Error.WriteLine("--deployment and --kubeconfig are required when using --auto-copy-index");
                    Environment.Exit(1);
                    return;
                }

                if (!autoCopyIndex && string.IsNullOrEmpty(path))
                {
                    Console.Error.WriteLine("--path is required when not using --auto-copy-index");
                    Environment.Exit(1);
                    return;
                }

                try
                {
                    var actualPath = autoCopyIndex ? 
                        await AutoCopyIndexFromKubernetes(kubeconfig, deployment, namespaceName, indexPathInPod, path ?? "auto-index") : 
                        path!;

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
            });

            // New command specifically for initializing a non-SenseNet index
            initCommand.SetHandler(async (context) =>
            {
                var path = context.ParseResult.GetValueForOption(pathOption);
                var id = context.ParseResult.GetValueForOption(idOption);
                var backup = context.ParseResult.GetValueForOption(backupOption);
                var backupPath = context.ParseResult.GetValueForOption(backupPathOption);
                var offline = context.ParseResult.GetValueForOption(offlineOption);
                var kubeconfig = context.ParseResult.GetValueForOption(kubeconfigOption);
                var namespaceName = context.ParseResult.GetValueForOption(namespaceOption)!;
                var deployment = context.ParseResult.GetValueForOption(deploymentOption);
                var indexPathInPod = context.ParseResult.GetValueForOption(indexPathInPodOption)!;
                var autoCopyIndex = context.ParseResult.GetValueForOption(autoCopyIndexOption);

                if (autoCopyIndex && (string.IsNullOrEmpty(deployment) || string.IsNullOrEmpty(kubeconfig)))
                {
                    Console.Error.WriteLine("--deployment and --kubeconfig are required when using --auto-copy-index");
                    Environment.Exit(1);
                    return;
                }

                if (!autoCopyIndex && string.IsNullOrEmpty(path))
                {
                    Console.Error.WriteLine("--path is required when not using --auto-copy-index");
                    Environment.Exit(1);
                    return;
                }

                try
                {
                    var actualPath = autoCopyIndex ? 
                        await AutoCopyIndexFromKubernetes(kubeconfig, deployment, namespaceName, indexPathInPod, path ?? "auto-index") : 
                        path!;

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
