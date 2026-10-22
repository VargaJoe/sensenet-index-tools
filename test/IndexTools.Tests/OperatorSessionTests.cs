using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using SenseNetIndexTools;
using WebApp.Models;
using WebApp.Services;
using Xunit;

namespace IndexTools.Tests;

[Collection("Runtime defaults")]
public sealed class OperatorSessionTests
{
    [Fact]
    public async Task BrowserSelectionsAreIndependentAndReloadRestoresOnlyThatBrowser()
    {
        using var lab = new SessionLab();
        var browserA = new BrowserStorage(); var browserB = new BrowserStorage();
        var first = lab.Context(browserA); var second = lab.Context(browserB);
        await first.InitializeAsync(); await second.InitializeAsync();
        await first.ApplyAsync(new() { Name = "Repository A", IndexPath = lab.Fixture.IndexPath,
            ConnectionString = "Server=a.invalid;Database=A;Password=synthetic-private-value", ApiKey = "synthetic-api-A", RepositoryUrl = "https://a.invalid" });
        await second.ApplyAsync(new() { Name = "Repository B", IndexPath = lab.Fixture.IndexPath, ConnectionString = "Server=b.invalid;Database=B" });
        Assert.Equal("Repository A", first.Selection.Name);
        Assert.Equal("Repository B", second.Selection.Name);
        Assert.DoesNotContain("synthetic-private-value", browserA.Value!);
        Assert.DoesNotContain("synthetic-api-A", browserA.Value!);
        var reload = lab.Context(browserA); await reload.InitializeAsync();
        Assert.Equal(first.Selection.ConfigurationId, reload.Selection.ConfigurationId);
        Assert.Equal(first.Settings.ConnectionString, reload.Settings.ConnectionString);
        Assert.Equal("Server=b.invalid;Database=B", second.Settings.ConnectionString);
    }

