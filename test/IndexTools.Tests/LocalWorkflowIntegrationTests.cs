using System.Data.SqlClient;
using System.Text.Json;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging.Abstractions;
using SenseNetIndexTools;
using WebApp.Services;
using Xunit;
using System.Security.Cryptography;

namespace IndexTools.Tests;

public class LocalWorkflowIntegrationTests
{
    [LocalLabFact]
    public async Task NativeBackupReportRepairAndIsolatedOrphanCleanup()
    {
        var settings = RuntimeSettings.Load();
        // This destructive fixture may only run against the disposable workspace lab.
        Assert.Equal("repository", new Uri(settings.RepositoryUrl!).Host);
        Assert.Equal("IndexToolsLab", new SqlConnectionStringBuilder(settings.ConnectionString).InitialCatalog);
        Assert.Equal("/source/repository/LocalIndex", settings.SourcePath);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var workspace = new IndexWorkspace(settings, http);
        var reports = new OperatorReportService(workspace, new ReportStorageService(NullLogger<ReportStorageService>.Instance, new TestEnvironment("/state")));
        const string scope = "/Root/Content/IndexToolsLab";
        var backup = await workspace.BackupAsync("acceptance-" + Guid.NewGuid().ToString("N")[..10]);
        var backupVersion = IndexWorkspace.Version(backup.Path);
        var originalHashes = System.IO.Directory.GetFiles(backup.Path).ToDictionary(p => Path.GetFileName(p)!, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        var cleanReport = await reports.CompareAsync(backup.Id, scope, true, CancellationToken.None);
        Assert.NotEmpty(cleanReport.Items);
        Assert.All(cleanReport.Items, row => Assert.Equal("Match", row.Status));
        var repairTarget = Assert.Single(cleanReport.Items, i => i.Path.EndsWith("/repairtarget", StringComparison.OrdinalIgnoreCase) && i.InDatabase);
        var rootVersion = Assert.Single(ContentComparer.ReadIndexItems(backup.Path, "/Root", false));
        var working = workspace.Copy(backup.Id);
        AddIsolatedDefects(working.Path, repairTarget.NodeId, rootVersion, scope);
        var report = await reports.CompareAsync(working.Id, scope, true, CancellationToken.None);
        Assert.Contains(report.Items, i => i.Status == "DB only" && i.NodeId == repairTarget.NodeId);
        Assert.Contains(report.Items, i => i.Status == "Index only" && i.IndexNodeId == rootVersion.IndexNodeId);
        Assert.Equal("2147000000", Assert.Single(report.Orphans).IndexNodeId);
        Assert.DoesNotContain(report.Orphans, i => i.IndexNodeId == rootVersion.IndexNodeId); // Exists outside the subtree.
        var repair = Assert.Single(await workspace.RepairAsync(report.Id, [repairTarget.NodeId], true));
        Assert.True(repair.Success, repair.Message);
        Assert.True(repair.ActivityId > backup.CapturedActivityId);
        Assert.Equal(1, await workspace.CleanAsync(report.Id, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CleanAsync(report.Id, true));
        var verified = await reports.CompareAsync(working.Id, scope, true, CancellationToken.None);
        Assert.Empty(verified.Orphans);
        Assert.Contains(verified.Items, i => i.IndexNodeId == rootVersion.IndexNodeId); // Not deleted as a false orphan.
        Assert.Equal(backupVersion, IndexWorkspace.Version(backup.Path));
        Assert.Equal(backup.CapturedActivityId, ActivityStatusReader.Read(backup.Path).LastActivityId);
        foreach (var entry in originalHashes) Assert.Equal(entry.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(backup.Path, entry.Key!)))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CleanAsync(cleanReport.Id, true));
        Assert.Throws<InvalidOperationException>(() => RuntimeSettings.EnsureWritableCopy(settings.SourcePath!));
        var freshBackup = await workspace.BackupAsync("verified-" + Guid.NewGuid().ToString("N")[..10]);
        var fresh = await reports.CompareAsync(freshBackup.Id, scope, true, CancellationToken.None);
        Assert.All(fresh.Items, row => Assert.Equal("Match", row.Status));
        var unchangedContent = Assert.Single(fresh.Items, i => i.NodeId == repairTarget.NodeId);
        Assert.Equal(repairTarget.TimestampNumeric, unchangedContent.TimestampNumeric);
        Assert.Equal(repairTarget.VersionTimestampNumeric, unchangedContent.VersionTimestampNumeric);
        File.WriteAllText("/state/reports/local-acceptance.json", JsonSerializer.Serialize(new {
            Success = true, BackupId = backup.Id, WorkingCopyId = working.Id, OriginalReportId = report.Id,
            RebuildNodeId = repairTarget.NodeId, RebuildActivityId = repair.ActivityId, OrphansRemoved = 1,
            FalseOrphanPreserved = true, OriginalBackupPreserved = true, FreshBackupId = freshBackup.Id, FreshRows = fresh.Items.Count
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (Environment.GetEnvironmentVariable("INDEXTOOLS_PREPARE_BROWSER_FIXTURE") == "1")
        {
            var browserCopy = workspace.Copy(freshBackup.Id);
            AddIsolatedDefects(browserCopy.Path, repairTarget.NodeId, rootVersion, scope);
            var browserReport = await reports.CompareAsync(browserCopy.Id, scope, true, CancellationToken.None);
            File.WriteAllText("/state/reports/browser-fixture.json", JsonSerializer.Serialize(new { SourceId = browserCopy.Id, ReportId = browserReport.Id, NodeId = repairTarget.NodeId, FreshSourceId = freshBackup.Id }));
        }
    }

    internal static void AddIsolatedDefects(string path, int missingNode, ContentItem outsideScope, string scope)
    {
        var targets = ContentComparer.ReadIndexItems(path, scope).Where(i => i.IndexNodeId == missingNode.ToString()).ToArray();
        Assert.NotEmpty(targets);
        Dictionary<string, string> commit;
        using (var directory = FSDirectory.Open(new DirectoryInfo(path)))
        using (var reader = IndexReader.Open(directory, true)) commit = new(reader.GetCommitUserData());
        // Native SenseNet Id fields are numeric indexed terms; delete by the exact document ID.
        using (var directory = FSDirectory.Open(new DirectoryInfo(path)))
        using (var reader = IndexReader.Open(directory, false))
        {
            foreach (var item in targets) reader.DeleteDocument(item.IndexDocumentId!.Value);
            reader.Commit();
        }
        using var writable = FSDirectory.Open(new DirectoryInfo(path));
        using var writer = new IndexWriter(writable, new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29), false, IndexWriter.MaxFieldLength.UNLIMITED);
        writer.AddDocument(Document("2147000000", "2147000000", scope.ToLowerInvariant() + "/orphaned-entry", "folder", "1000", "2000"));
        writer.AddDocument(Document(outsideScope.IndexNodeId!, outsideScope.IndexVersionId!, scope.ToLowerInvariant() + "/moved-content", outsideScope.NodeType!, outsideScope.IndexTimestamp!, outsideScope.IndexVersionTimestamp!));
        writer.Commit(commit);
    }

    private static Document Document(string node, string version, string path, string type, string nodeStamp, string versionStamp)
    {
        var document = new Document();
        foreach (var pair in new Dictionary<string, string> { ["Id"] = node, ["VersionId"] = version, ["Path"] = path,
            ["Type"] = type, ["NodeTimestamp"] = nodeStamp, ["VersionTimestamp"] = versionStamp,
            ["Version"] = "V1.0", ["IsLastPublic"] = "true", ["IsLastDraft"] = "true" })
            document.Add(new Field(pair.Key, pair.Value, Field.Store.YES, Field.Index.NOT_ANALYZED));
        return document;
    }
}

public sealed class LocalLabFactAttribute : FactAttribute
{
    public LocalLabFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("INDEXTOOLS_LOCAL_ACCEPTANCE") != "1")
            Skip = "Requires the explicitly initialized disposable local SQL/repository/tools lab.";
    }
}
