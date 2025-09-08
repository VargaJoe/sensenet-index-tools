using SenseNet.Tools;
using SenseNet.Search;
using SenseNet.Search.Lucene29;
using SenseNet.Search.Indexing;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Store;
using Lucene.Net.Index;
using System.Collections.Generic;
using LuceneDirectory = Lucene.Net.Store.Directory;
using IODirectory = System.IO.Directory;
using Lucene.Net.Documents;
using Lucene.Net.Analysis.Standard;
using System.Data.SqlClient;

namespace WebApp.Services;

using WebApp.Models;

public class LastActivityIdService
{
    private readonly ILogger<LastActivityIdService> _logger;

    public LastActivityIdService(ILogger<LastActivityIdService> logger)
    {
        _logger = logger;
    }

    public bool ValidatePath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalizedPath = Path.GetFullPath(path);
            var exists = IODirectory.Exists(normalizedPath);
            _logger.LogInformation("Validating path: {Path}, Exists: {Exists}", normalizedPath, exists);
            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating path: {Path}", path);
            return false;
        }
    }

    public async Task<LastActivityInfo> GetLastActivityIdAsync(string indexPath)
    {
        _logger.LogInformation("Getting LastActivityId from index at {Path}", indexPath);
        
        if (!ValidatePath(indexPath))
        {
            throw new DirectoryNotFoundException($"Index directory not found: {indexPath}");
        }

        // First try using SenseNet API method
        try
        {
            var directory = new IndexDirectory(indexPath);
            var engine = new Lucene29LocalIndexingEngine(directory);
            
            var status = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
            
            if (status == null)
            {
                throw new InvalidOperationException("No LastActivityId found using SenseNet API");
            }
            
            _logger.LogInformation("Successfully read LastActivityId using SenseNet API: {LastActivityId}", status.LastActivityId);
            return new LastActivityInfo 
            { 
                LastActivityId = status.LastActivityId,
                Gaps = status.Gaps 
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SenseNet API method failed, falling back to direct Lucene.NET access");
        }

        // Fall back to direct Lucene.NET access
        try
        {
            using var directory = FSDirectory.Open(new DirectoryInfo(indexPath));
            if (IndexReader.IndexExists(directory))
            {
                using var reader = IndexReader.Open(directory, true);
                var commitUserData = reader.GetCommitUserData();
                if (commitUserData != null && commitUserData.TryGetValue("LastActivityId", out var lastActivityIdString))
                {
                    if (int.TryParse(lastActivityIdString, out var lastActivityId))
                    {
                        _logger.LogInformation("Successfully read LastActivityId using direct Lucene access: {LastActivityId}", lastActivityId);
                        return new LastActivityInfo
                        {
                            LastActivityId = lastActivityId,
                            Gaps = new int[0] // Gaps not available via direct access
                        };
                    }
                }
            }
            
            throw new InvalidOperationException("No LastActivityId found in index commit data");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading LastActivityId from index: {Path}", indexPath);
            throw;
        }
    }

    public async Task<long?> GetLastActivityIdFromDatabaseAsync(string connectionString)
    {
        _logger.LogInformation("Getting LastActivityId from database with connection: {ConnectionString}", 
            connectionString?.Substring(0, Math.Min(50, connectionString?.Length ?? 0)) + "...");
        
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));
        }

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            
            var query = "SELECT TOP 1 [IndexingActivityId] FROM [dbo].[IndexingActivities] ORDER BY IndexingActivityId DESC";
            using var command = new SqlCommand(query, connection);
            
            var result = await command.ExecuteScalarAsync();
            
            if (result == null || result == DBNull.Value)
            {
                _logger.LogWarning("No IndexingActivityId found in database");
                return null;
            }
            
            var lastActivityId = Convert.ToInt64(result);
            _logger.LogInformation("Successfully retrieved LastActivityId from database: {LastActivityId}", lastActivityId);
            return lastActivityId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting LastActivityId from database");
            throw;
        }
    }

    public async Task<LastActivityIdComparison> CompareLastActivityIdAsync(string indexPath, string connectionString)
    {
        _logger.LogInformation("Comparing LastActivityId between index and database");
        
        var comparison = new LastActivityIdComparison
        {
            IndexPath = indexPath,
            ConnectionString = connectionString
        };

        // Get index value
        try
        {
            var indexInfo = await GetLastActivityIdAsync(indexPath);
            comparison.IndexValue = indexInfo.LastActivityId;
            comparison.IndexGaps = indexInfo.Gaps;
            comparison.IndexRetrievedAt = DateTime.Now;
            _logger.LogInformation("Index LastActivityId: {IndexValue}", comparison.IndexValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting LastActivityId from index");
            // Continue to try database even if index fails
        }

        // Get database value
        try
        {
            comparison.DatabaseValue = await GetLastActivityIdFromDatabaseAsync(connectionString);
            comparison.DatabaseRetrievedAt = DateTime.Now;
            _logger.LogInformation("Database LastActivityId: {DatabaseValue}", comparison.DatabaseValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting LastActivityId from database");
            // Continue even if database fails
        }

        _logger.LogInformation("Comparison result - Index: {IndexValue}, Database: {DatabaseValue}, Status: {Status}", 
            comparison.IndexValue, comparison.DatabaseValue, comparison.Status);

        return comparison;
    }

    public async Task SetLastActivityIdAsync(string indexPath, long id, bool backup = true, string? backupPath = null)
    {
        if (backup)
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
            
            var newStatus = new IndexingActivityStatus
            {
                LastActivityId = (int)id,
                Gaps = currentStatus?.Gaps ?? Array.Empty<int>()
            };

            await engine.WriteActivityStatusToIndexAsync(newStatus, CancellationToken.None);

            // Verify the change
            var verificationStatus = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
            if (verificationStatus.LastActivityId != (int)id)
            {
                throw new InvalidOperationException($"Verification failed: LastActivityId was not properly updated. Expected {id}, got {verificationStatus.LastActivityId}");
            }
            
            _logger.LogInformation("Successfully set LastActivityId using SenseNet API: {LastActivityId}", id);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SenseNet API method failed for SetLastActivityId, falling back to direct Lucene.NET access");
        }

        // Fall back to direct Lucene.NET access
        try
        {
            await SetLastActivityIdDirectAsync(indexPath, id);
            _logger.LogInformation("Successfully set LastActivityId using direct Lucene access: {LastActivityId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting LastActivityId in index: {Path}", indexPath);
            throw;
        }
    }

    public async Task InitializeLastActivityIdAsync(string indexPath, long id, bool backup = true, string? backupPath = null)
    {
        if (backup)
        {
            CreateBackup(indexPath, backupPath);
        }

        // First try using SenseNet API method
        try
        {
            var directory = new IndexDirectory(indexPath);
            var engine = new Lucene29LocalIndexingEngine(directory);

            try
            {
                // Check if already initialized
                var currentStatus = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
                if (currentStatus != null)
                {
                    throw new InvalidOperationException($"Index already has LastActivityId set to {currentStatus.LastActivityId}. Use SetLastActivityIdAsync to modify it.");
                }
            }
            catch
            {
                // If reading fails, assume it needs initialization
            }

            var newStatus = new IndexingActivityStatus
            {
                LastActivityId = (int)id,
                Gaps = Array.Empty<int>()
            };

            await engine.WriteActivityStatusToIndexAsync(newStatus, CancellationToken.None);

            // Verify initialization
            var verificationStatus = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
            if (verificationStatus.LastActivityId != (int)id)
            {
                throw new InvalidOperationException($"Verification failed: LastActivityId was not properly initialized. Expected {id}, got {verificationStatus.LastActivityId}");
            }
            
            _logger.LogInformation("Successfully initialized LastActivityId using SenseNet API: {LastActivityId}", id);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SenseNet API method failed for InitializeLastActivityId, falling back to direct Lucene.NET access");
        }

        // Fall back to direct Lucene.NET access
        try
        {
            await SetLastActivityIdDirectAsync(indexPath, id);
            _logger.LogInformation("Successfully initialized LastActivityId using direct Lucene access: {LastActivityId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing LastActivityId in index: {Path}", indexPath);
            throw;
        }
    }

    private async Task SetLastActivityIdDirectAsync(string indexPath, long id)
    {
        await Task.Run(() =>
        {
            using var directory = FSDirectory.Open(new DirectoryInfo(indexPath));
            if (!IndexReader.IndexExists(directory))
            {
                throw new InvalidOperationException("Index does not exist or cannot be opened.");
            }

            // Check if index is locked and unlock if necessary
            if (IndexWriter.IsLocked(directory))
            {
                _logger.LogInformation("Index is locked. Attempting to unlock...");
                IndexWriter.Unlock(directory);
                _logger.LogInformation("Index unlocked successfully.");
            }

            // Get existing commit user data first
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
                _logger.LogWarning(ex, "Could not read existing commit data, continuing with empty data");
            }

            // Update the LastActivityId
            commitUserData["LastActivityId"] = id.ToString();
            _logger.LogInformation("Preparing to write LastActivityId = {LastActivityId} to index...", id);

            // Open the index writer with create=false (don't overwrite the existing index)
            using var indexWriter = new IndexWriter(directory,
                                                   new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29),
                                                   false, // don't create a new index
                                                   IndexWriter.MaxFieldLength.UNLIMITED);

            // Create and add commit document
            const string COMMITFIELDNAME = "CommitMarker";
            const string COMMITDATAFIELDNAME = "CommitData";
            
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
            _logger.LogInformation("Committing LastActivityId = {LastActivityId} to index...", id);
            indexWriter.Commit(commitUserData);
            _logger.LogInformation("Commit successful.");

            // Verify the change - use a new reader after closing the writer
            _logger.LogInformation("Verifying change with a new reader...");
            using var verifyReader = IndexReader.Open(directory, true);
            var verifyCommitUserData = verifyReader.GetCommitUserData();
            if (verifyCommitUserData != null && verifyCommitUserData.TryGetValue("LastActivityId", out var lastActivityId))
            {
                _logger.LogInformation("Verification: LastActivityId = {LastActivityId}", lastActivityId);
                if (lastActivityId != id.ToString())
                {
                    throw new InvalidOperationException($"Verification failed: LastActivityId value is different from expected: {lastActivityId} vs {id}");
                }
            }
            else
            {
                throw new InvalidOperationException("LastActivityId not found in commit user data during verification.");
            }
        });
    }

    private void CreateBackup(string indexPath, string? backupPath)
    {
        if (string.IsNullOrEmpty(indexPath))
            throw new ArgumentException("Index path cannot be null or empty", nameof(indexPath));

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var indexName = new DirectoryInfo(indexPath).Name;
        var backupName = $"{indexName}_backup_{timestamp}";

        backupPath ??= Path.Combine(Path.GetDirectoryName(indexPath)!, "IndexBackups");
        IODirectory.CreateDirectory(backupPath);

        var backupFolderPath = Path.Combine(backupPath, backupName);
        IODirectory.CreateDirectory(backupFolderPath);

        foreach (var file in IODirectory.GetFiles(indexPath))
        {
            var destFile = Path.Combine(backupFolderPath, Path.GetFileName(file));
            File.Copy(file, destFile);
        }
    }
}
