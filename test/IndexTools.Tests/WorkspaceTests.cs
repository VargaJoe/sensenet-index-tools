using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Store;
using SenseNetIndexTools;
using Xunit;
using Directory = System.IO.Directory;

namespace IndexTools.Tests;

public class WorkspaceTests
{
    private static RuntimeSettings Settings(IndexFixture fixture)
    {
        var secret = Path.Combine(fixture.Root, "api-key");
        File.WriteAllText(secret, "synthetic-private-key");
        return new() { SourcePath = fixture.IndexPath, BackupSourceRoot = Path.Combine(fixture.Root, "backups"),
            RepositoryBackupRoot = "/app/IndexBackups", RepositoryUrl = "https://repository.example.invalid", ApiKeyFile = secret,
            SnapshotDirectory = Path.Combine(fixture.Root, "copies"), DataDirectory = Path.Combine(fixture.Root, "state"),
            BackupPollMilliseconds = 1, BackupTimeoutSeconds = 3 };
    }

    private static void SeedActivity(string path)
    {
        using var directory = FSDirectory.Open(new DirectoryInfo(path));
        using var writer = new IndexWriter(directory, new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29), false, IndexWriter.MaxFieldLength.UNLIMITED);
        var commit = new Lucene.Net.Documents.Document();
        commit.Add(new Lucene.Net.Documents.Field("$#COMMIT", "$#COMMIT", Lucene.Net.Documents.Field.Store.YES, Lucene.Net.Documents.Field.Index.NOT_ANALYZED));
        writer.AddDocument(commit);
        writer.Commit(new Dictionary<string, string> { ["LastActivityId"] = "42", ["Gaps"] = "" });
    }

    [Fact]
    public async Task BackupWaitsForMatchingCompletionThenPreservesOriginalWhenCopyIsModified()
    {
        using var fixture = new IndexFixture(); SeedActivity(fixture.IndexPath);
        var settings = Settings(fixture);
        var calls = 0;
        using var http = new HttpClient(new Handler(async (request, token) => {
            calls++;
            Assert.DoesNotContain("synthetic-private-key", request.RequestUri!.ToString());
            Assert.Equal("synthetic-private-key", Assert.Single(request.Headers.GetValues("apikey")));
            if (request.Method == HttpMethod.Post)
            {
                Assert.EndsWith("/('Root')/BackupIndex", request.RequestUri.AbsoluteUri);
                var body = await request.Content!.ReadAsStringAsync(token);
                Assert.Equal("/app/IndexBackups/test-run", JsonDocument.Parse(body).RootElement.GetProperty("target").GetString());
                return Response(new { State = "Started", Current = (object?)null, History = (object?)null });
            }
            Assert.EndsWith("/('Root')/QueryIndexBackup", request.RequestUri.AbsoluteUri);
            if (calls == 1) return Response(new { State = "Finished", History = new[] { new { TargetPath = "/app/IndexBackups/old-run", FinishedAt = DateTime.UtcNow } } });
            if (calls == 3) return Response(new { State = "Executing", Current = new { TargetPath = "/app/IndexBackups/test-run", CopiedFiles = 1, CountOfFiles = 2 } });
            var copy = IndexSnapshot.Create(fixture.IndexPath, settings.BackupSourceRoot!);
            Directory.Move(copy, Path.Combine(settings.BackupSourceRoot!, "test-run"));
            return Response(new { State = "Finished", History = new[] { new { TargetPath = "/app/IndexBackups/test-run", StartedAt = DateTime.UtcNow.AddSeconds(-1), FinishedAt = DateTime.UtcNow, CopiedFiles = 2, CountOfFiles = 2, TotalBytes = 100, CopiedBytes = 100, Message = "Finished" } } });
        }));
        var workspace = new IndexWorkspace(settings, http);
        var backup = await workspace.BackupAsync("test-run");
        Assert.Equal(4, calls);
        Assert.Equal(42, backup.CapturedActivityId);
        Assert.False(backup.Writable);
        Assert.Throws<InvalidOperationException>(() => workspace.RequireWorkingCopy(backup));
        Assert.Throws<InvalidOperationException>(() => RuntimeSettings.EnsureWritableCopy(backup.Path, settings));
        var working = workspace.Copy(backup.Id);
        workspace.RequireWorkingCopy(working);
        using (var directory = FSDirectory.Open(new DirectoryInfo(working.Path)))
        using (var writer = new IndexWriter(directory, new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29), false, IndexWriter.MaxFieldLength.UNLIMITED))
        {
            writer.AddDocument(new Lucene.Net.Documents.Document());
            writer.Commit(new Dictionary<string, string> { ["LastActivityId"] = "43" });
        }
        Assert.Equal(43, ActivityStatusReader.Read(working.Path).LastActivityId);
        Assert.Equal(42, ActivityStatusReader.Read(backup.Path).LastActivityId);
        Assert.Equal(42, ActivityStatusReader.Read(fixture.IndexPath).LastActivityId);
        Assert.Equal(3, new IndexWorkspace(settings, http).Sources().Count);
    }

    [Theory]
    [InlineData("Faulted")]
    [InlineData("Canceled")]
    [InlineData("Finished")]
    public async Task UnrelatedOrFailedBackupDoesNotRegisterASource(string state)
    {
        using var fixture = new IndexFixture();
        var count = 0;
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Response(new { State = ++count == 1 ? "Initial" : state, History = new[] { new { TargetPath = "/some-other-backup", FinishedAt = DateTime.UtcNow } } }))));
        var workspace = new IndexWorkspace(Settings(fixture), http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.BackupAsync("requested"));
        Assert.Equal("live", Assert.Single(workspace.Sources()).Id);
    }

    [Fact]
    public async Task DeletedBackupTargetCannotReuseAnOldCompletedStatus()
    {
        using var fixture = new IndexFixture();
        var settings = Settings(fixture);
        using var http = new HttpClient(new Handler((request, _) => {
            Assert.Equal(HttpMethod.Get, request.Method); // Never start a new request with an old target identity.
            return Task.FromResult(Response(new { State = "Finished", History = new[] {
                new { TargetPath = "/app/IndexBackups/reused-run", StartedAt = DateTime.UtcNow.AddDays(-1),
                    FinishedAt = DateTime.UtcNow.AddDays(-1), Message = "Finished", CountOfFiles = 2,
                    CopiedFiles = 2, TotalBytes = 100, CopiedBytes = 100 }
            } }));
        }));
        Assert.False(Directory.Exists(Path.Combine(settings.BackupSourceRoot!, "reused-run")));
        var workspace = new IndexWorkspace(settings, http);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.BackupAsync("reused-run"));
        Assert.Contains("history", error.Message);
        Assert.Equal("live", Assert.Single(workspace.Sources()).Id);
    }

    [Fact]
    public async Task ActiveRepositoryBackupIsNotReplacedOrCanceled()
    {
        using var fixture = new IndexFixture();
        using var http = new HttpClient(new Handler((request, _) => { Assert.Equal(HttpMethod.Get, request.Method); return Task.FromResult(Response(new { State = "Executing" })); }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new IndexWorkspace(Settings(fixture), http).BackupAsync());
    }

    [Fact]
    public async Task ForbiddenBackupResponseDoesNotExposeResponseBodyOrSecrets()
    {
        using var fixture = new IndexFixture();
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("synthetic-private-key") })));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new IndexWorkspace(Settings(fixture), http).BackupAsync());
        Assert.Contains("403", error.Message); Assert.DoesNotContain("synthetic-private-key", error.Message);
    }

    [Fact]
    public async Task CompletedBackupCanBeRecoveredAfterInterruptedWaitingWithoutDuplicates()
    {
        using var fixture = new IndexFixture(); SeedActivity(fixture.IndexPath);
        var settings = Settings(fixture);
        var path = IndexSnapshot.Create(fixture.IndexPath, settings.BackupSourceRoot!);
        Directory.Move(path, Path.Combine(settings.BackupSourceRoot!, "interrupted-run"));
        using var http = new HttpClient(new Handler((request, _) => {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Response(new { State = "Finished", History = new[] {
                new { TargetPath = "/app/IndexBackups/interrupted-run", FinishedAt = DateTime.UtcNow, Message = "Finished", CountOfFiles = 2, CopiedFiles = 2, TotalBytes = 10, CopiedBytes = 10 },
                new { TargetPath = "/app/IndexBackups/../outside", FinishedAt = DateTime.UtcNow, Message = "Finished", CountOfFiles = 2, CopiedFiles = 2, TotalBytes = 10, CopiedBytes = 10 }
            } }));
        }));
        var workspace = new IndexWorkspace(settings, http);
        Assert.Equal(1, await workspace.RecoverCompletedBackupsAsync());
        Assert.Equal(0, await workspace.RecoverCompletedBackupsAsync());
        var source = Assert.Single(workspace.Sources(), s => s.Kind == IndexSourceKind.RepositoryBackup);
        Assert.Equal(42, source.CapturedActivityId);
        settings.RepositoryUrl = "https://another-repository.example.invalid";
        Assert.Equal("live", Assert.Single(workspace.Sources()).Id);
    }

    [Fact]
    public async Task OldReportsCannotRepairAnotherRepositoryOrReuseAChangedWorkingCopy()
    {
        using var fixture = new IndexFixture(); SeedActivity(fixture.IndexPath);
        var settings = Settings(fixture); using var http = new HttpClient();
        var workspace = new IndexWorkspace(settings, http);
        var working = workspace.Copy("live", true);
        var folder = Path.Combine(settings.DataDirectory!, "workspace", "comparisons"); Directory.CreateDirectory(folder);
        var report = new IndexComparison { SourceId = working.Id, RepositoryBinding = "another-repository" };
        File.WriteAllText(Path.Combine(folder, report.Id + ".json"), JsonSerializer.Serialize(report));
        var wrongRepo = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.RepairAsync(report.Id, [10], true));
        Assert.Contains("different repository", wrongRepo.Message);
        report.RepositoryBinding = working.RepositoryBinding;
        report.IndexVersion = IndexWorkspace.Version(working.Path) - 1;
        File.WriteAllText(Path.Combine(folder, report.Id + ".json"), JsonSerializer.Serialize(report));
        var changed = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CleanAsync(report.Id, true));
        Assert.Contains("changed", changed.Message);
    }

    [Fact]
    public async Task CancellationStopsStatusPollingWithoutSelectingAPartialBackup()
    {
        using var fixture = new IndexFixture();
        using var cancellation = new CancellationTokenSource(50);
        using var http = new HttpClient(new Handler(async (_, token) => { await Task.Delay(5000, token); return Response(new { State = "Initial" }); }));
        var workspace = new IndexWorkspace(Settings(fixture), http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.BackupAsync(cancellation: cancellation.Token));
        Assert.Single(workspace.Sources());
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("other\\folder")]
    public async Task BackupRejectsTargetTraversalBeforeAnyRequest(string name)
    {
        using var fixture = new IndexFixture();
        using var http = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("No request expected")));
        await Assert.ThrowsAsync<ArgumentException>(() => new IndexWorkspace(Settings(fixture), http).BackupAsync(name));
    }

    [Fact]
    public void OfflineCopyRefusesAnActiveWriterEvenWhenConfirmed()
    {
        using var fixture = new IndexFixture(); SeedActivity(fixture.IndexPath);
        using var http = new HttpClient(); var workspace = new IndexWorkspace(Settings(fixture), http);
        Assert.Throws<InvalidOperationException>(() => workspace.Copy("live"));
        using var directory = FSDirectory.Open(new DirectoryInfo(fixture.IndexPath));
        using var writer = new IndexWriter(directory, new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29), false, IndexWriter.MaxFieldLength.UNLIMITED);
        Assert.Throws<InvalidOperationException>(() => workspace.Copy("live", true));
        Assert.True(IndexWriter.IsLocked(directory));
    }

    [Fact]
    public void OfflineCopyReadsAnExistingUnlockedLockFileWithoutChangingIt()
    {
        using var fixture = new IndexFixture(); SeedActivity(fixture.IndexPath);
        File.WriteAllText(Path.Combine(fixture.IndexPath, "write.lock"), "stale-file-must-remain");
        using var http = new HttpClient(); var workspace = new IndexWorkspace(Settings(fixture), http);
        var copy = workspace.Copy("live", true);
        Assert.Equal(42, ActivityStatusReader.Read(copy.Path).LastActivityId);
        Assert.Equal("stale-file-must-remain", File.ReadAllText(Path.Combine(fixture.IndexPath, "write.lock")));
        Assert.False(File.Exists(Path.Combine(copy.Path, "write.lock")));
    }

    [Fact]
    public async Task MissingActivityMetadataCanBeInitializedOnACopyWithoutChangingTheSource()
    {
        using var fixture = new IndexFixture(); using var http = new HttpClient();
        var workspace = new IndexWorkspace(Settings(fixture), http);
        var copy = workspace.Copy("live", true);
        Assert.Null(copy.CapturedActivityId);
        workspace.RequireWorkingCopy(copy);
        var service = new WebApp.Services.LastActivityIdService(Microsoft.Extensions.Logging.Abstractions.NullLogger<WebApp.Services.LastActivityIdService>.Instance);
        await service.InitializeLastActivityIdAsync(copy.Path, 42, backup: false);
        Assert.Equal(42, ActivityStatusReader.Read(copy.Path).LastActivityId);
        Assert.Throws<InvalidOperationException>(() => ActivityStatusReader.Read(fixture.IndexPath));
    }

    [Fact]
    public async Task RepairAndCleanupRequireExplicitConfirmationAndManagedRecords()
    {
        using var fixture = new IndexFixture(); using var http = new HttpClient();
        var workspace = new IndexWorkspace(Settings(fixture), http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CleanAsync("missing", false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.RepairAsync("missing", [10], false));
        Assert.Throws<InvalidOperationException>(() => workspace.RequireWorkingCopy(new() { Kind = IndexSourceKind.WorkingCopy, Path = fixture.IndexPath }));
        Assert.Throws<ArgumentException>(() => workspace.GetComparison("../outside"));
    }

    private static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
}
