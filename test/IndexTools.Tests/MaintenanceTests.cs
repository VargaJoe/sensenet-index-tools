using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging.Abstractions;
using SenseNetIndexTools;
using WebApp.Models;
using WebApp.Services;
using Xunit;

namespace IndexTools.Tests;

public class MaintenanceTests
{
    [Fact]
    public void CleanupRemovesOnlyOrphanedVersion()
    {
        using var fixture = new IndexFixture();
        var index = ContentComparer.ReadIndexItems(fixture.IndexPath, "/Root/Review");
        var db = new ContentItem { NodeId = 10, VersionId = 100, NodeType = "File", Path = index[0].Path };
        var orphan = Assert.Single(ContentComparer.CompareItems([db], index), i => !i.InDatabase);
        Assert.Equal(1, OrphanedIndexCleaner.Remove(fixture.IndexPath, [orphan]));
        Assert.Equal("100", Assert.Single(ContentComparer.ReadIndexItems(fixture.IndexPath, "/Root/Review")).IndexVersionId);
    }

    [Fact]
    public void DifferentTypeForExistingDatabaseVersionIsNotAnOrphan()
    {
        using var fixture = new IndexFixture();
        var index = ContentComparer.ReadIndexItems(fixture.IndexPath, "/Root/Review");
        var db = new ContentItem { NodeId = 10, VersionId = 100, NodeType = "DifferentType", Path = index[0].Path };
        var candidates = OrphanedIndexCleaner.FindCandidates(ContentComparer.CompareItems([db], index));
        Assert.Equal("101", Assert.Single(candidates).IndexVersionId);
    }

    [Fact]
    public void CleanupRefusesStaleDocumentIdentifiersWithoutDeletingAnything()
    {
        using var fixture = new IndexFixture();
        var index = ContentComparer.ReadIndexItems(fixture.IndexPath, "/Root/Review");
        index[1].IndexVersionId = "999";
        Assert.Throws<InvalidOperationException>(() => OrphanedIndexCleaner.Remove(fixture.IndexPath, index));
        Assert.Equal(2, ContentComparer.ReadIndexItems(fixture.IndexPath, "/Root/Review").Count);
    }

    [Fact]
    public async Task LastActivityInitializationDoesNotOverwriteExistingValue()
    {
        using var fixture = new IndexFixture();
        var service = new LastActivityIdService(NullLogger<LastActivityIdService>.Instance);
        await service.InitializeLastActivityIdAsync(fixture.IndexPath, 42, backup: false);
        Assert.Equal(42, (await service.GetLastActivityIdAsync(fixture.IndexPath)).LastActivityId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InitializeLastActivityIdAsync(fixture.IndexPath, 99, backup: false));
        await service.SetLastActivityIdAsync(fixture.IndexPath, 43, backup: false);
        Assert.Equal(43, (await service.GetLastActivityIdAsync(fixture.IndexPath)).LastActivityId);
        Assert.Equal(2, ContentComparer.ReadIndexItems(fixture.IndexPath, "/Root/Review").Count);
    }

    [Fact]
    public async Task WritesDoNotClearAnActiveWriterLock()
    {
        using var fixture = new IndexFixture();
        using var directory = FSDirectory.Open(new DirectoryInfo(fixture.IndexPath));
        using var writer = new IndexWriter(directory, new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29), false, IndexWriter.MaxFieldLength.UNLIMITED);
        var service = new LastActivityIdService(NullLogger<LastActivityIdService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetLastActivityIdAsync(fixture.IndexPath, 10, backup: false));
        Assert.True(IndexWriter.IsLocked(directory));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(2147483648L)]
    public async Task ActivityIdMustFitSenseNetInt32(long value)
    {
        using var fixture = new IndexFixture();
        var service = new LastActivityIdService(NullLogger<LastActivityIdService>.Instance);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetLastActivityIdAsync(fixture.IndexPath, value, backup: false));
    }

    [Fact]
    public async Task MultipleConfigurationInstancesDoNotLoseUpdates()
    {
        using var fixture = new IndexFixture();
        var env = new TestEnvironment(fixture.Root);
        var first = new ConfigurationService(NullLogger<ConfigurationService>.Instance, env);
        var second = new ConfigurationService(NullLogger<ConfigurationService>.Instance, env);
        var results = await Task.WhenAll(first.CreateConfigurationAsync(new() { Name = "One" }), second.CreateConfigurationAsync(new() { Name = "Two" }));
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(2, (await first.GetAllConfigurationsAsync()).Count);
        Assert.Equal(2, (await second.GetAllConfigurationsAsync()).Count);
    }

    [Fact]
    public async Task ConcurrentReportWritesPreserveBothReports()
    {
        using var fixture = new IndexFixture();
        var env = new TestEnvironment(fixture.Root);
        var first = new ReportStorageService(NullLogger<ReportStorageService>.Instance, env);
        var second = new ReportStorageService(NullLogger<ReportStorageService>.Instance, env);
        await Task.WhenAll(first.SaveReportAsync(new StoredReport { Name = "One", Content = "test" }), second.SaveReportAsync(new StoredReport { Name = "Two", Content = "test" }));
        Assert.Equal(2, (await first.GetReportsAsync()).Count);
    }
}
