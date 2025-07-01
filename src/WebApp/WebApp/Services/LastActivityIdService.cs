using SenseNet.Tools;
using SenseNet.Search;
using SenseNet.Search.Lucene29;
using SenseNet.Search.Indexing;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
            var exists = Directory.Exists(normalizedPath);
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

        var directory = new IndexDirectory(indexPath);
        var engine = new Lucene29LocalIndexingEngine(directory);
        
        var status = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
        return new LastActivityInfo 
        { 
            LastActivityId = status.LastActivityId,
            Gaps = status.Gaps 
        };
    }

    public async Task SetLastActivityIdAsync(string indexPath, long id, bool backup = true, string? backupPath = null)
    {
        if (backup)
        {
            CreateBackup(indexPath, backupPath);
        }

        var directory = new IndexDirectory(indexPath);
        var engine = new Lucene29LocalIndexingEngine(directory);
        
        // Get current status to preserve gaps
        var currentStatus = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
        
        var newStatus = new IndexingActivityStatus
        {
            LastActivityId = (int)id,
            Gaps = currentStatus.Gaps
        };

        await engine.WriteActivityStatusToIndexAsync(newStatus, CancellationToken.None);

        // Verify the change
        var verificationStatus = await engine.ReadActivityStatusFromIndexAsync(CancellationToken.None);
        if (verificationStatus.LastActivityId != (int)id)
        {
            throw new InvalidOperationException($"Verification failed: LastActivityId was not properly updated. Expected {id}, got {verificationStatus.LastActivityId}");
        }
    }

    public async Task InitializeLastActivityIdAsync(string indexPath, long id, bool backup = true, string? backupPath = null)
    {
        if (backup)
        {
            CreateBackup(indexPath, backupPath);
        }

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
    }

    private void CreateBackup(string indexPath, string? backupPath)
    {
        if (string.IsNullOrEmpty(indexPath))
            throw new ArgumentException("Index path cannot be null or empty", nameof(indexPath));

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var indexName = new DirectoryInfo(indexPath).Name;
        var backupName = $"{indexName}_backup_{timestamp}";

        backupPath ??= Path.Combine(Path.GetDirectoryName(indexPath)!, "IndexBackups");
        Directory.CreateDirectory(backupPath);

        var backupFolderPath = Path.Combine(backupPath, backupName);
        Directory.CreateDirectory(backupFolderPath);

        foreach (var file in Directory.GetFiles(indexPath))
        {
            var destFile = Path.Combine(backupFolderPath, Path.GetFileName(file));
            File.Copy(file, destFile);
        }
    }
}
