using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using SenseNet.Search;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29;
using IODirectory = System.IO.Directory;
using Document = Lucene.Net.Documents.Document;

namespace SenseNet.IndexTools.Core.Services
{
    /// <summary>
    /// Service for managing LastActivityId in SenseNet indexes
    /// </summary>
    public class LastActivityIdService
    {
        private readonly ILogger<LastActivityIdService> _logger;
        private const string COMMITFIELDNAME = "$#COMMIT";
        private const string COMMITDATAFIELDNAME = "$#DATA";

        public LastActivityIdService(ILogger<LastActivityIdService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets the current LastActivityId from a SenseNet index
        /// </summary>
        /// <param name="indexPath">Path to the Lucene index directory</param>
        /// <returns>LastActivityId and any activity gaps found</returns>
        public async Task<(long LastActivityId, IEnumerable<long>? Gaps)> GetLastActivityIdAsync(string indexPath)
        {
            _logger.LogInformation("Opening index directory: {Path}", indexPath);

            // First verify this is a valid Lucene index
            if (!IsValidLuceneIndex(indexPath))
            {
                _logger.LogError("The directory does not appear to be a valid Lucene index: {Path}", indexPath);
                throw new InvalidOperationException($"Not a valid Lucene index: {indexPath}");
            }

            // First try using SenseNet API method
            try
            {
                var directory = new IndexDirectory(indexPath);
                _logger.LogDebug("Created IndexDirectory object successfully.");

                var engine = new Lucene29LocalIndexingEngine(directory);
                _logger.LogDebug("Created Lucene29LocalIndexingEngine object successfully.");                _logger.LogInformation("Attempting to read activity status using SenseNet API...");
                var status = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
                _logger.LogInformation("Last activity ID: {LastActivityId}", status.LastActivityId);
                
                IEnumerable<long>? convertedGaps = null;
                if (status.Gaps?.Any() == true)
                {
                    // Convert int[] to IEnumerable<long>
                    convertedGaps = status.Gaps.Select(g => (long)g);
                    _logger.LogInformation("Activity gaps: {Gaps}", string.Join(", ", convertedGaps));
                }

                return (status.LastActivityId, convertedGaps);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SenseNet API method failed: {Message}", ex.Message);
                _logger.LogInformation("Falling back to direct Lucene.NET access method...");
            }

            // Fall back to direct Lucene.NET access
            try
            {
                using var directory = FSDirectory.Open(new DirectoryInfo(indexPath));
                if (!IndexReader.IndexExists(directory))
                {
                    _logger.LogError("No valid Lucene index found at {Path}", indexPath);
                    throw new InvalidOperationException($"No valid Lucene index found at {indexPath}");
                }

                using var reader = IndexReader.Open(directory, true);
                var commitUserData = reader.GetCommitUserData();
                if (commitUserData != null && commitUserData.ContainsKey("LastActivityId"))
                {
                    var lastActivityId = commitUserData["LastActivityId"];
                    if (long.TryParse(lastActivityId, out var id))
                    {
                        _logger.LogInformation("Last activity ID (direct method): {LastActivityId}", id);
                        return (id, null);
                    }
                }

                _logger.LogWarning("Could not find LastActivityId in the index's commit user data");
                return (0, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accessing Lucene index directly: {Message}", ex.Message);
                throw new InvalidOperationException($"Error accessing Lucene index: {ex.Message}", ex);
            }
        }        /// <summary>
        /// Sets the LastActivityId in a SenseNet index
        /// </summary>
        /// <param name="indexPath">Path to the Lucene index directory</param>
        /// <param name="newId">The new LastActivityId value to set</param>
        /// <param name="createBackup">Whether to create a backup before modification</param>
        /// <param name="backupPath">Optional custom backup path</param>
        /// <returns>True if successful</returns>
        public async Task<bool> SetLastActivityIdAsync(string indexPath, long newId, bool createBackup = true, string? backupPath = null)
        {
            _logger.LogInformation("Setting LastActivityId to {NewId} in index at {Path}", newId, indexPath);

            // Verify index
            if (!IsValidLuceneIndex(indexPath))
            {
                throw new InvalidOperationException($"Not a valid Lucene index: {indexPath}");
            }

            if (createBackup)
            {
                CreateBackup(indexPath, backupPath);
            }

            // First try using SenseNet API method
            try
            {
                var directory = new IndexDirectory(indexPath);
                var engine = new Lucene29LocalIndexingEngine(directory);

                // Get current status to preserve gaps
                var currentStatus = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);

                // Create new status, preserving gaps
                var newStatus = new IndexingActivityStatus
                {
                    LastActivityId = (int)newId,
                    Gaps = currentStatus?.Gaps ?? Array.Empty<int>()
                };

                _logger.LogInformation("Writing updated activity status using SenseNet API...");
                await engine.WriteActivityStatusToIndexAsync(newStatus, CancellationToken.None);

                // Verify the change
                var verificationStatus = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
                if (verificationStatus.LastActivityId == (int)newId)
                {
                    _logger.LogInformation("LastActivityId updated successfully");
                    return true;
                }

                _logger.LogWarning("Verification returned different value: {VerificationId}", verificationStatus.LastActivityId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SenseNet API method failed: {Message}", ex.Message);
                _logger.LogInformation("Falling back to direct Lucene.NET access method...");
            }

            // Fallback to direct access
            try
            {
                using var directory = FSDirectory.Open(new DirectoryInfo(indexPath));
                if (!IndexReader.IndexExists(directory))
                {
                    throw new InvalidOperationException($"No valid Lucene index found at {indexPath}");
                }

                // Check for locks
                if (IndexWriter.IsLocked(directory))
                {
                    _logger.LogInformation("Index is locked. Attempting to unlock...");
                    IndexWriter.Unlock(directory);
                }

                using var indexWriter = new IndexWriter(directory, 
                    new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29), 
                    false, 
                    IndexWriter.MaxFieldLength.UNLIMITED);

                // Get existing commit user data
                var commitUserData = new Dictionary<string, string>();
                try
                {
                    using var reader = IndexReader.Open(directory, true);
                    var existingData = reader.GetCommitUserData();
                    if (existingData != null)
                    {
                        foreach (var entry in existingData)
                        {
                            commitUserData[entry.Key] = entry.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not read existing commit data: {Message}", ex.Message);
                }

                // Update the LastActivityId
                commitUserData["LastActivityId"] = newId.ToString();

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

                // Update the document
                indexWriter.UpdateDocument(new Term(COMMITFIELDNAME, COMMITFIELDNAME), doc);

                // Commit changes
                indexWriter.Commit(commitUserData);
                indexWriter.Close();

                // Verify the change
                using (var reader = IndexReader.Open(directory, true))
                {
                    var verifyCommitUserData = reader.GetCommitUserData();
                    if (verifyCommitUserData != null && verifyCommitUserData.ContainsKey("LastActivityId"))
                    {
                        var lastActivityId = verifyCommitUserData["LastActivityId"];
                        if (lastActivityId == newId.ToString())
                        {
                            _logger.LogInformation("LastActivityId updated successfully");
                            return true;
                        }
                        _logger.LogWarning("Verification returned different value: {VerificationId}", lastActivityId);
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting LastActivityId: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Initializes LastActivityId in a non-SenseNet Lucene index
        /// </summary>
        public async Task<bool> InitLastActivityIdAsync(string indexPath, long initialId, bool createBackup = true, string? backupPath = null)
        {
            _logger.LogInformation("Initializing LastActivityId to {InitialId} in index at {Path}", initialId, indexPath);

            // Check if LastActivityId already exists
            try
            {
                using var directory = FSDirectory.Open(new DirectoryInfo(indexPath));
                if (IndexReader.IndexExists(directory))
                {
                    using var reader = IndexReader.Open(directory, true);
                    var commitUserData = reader.GetCommitUserData();
                    if (commitUserData != null && commitUserData.ContainsKey("LastActivityId"))
                    {
                        var lastActivityId = commitUserData["LastActivityId"];
                        throw new InvalidOperationException($"Index already has LastActivityId: {lastActivityId}. Use SetLastActivityId to modify existing value.");
                    }
                }
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                _logger.LogWarning("Error checking existing LastActivityId: {Message}", ex.Message);
            }

            // Try SenseNet API first
            try
            {
                var directory = new IndexDirectory(indexPath);
                var engine = new Lucene29LocalIndexingEngine(directory);

                var newStatus = new IndexingActivityStatus
                {
                    LastActivityId = (int)initialId,
                    Gaps = Array.Empty<int>()
                };

                await engine.WriteActivityStatusToIndexAsync(newStatus, CancellationToken.None);

                // Verify
                var verificationStatus = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
                if (verificationStatus.LastActivityId == (int)initialId)
                {
                    _logger.LogInformation("LastActivityId initialized successfully");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SenseNet API initialization failed: {Message}", ex.Message);
                _logger.LogInformation("Falling back to direct Lucene.NET access method...");
            }

            // Fall back to direct Lucene.NET access
            return await SetLastActivityIdAsync(indexPath, initialId, createBackup, backupPath);
        }

        /// <summary>
        /// Creates a backup of the index directory
        /// </summary>
        public string CreateBackup(string indexPath, string? backupPath = null)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupRoot = !string.IsNullOrEmpty(backupPath) ? 
                backupPath : 
                Path.Combine(Path.GetDirectoryName(indexPath.TrimEnd('\\', '/')) ?? ".", "IndexBackups");

            var backupPathFinal = Path.Combine(backupRoot, $"backup_{timestamp}");
            _logger.LogInformation("Creating backup at {BackupPath}", backupPathFinal);

            // Ensure backup directory exists
            IODirectory.CreateDirectory(backupPathFinal);

            // Copy all files from the index directory to the backup
            foreach (var file in IODirectory.GetFiles(indexPath))
            {
                File.Copy(file, Path.Combine(backupPathFinal, Path.GetFileName(file)));
            }

            _logger.LogInformation("Backup completed successfully");
            return backupPathFinal;
        }

        private bool IsValidLuceneIndex(string path)
        {
            if (!IODirectory.Exists(path))
                return false;

            try
            {
                using var directory = FSDirectory.Open(new DirectoryInfo(path));
                return IndexReader.IndexExists(directory);
            }
            catch
            {
                return false;
            }
        }
    }
}
