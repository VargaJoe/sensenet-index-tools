using System.Data.SqlClient;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Directory = System.IO.Directory;

namespace SenseNetIndexTools;

public enum IndexSourceKind { Live, RepositoryBackup, OfflineCopy, WorkingCopy }

public sealed class IndexSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public IndexSourceKind Kind { get; set; }
    public string Path { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? ParentId { get; set; }
    public string? RepositoryTarget { get; set; }
    public int? CapturedActivityId { get; set; }
    public string RepositoryBinding { get; set; } = "";
    public bool Writable => Kind == IndexSourceKind.WorkingCopy;
}

public sealed class IndexComparison
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceId { get; set; } = "";
    public string Scope { get; set; } = "";
    public bool Recursive { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public long IndexVersion { get; set; }
    public List<ContentItem> Items { get; set; } = [];
    public List<ContentItem> Orphans { get; set; } = [];
    public bool CleanupApplied { get; set; }
    public string RepositoryBinding { get; set; } = "";
    public IEnumerable<ContentItem> RepairCandidates => Items.Where(i => i.InDatabase && i.Status != "Match").DistinctBy(i => i.NodeId);
}

public sealed record RepositoryRepairResult(int NodeId, string Path, bool Success, string Message, long? ActivityId = null);

/// <summary>Shared CLI/web workflow. Source files and repository mutations have separate entry points.</summary>
public sealed class IndexWorkspace
{
    private static readonly SemaphoreSlim BackupGate = new(1, 1);
    private static readonly SemaphoreSlim WriteGate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly RuntimeSettings _settings;
    private readonly HttpClient _http;
    private string Data => Path.Combine(_settings.DataDirectory ?? "App_Data", "workspace");

    public IndexWorkspace(HttpClient http) : this(RuntimeSettings.Load(), http) { }
    public IndexWorkspace(RuntimeSettings settings, HttpClient http) { _settings = settings; _http = http; }