    [Fact]
    public async Task ProfileReplacementClearsEmptyCredentialsAndOperationPinsItsTarget()
    {
        using var lab = new SessionLab(); var context = lab.Context(new()); await context.InitializeAsync();
        await context.ApplyAsync(new() { Name = "A", IndexPath = lab.Fixture.IndexPath, ConnectionString = "Server=a.invalid", ApiKey = "A", BearerToken = "B", RepositoryUrl = "https://a.invalid", RepositoryBackupRoot = "/a", BackupSourceRoot = "/b" });
        var revision = context.Revision;
        using (context.BeginOperation())
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.ApplyAsync(new() { Name = "B" }));
        Assert.Equal(revision, context.Revision);
        await context.ApplyAsync(new() { Name = "B", IndexPath = lab.Fixture.IndexPath });
        Assert.Empty(context.Settings.ConnectionString); Assert.Empty(context.Settings.ApiKey); Assert.Empty(context.Settings.BearerToken);
        Assert.Empty(context.Selection.RepositoryUrl); Assert.Empty(context.Selection.RepositoryBackupRoot); Assert.Empty(context.Selection.BackupSourceRoot);
        Assert.True(context.Revision > revision);
    }

    [Fact]
    public async Task SelectedCopySurvivesReloadAndMetadataEditsCannotTouchOriginalOrCallRebuild()
    {
        using var lab = new SessionLab(); var storage = new BrowserStorage(); var context = lab.Context(storage); await context.InitializeAsync();
        var originals = Directory.GetFiles(lab.Fixture.IndexPath).ToDictionary(path => Path.GetFileName(path)!, path => SHA256.HashData(File.ReadAllBytes(path)));
        var copy = context.Workspace.Copy("live", true); await context.SelectSourceAsync(copy.Id);
        var service = new LastActivityIdService(NullLogger<LastActivityIdService>.Instance, context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InitializeLastActivityIdAsync(lab.Fixture.IndexPath, 42));
        await service.InitializeLastActivityIdAsync(copy.Path, 12345);
        await service.SetLastActivityIdAsync(copy.Path, 54321);
        Assert.Equal(54321, ActivityStatusReader.Read(copy.Path).LastActivityId);
        Assert.Equal(2, Directory.GetDirectories(lab.Backups).Length);
        foreach (var original in originals) Assert.Equal(original.Value, SHA256.HashData(File.ReadAllBytes(Path.Combine(lab.Fixture.IndexPath, original.Key!))));
        Assert.Equal(0, lab.Http.Requests);
        var reloaded = lab.Context(storage); await reloaded.InitializeAsync();
        Assert.Equal(copy.Id, reloaded.Selection.SourceId); Assert.Equal(copy.Path, reloaded.IndexPath);
        Directory.Move(copy.Path, copy.Path + "-unavailable");
        Assert.Null(reloaded.Source); Assert.Equal("", reloaded.IndexPath);
    }

    [Fact]
    public async Task BothReportExportsPersistAndDeleteTogether()
    {
        using var lab = new SessionLab();
        var reports = new ReportStorageService(NullLogger<ReportStorageService>.Instance, new TestEnvironment(lab.Fixture.Root));
        var report = new StoredReport { Name = "Dual exports", Content = "<h1>Result</h1>", Format = "html", ReportType = "validation" };
        await reports.SaveReportAsync(report, new Dictionary<string, string> { ["html"] = report.Content, ["md"] = "# Result" });
        Assert.Equal("# Result", await reports.GetExportAsync(report.Id, "md"));
        Assert.Equal(report.Content, await reports.GetExportAsync(report.Id, "html"));
        var files = report.ExportFiles.Values.ToArray(); Assert.All(files, path => Assert.True(File.Exists(path)));
        Assert.True(await reports.DeleteReportAsync(report.Id)); Assert.All(files, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public async Task ReportsRespectRequestedOrderingAndMixedTimestampKinds()
    {
        using var lab = new SessionLab();
        var reports = new ReportStorageService(NullLogger<ReportStorageService>.Instance, new TestEnvironment(lab.Fixture.Root));
        var now = DateTime.UtcNow;
        await reports.SaveReportAsync(new() { Name = "A earlier local", CreatedAt = now.AddMinutes(-10).ToLocalTime(), Content = "earlier" });
        await reports.SaveReportAsync(new() { Name = "B latest UTC", CreatedAt = now, Content = "latest" });
        Assert.Equal("B latest UTC", (await reports.GetReportsAsync()).First().Name);
        Assert.Equal("A earlier local", (await reports.GetReportsAsync(new() { SortBy = "Name", SortDescending = false })).First().Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidationReportsRetainExplicitOrActiveRepository(bool explicitProfile)
    {
        using var lab = new SessionLab();
        var context = lab.Context(new()); await context.InitializeAsync();
        var profile = new SavedConfiguration { Name = "Selected repository", IndexPath = lab.Fixture.IndexPath };
        await context.ApplyAsync(profile);
        var storage = new ReportStorageService(NullLogger<ReportStorageService>.Instance, new TestEnvironment(lab.Fixture.Root));
        var service = new IndexValidationService(NullLogger<IndexValidationService>.Instance, storage, context);
        var options = new ValidationOptions { IndexPath = lab.Fixture.IndexPath, Format = "html" };
        var expectedId = explicitProfile ? Guid.NewGuid().ToString() : profile.Id.ToString();
        var expectedName = explicitProfile ? "Explicit repository" : profile.Name;
        var result = explicitProfile
            ? await service.ValidateIndexAsync(options, expectedId, expectedName)
            : await service.ValidateIndexAsync(options);
        var report = Assert.Single(await storage.GetReportsAsync());
        Assert.Equal(expectedId, report.ConfigurationId); Assert.Equal(expectedName, report.ConfigurationName);
        Assert.Equal(expectedId, result.ConfigurationId); Assert.Equal(expectedName, result.ConfigurationName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopeningComparisonRestoresItsRepositoryOnlyInCurrentBrowser(bool workingCopy)
    {
        using var lab = new SessionLab();
        var environment = new TestEnvironment(lab.Fixture.Root);
        var configurations = new ConfigurationService(NullLogger<ConfigurationService>.Instance, environment);
        var created = await configurations.CreateConfigurationAsync(new() { Name = "A", IndexPath = lab.Fixture.IndexPath,
            ConnectionString = "Server=a.invalid;Database=A", RepositoryUrl = "https://a.invalid" });
        Assert.True(created.Success);
        var context = lab.Context(new()); await context.InitializeAsync(); await context.ApplyAsync(created.Configuration!);
        var source = workingCopy ? context.Workspace.Copy("live", true) : context.Workspace.GetSource("live");
        var comparison = new IndexComparison { SourceId = source.Id, RepositoryBinding = source.RepositoryBinding, Scope = "/Root" };
        var folder = Path.Combine(RuntimeSettings.Load().DataDirectory!, "workspace", "comparisons");
        Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, comparison.Id + ".json"), JsonSerializer.Serialize(comparison));
        var storage = new ReportStorageService(NullLogger<ReportStorageService>.Instance, environment);
        await storage.SaveReportAsync(new() { Id = comparison.Id, Content = "Result", ConfigurationId = created.Configuration!.Id.ToString(), ConfigurationName = "A" });
        var profileB = new SavedConfiguration { Name = "B", IndexPath = lab.Fixture.IndexPath, ConnectionString = "Server=b.invalid;Database=B", RepositoryUrl = "https://b.invalid" };
        await context.ApplyAsync(profileB);
        var otherBrowser = lab.Context(new()); await otherBrowser.InitializeAsync(); await otherBrowser.ApplyAsync(profileB);
        var reports = new OperatorReportService(context, storage);
        var restored = await reports.OpenComparisonAsync(comparison.Id, configurations);
        Assert.Equal(comparison.Id, restored.Id); Assert.Equal(source.Id, context.Selection.SourceId);
        Assert.Equal("A", context.Selection.Name); Assert.Equal("B", otherBrowser.Selection.Name);
        Assert.Equal("live", otherBrowser.Selection.SourceId);
        await configurations.DeleteConfigurationAsync(created.Configuration.Id);
        await context.ApplyAsync(profileB);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.OpenComparisonAsync(comparison.Id, configurations));
        Assert.Equal("B", context.Selection.Name);
        await context.ApplyManualAsync(new() { IndexPath = lab.Fixture.IndexPath,
            ConnectionString = created.Configuration.ConnectionString!, RepositoryUrl = created.Configuration.RepositoryUrl! });
        await reports.OpenComparisonAsync(comparison.Id, configurations);
        Assert.Equal("Manual repository", context.Selection.Name);
        Assert.Equal(source.Id, context.Selection.SourceId);
    }

    private sealed class SessionLab : IDisposable
    {
        public IndexFixture Fixture { get; } = new();
        public CountingClient Http { get; } = new();
        private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
        private readonly Dictionary<string, string?> previous;
        public string Backups => Path.Combine(Fixture.Root, "safety");
        public SessionLab()
        {
            var keys = new[] { "INDEXTOOLS_CONFIG_FILE", "INDEXTOOLS_SOURCE_PATH", "INDEXTOOLS_DATA_DIRECTORY", "INDEXTOOLS_SNAPSHOT_DIRECTORY", "INDEXTOOLS_BACKUP_DIRECTORY", "INDEXTOOLS_SQL_CONNECTION_STRING_FILE", "INDEXTOOLS_REPOSITORY_URL", "INDEXTOOLS_API_KEY_FILE", "INDEXTOOLS_BEARER_TOKEN_FILE", "INDEXTOOLS_BACKUP_SOURCE_ROOT", "INDEXTOOLS_REPOSITORY_BACKUP_ROOT", "INDEXTOOLS_CONTAINER_RUNTIME" };
            previous = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
            foreach (var key in keys) Environment.SetEnvironmentVariable(key, null);
            var settings = new RuntimeSettings { SourcePath = Fixture.IndexPath, DataDirectory = Path.Combine(Fixture.Root, "data"), SnapshotDirectory = Path.Combine(Fixture.Root, "copies"), BackupDirectory = Backups, OutputDirectory = Path.Combine(Fixture.Root, "reports") };
            var file = Path.Combine(Fixture.Root, "runtime.json"); File.WriteAllText(file, JsonSerializer.Serialize(settings));
            Environment.SetEnvironmentVariable("INDEXTOOLS_CONFIG_FILE", file);
        }
        public ActiveRepositoryContext Context(BrowserStorage storage) => new(protection, Http, storage);
        public void Dispose() { foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value); Fixture.Dispose(); }
    }
    private sealed class CountingClient : HttpMessageHandler, IHttpClientFactory
    {
        public int Requests { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Requests++; throw new InvalidOperationException("This metadata test must not call the repository API."); }
    }
    private sealed class BrowserStorage : IJSRuntime
    {
        public string? Value { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "indexToolsSession.write") Value = (string?)args![0];
            if (identifier == "indexToolsSession.clear") Value = null;
            return ValueTask.FromResult(identifier == "indexToolsSession.read" ? (TValue)(object?)Value! : default!);
        }
    }
}