    public List<IndexSource> Sources()
    {
        var sources = new List<IndexSource>();
        if (!string.IsNullOrWhiteSpace(_settings.SourcePath) && Directory.Exists(_settings.SourcePath))
            sources.Add(new() { Id = "live", Name = "Live index (read only)", Kind = IndexSourceKind.Live, Path = IndexSnapshot.Resolve(_settings.SourcePath), CreatedUtc = DateTime.MinValue, RepositoryBinding = Binding() });
        var folder = Path.Combine(Data, "sources");
        if (Directory.Exists(folder))
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                var source = JsonSerializer.Deserialize<IndexSource>(File.ReadAllText(file))!;
                if (source.RepositoryBinding != Binding()) continue;
                ValidateSource(source);
                sources.Add(source);
            }
        return sources.OrderByDescending(s => s.CreatedUtc).ToList();
    }

    public IndexSource GetSource(string id) => Sources().SingleOrDefault(s => s.Id == id)
        ?? throw new InvalidOperationException("Select an available index source.");

    public async Task<IndexSource> BackupAsync(string? folderName = null, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.RepositoryBackupRoot) || string.IsNullOrWhiteSpace(_settings.BackupSourceRoot))
            throw new InvalidOperationException("Configure the repository backup target root and its tool-visible read-only mount.");
        folderName = string.IsNullOrWhiteSpace(folderName) ? "backup-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] : folderName;
        if (!Regex.IsMatch(folderName, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$")) throw new ArgumentException("Use a folder name containing letters, digits, hyphens or underscores.");
        var target = _settings.RepositoryBackupRoot.TrimEnd('/', '\\') + "/" + folderName;
        var mapped = Path.Combine(_settings.BackupSourceRoot, folderName);
        WorkspacePaths.RejectLinks(mapped);
        if (Directory.Exists(mapped) || File.Exists(mapped)) throw new InvalidOperationException("Choose a new backup folder; existing backups are never overwritten.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.BackupTimeoutSeconds));
        var token = timeout.Token;
        await BackupGate.WaitAsync(token);
        try
        {
            var prior = await QueryBackupAsync(token);
            if (prior.State is "Executing" or "Started" or "CancelRequested") throw new InvalidOperationException("The repository already has a running backup. Wait for it to finish.");
            if ((prior.History ?? []).Append(prior.Current).Any(entry => entry != null &&
                NormalizeTarget(entry.TargetPath) == NormalizeTarget(target)))
                throw new InvalidOperationException("Choose a new backup folder name; this target is already in the repository's backup history.");
            progress?.Report("Requesting a repository-managed backup...");
            using var request = Request(HttpMethod.Post, "BackupIndex");
            request.Content = JsonContent.Create(new { target });
            var response = await SendBackupAsync(request, token);
            if (response.State is not ("Started" or "Executing" or "Finished")) throw new InvalidOperationException("The repository did not start the backup.");
            while (true)
            {
                var entries = (response.History ?? []).Append(response.Current).Where(e => e != null).Cast<BackupEntry>();
                var own = entries.FirstOrDefault(e => NormalizeTarget(e.TargetPath) == NormalizeTarget(target));
                if (own != null && own.FinishedAt > DateTime.MinValue)
                {
                    if (own.Message is not (null or "Finished") || own.CopiedFiles != own.CountOfFiles || own.CopiedBytes != own.TotalBytes)
                        throw new InvalidOperationException("The requested backup failed or is incomplete; its folder was not selected.");
                    var resolved = IndexSnapshot.Resolve(mapped);
                    var source = new IndexSource { Name = folderName, Kind = IndexSourceKind.RepositoryBackup, Path = resolved,
                        RepositoryTarget = target, CapturedActivityId = CapturedActivity(resolved), RepositoryBinding = Binding() };
                    Save("sources", source.Id, source);
                    progress?.Report("Backup completed and its index was verified.");
                    return source;
                }
                if (response.State is "Faulted" or "Canceled" or "Initial" or "Finished")
                    throw new InvalidOperationException("No completed backup matching this request was found. Source selection was unchanged.");
                progress?.Report(own == null ? "Waiting for the requested backup..." : $"Backup: {own.CopiedFiles}/{own.CountOfFiles} files, {own.CopiedBytes}/{own.TotalBytes} bytes");
                await Task.Delay(_settings.BackupPollMilliseconds, token);
                response = await QueryBackupAsync(token);
            }
        }
        finally { BackupGate.Release(); }
    }

    public async Task<int> RecoverCompletedBackupsAsync(CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.RepositoryBackupRoot) || string.IsNullOrWhiteSpace(_settings.BackupSourceRoot))
            throw new InvalidOperationException("Configure both backup roots before discovering completed backups.");
        var status = await QueryBackupAsync(cancellation);
        var existing = Sources();
        var count = 0;
        foreach (var entry in (status.History ?? []).Append(status.Current).Where(e => e != null).Cast<BackupEntry>())
        {
            var target = NormalizeTarget(entry.TargetPath);
            var root = NormalizeTarget(_settings.RepositoryBackupRoot) + "/";
            if (!target.StartsWith(root, StringComparison.Ordinal) || entry.FinishedAt == DateTime.MinValue ||
                entry.Message is not (null or "Finished") || entry.CopiedBytes != entry.TotalBytes || entry.CopiedFiles != entry.CountOfFiles) continue;
            var name = target[root.Length..];
            if (!Regex.IsMatch(name, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$")) continue;
            var mapped = Path.Combine(_settings.BackupSourceRoot, name);
            if (!Directory.Exists(mapped) || existing.Any(s => s.RepositoryTarget == target)) continue;
            WorkspacePaths.RejectLinks(mapped);
            var path = IndexSnapshot.Resolve(mapped);
            var source = new IndexSource { Name = name, Kind = IndexSourceKind.RepositoryBackup, Path = path,
                CreatedUtc = entry.FinishedAt.ToUniversalTime(), RepositoryTarget = target,
                CapturedActivityId = CapturedActivity(path), RepositoryBinding = Binding() };
            Save("sources", source.Id, source); existing.Add(source); count++;
        }
        return count;
    }

    public IndexSource Copy(string sourceId, bool offlineConfirmed = false)
    {
        var parent = GetSource(sourceId);
        if (parent.Kind == IndexSourceKind.Live && !offlineConfirmed)
            throw new InvalidOperationException("For a running repository, use its backup API. Offline copying requires confirmation that all index writers are stopped.");
        if (parent.Kind == IndexSourceKind.Live)
        {
            WorkspacePaths.EnsureNoWriter(parent.Path);
        }
        var copy = IndexSnapshot.Create(parent.Path, _settings.SnapshotDirectory ?? "IndexCopies");
        var result = new IndexSource { Name = "Working copy of " + parent.Name, Kind = IndexSourceKind.WorkingCopy,
            ParentId = parent.Id, Path = copy, CapturedActivityId = CapturedActivity(copy), RepositoryBinding = Binding() };
        Save("sources", result.Id, result);
        return result;
    }

    public async Task<IndexComparison> CompareAsync(string sourceId, string scope, bool recursive, CancellationToken cancellation = default)
    {
        _ = RebuildIndexRequest.GetActionUrl("https://repository.example.invalid", scope, null);
        var source = GetSource(sourceId);
        var version = Version(source.Path);
        var items = await Task.Run(() => new ContentComparer().CompareContent(source.Path, _settings.ConnectionString, scope, recursive, 0), cancellation);
        if (version != Version(source.Path)) throw new InvalidOperationException("Index changed during comparison. Prepare a backup or repeat the comparison.");
        var report = new IndexComparison { SourceId = sourceId, Scope = scope, Recursive = recursive, IndexVersion = version, Items = items, RepositoryBinding = Binding() };
        // A version outside the selected subtree is not an orphan. Check the whole database.
        foreach (var item in OrphanedIndexCleaner.FindCandidates(items))
            if (!await VersionExistsAsync(item, cancellation)) report.Orphans.Add(item);
        Save("comparisons", report.Id, report);
        return report;
    }

    public IndexComparison GetComparison(string id) => Read<IndexComparison>("comparisons", id);

    public async Task<int> CleanAsync(string reportId, bool confirmed, CancellationToken cancellation = default)
    {
        if (!confirmed) throw new InvalidOperationException("Confirm the previewed orphan cleanup on the selected working copy.");
        await WriteGate.WaitAsync(cancellation);
        try
        {
            var report = GetComparison(reportId);
            CheckBinding(report);
            var source = GetSource(report.SourceId);
            RequireWorkingCopy(source);
            if (report.CleanupApplied || Version(source.Path) != report.IndexVersion) throw new InvalidOperationException("The working copy changed. Run a fresh comparison before cleanup.");
            foreach (var item in report.Orphans)
                if (await VersionExistsAsync(item, cancellation)) throw new InvalidOperationException("An orphan candidate now exists in SQL. Compare again before cleanup.");
            var removed = OrphanedIndexCleaner.Remove(source.Path, report.Orphans);
            report.CleanupApplied = true;
            Save("comparisons", report.Id, report);
            return removed;
        }
        finally { WriteGate.Release(); }
    }

    public async Task<List<RepositoryRepairResult>> RepairAsync(string reportId, IEnumerable<int> selectedIds, bool confirmed, CancellationToken cancellation = default, IProgress<string>? progress = null)
    {
        if (!confirmed) throw new InvalidOperationException("Confirm the selected non-recursive IndexOnly requests to the repository.");
        var report = GetComparison(reportId);
        CheckBinding(report);
        var eligible = report.RepairCandidates.ToDictionary(i => i.NodeId);
        var ids = selectedIds.Distinct().ToArray();
        if (ids.Length == 0 || ids.Any(id => !eligible.ContainsKey(id))) throw new InvalidOperationException("Select only repair candidates from this report.");
        var results = new List<RepositoryRepairResult>();
        var operationId = Guid.NewGuid().ToString("N");
        var started = DateTime.UtcNow;
        Save("repairs", operationId, new { ReportId = reportId, SelectedNodeIds = ids, StartedUtc = started, Completed = false, Results = results });
        foreach (var id in ids)
        {
            var item = eligible[id];
            long? activityId = null;
            progress?.Report($"Rebuilding {results.Count + 1}/{ids.Length}: {item.Path}");
            try
            {
                var before = await DatabaseActivityAsync(cancellation);
                var action = RebuildIndexRequest.GetActionUrl(_settings.RepositoryUrl!, item.Path, null);
                using (var read = Auth(new HttpRequestMessage(HttpMethod.Get, action[..^"/RebuildIndex".Length] + "?$select=Id")))
                using (var response = await _http.SendAsync(read, cancellation))
                {
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"REST identity check returned HTTP {(int)response.StatusCode}; rebuild was not sent.");
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
                    var root = json.RootElement.TryGetProperty("d", out var d) ? d : json.RootElement;
                    if (root.GetProperty("Id").GetInt32() != id) throw new InvalidOperationException("SQL and REST content identities differ; rebuild was not sent.");
                }
                using (var request = RebuildIndexRequest.Create(_settings.RepositoryUrl!, _settings.ApiKey, item.Path, null, false, "IndexOnly", _settings.BearerToken))
                using (var response = await _http.SendAsync(request, cancellation))
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Rebuild request returned HTTP {(int)response.StatusCode}.");
                for (var attempt = 0; attempt < 60; attempt++)
                {
                    activityId = await RebuildActivityAsync(id, before, cancellation);
                    var status = ActivityStatusReader.Read(_settings.SourcePath!);
                    if (activityId != null && status.LastActivityId >= activityId && !status.Gaps.Contains((int)activityId)) break;
                    await Task.Delay(1000, cancellation);
                }
                var processed = ActivityStatusReader.Read(_settings.SourcePath!);
                if (activityId == null || processed.LastActivityId < activityId || processed.Gaps.Contains((int)activityId))
                    throw new InvalidOperationException("Request was accepted, but the selected rebuild activity was not verified. Do not assume completion.");
                var fresh = new ContentComparer().CompareContent(IndexSnapshot.Resolve(_settings.SourcePath!), _settings.ConnectionString, item.Path, false, 0);
                if (fresh.Count == 0 || fresh.Any(i => i.Status != "Match")) throw new InvalidOperationException("Rebuild was processed but fresh SQL/index comparison still has differences.");
                results.Add(new(id, item.Path, true, "Rebuild activity processed; fresh SQL/index comparison matches.", activityId));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { results.Add(new(id, item.Path, false, SecretRedactor.Redact(error.Message, _settings), activityId)); }
            finally
            {
                // Retain a receipt even when waiting is canceled after the server accepted a request.
                Save("repairs", operationId, new { ReportId = reportId, SelectedNodeIds = ids, StartedUtc = started,
                    Completed = results.Count == ids.Length, PendingNodeId = results.Any(r => r.NodeId == id) ? (int?)null : id, Results = results });
            }
        }
        return results;
    }

    public void RequireWorkingCopy(IndexSource source)
    {
        ValidateSource(source);
        if (!source.Writable || _settings.SnapshotDirectory == null || !WorkspacePaths.IsWithin(source.Path, _settings.SnapshotDirectory) ||
            !File.Exists(Path.Combine(source.Path, "snapshot-manifest.json")))
            throw new InvalidOperationException("This operation requires a managed working copy. Live indexes and original backups are read only.");
        RuntimeSettings.EnsureWritableCopy(source.Path, _settings);
    }

    public async Task<long> DatabaseActivityAsync(CancellationToken cancellation = default)
        => Convert.ToInt64(await ScalarAsync("SELECT ISNULL(MAX(IndexingActivityId),0) FROM dbo.IndexingActivities", [], cancellation));

    private async Task<long?> RebuildActivityAsync(int nodeId, long before, CancellationToken cancellation)
    {
        var value = await ScalarAsync("SELECT MAX(IndexingActivityId) FROM dbo.IndexingActivities WHERE NodeId=@node AND ActivityType='Rebuild' AND IndexingActivityId>@before",
            [new("@node", nodeId), new("@before", before)], cancellation);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private async Task<bool> VersionExistsAsync(ContentItem item, CancellationToken cancellation)
    {
        if (!int.TryParse(item.IndexVersionId, out var version) || !int.TryParse(item.IndexNodeId, out var node))
            throw new InvalidOperationException("Invalid node/version identity in an orphan candidate.");
        return Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM dbo.Versions WHERE VersionId=@version AND NodeId=@node",
            [new("@version", version), new("@node", node)], cancellation)) > 0;
    }

    private async Task<object?> ScalarAsync(string sql, SqlParameter[] parameters, CancellationToken cancellation)
    {
        using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellation);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync(cancellation);
    }

    private async Task<BackupStatus> QueryBackupAsync(CancellationToken token)
    {
        using var request = Request(HttpMethod.Get, "QueryIndexBackup");
        return await SendBackupAsync(request, token);
    }
    private async Task<BackupStatus> SendBackupAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var response = await _http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Repository backup API returned HTTP {(int)response.StatusCode}. Check its URL and the operator's backup permissions.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = json.RootElement.TryGetProperty("d", out var d) ? d : json.RootElement;
        return root.Deserialize<BackupStatus>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Invalid backup status response.");
    }
    private HttpRequestMessage Request(HttpMethod method, string operation) => Auth(new(method,
        RebuildIndexRequest.GetActionUrl(_settings.RepositoryUrl!, "/Root", null).Replace("/RebuildIndex", "/" + operation)));
    private HttpRequestMessage Auth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_settings.BearerToken)) request.Headers.Authorization = new("Bearer", _settings.BearerToken);
        else if (!string.IsNullOrWhiteSpace(_settings.ApiKey)) request.Headers.Add("apikey", _settings.ApiKey);
        else throw new InvalidOperationException("Configure an API key or bearer secret file.");
        return request;
    }
    private static string NormalizeTarget(string? target) => (target ?? "").Replace('\\', '/').TrimEnd('/');
    private static int? CapturedActivity(string path)
    {
        try { return ActivityStatusReader.Read(path).LastActivityId; }
        catch (InvalidOperationException) { return null; } // Preserve readable copies for metadata diagnosis/initialization.
    }
    public static long Version(string path) { using var directory = FSDirectory.Open(new DirectoryInfo(path)); using var reader = IndexReader.Open(directory, true); return reader.GetVersion(); }
    private void ValidateSource(IndexSource source)
    {
        if (source.RepositoryBinding != Binding()) throw new InvalidOperationException("This source belongs to a different repository configuration.");
        var root = source.Kind switch { IndexSourceKind.Live => _settings.SourcePath, IndexSourceKind.RepositoryBackup => _settings.BackupSourceRoot, _ => _settings.SnapshotDirectory };
        if (root == null || !WorkspacePaths.IsWithin(source.Path, root)) throw new InvalidOperationException("Stored source is outside its configured root.");
        WorkspacePaths.RejectLinks(source.Path);
    }
    private string Binding()
    {
        var connection = new SqlConnectionStringBuilder(_settings.ConnectionString);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((_settings.RepositoryUrl ?? "").TrimEnd('/') + "\n" + connection.DataSource.ToUpperInvariant() + "\n" + connection.InitialCatalog.ToUpperInvariant())));
    }
    private void CheckBinding(IndexComparison report)
    {
        if (report.RepositoryBinding != Binding()) throw new InvalidOperationException("This report belongs to a different repository configuration. Create a fresh comparison.");
    }
    private void Save<T>(string category, string id, T value)
    {
        if (!Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new ArgumentException("Invalid workspace record ID.");
        var folder = Path.Combine(Data, category); Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, id + ".json");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
        File.Move(temporary, file, true);
    }
    private T Read<T>(string category, string id)
    {
        if (!Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new ArgumentException("Invalid workspace record ID.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Data, category, id + ".json")))!;
    }
    private sealed class BackupStatus { public string State { get; set; } = ""; public BackupEntry? Current { get; set; } public BackupEntry[] History { get; set; } = []; }
    private sealed class BackupEntry
    {
        public string? TargetPath { get; set; } public DateTime StartedAt { get; set; } public DateTime FinishedAt { get; set; }
        public long TotalBytes { get; set; } public long CopiedBytes { get; set; } public int CountOfFiles { get; set; } public int CopiedFiles { get; set; } public string? Message { get; set; }
    }
}
