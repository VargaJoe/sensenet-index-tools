using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using System.CommandLine;
using SenseNetIndexTools;
using Xunit;

namespace IndexTools.Tests;

public class DockerTests
{
    [Fact]
    public void KubernetesCopyDoesNotConflictWithImplicitConfiguredSource()
    {
        var command = new System.CommandLine.Command("test");
        var path = new System.CommandLine.Option<string>("--path");
        path.SetDefaultValue("/source/index"); command.AddOption(path);
        _ = new IndexInputOptions(command, path);
        var result = command.Parse("--auto-copy-index --deployment example");
        Assert.Empty(result.Errors);
    }
    [Fact]
    public void SnapshotContainsOnlyCommittedFilesAndPreservesSource()
    {
        using var fixture = new IndexFixture();
        File.WriteAllText(Path.Combine(fixture.IndexPath, "uncommitted.tmp"), "not a commit file");
        var original = System.IO.Directory.GetFiles(fixture.IndexPath).ToDictionary(p => Path.GetFileName(p)!, File.ReadAllBytes);
        var snapshot = IndexSnapshot.Create(fixture.IndexPath, Path.Combine(fixture.Root, "copies"));
        Assert.Equal(2, ContentComparer.ReadIndexItems(snapshot, "/Root/Review").Count);
        Assert.False(File.Exists(Path.Combine(snapshot, "uncommitted.tmp")));
        Assert.False(File.Exists(Path.Combine(snapshot, "write.lock")));
        Assert.True(File.Exists(Path.Combine(snapshot, "snapshot-manifest.json")));
        Assert.All(original, pair => Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(fixture.IndexPath, pair.Key!))));
    }

    [Fact]
    public async Task NativeActivityMetadataReadsWithoutWriterAndCopyWritesRemainIsolated()
    {
        using var fixture = new IndexFixture();
        using (var directory = FSDirectory.Open(new DirectoryInfo(fixture.IndexPath)))
        using (var writer = new IndexWriter(directory, new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29), false, IndexWriter.MaxFieldLength.UNLIMITED))
        {
            var document = new Document();
            document.Add(new Field("$#COMMIT", "$#COMMIT", Field.Store.YES, Field.Index.NOT_ANALYZED));
            document.Add(new Field("$#DATA", Guid.NewGuid().ToString(), Field.Store.YES, Field.Index.NOT_ANALYZED));
            writer.AddDocument(document);
            writer.Commit(new Dictionary<string, string> { ["LastActivityId"] = "42", ["Gaps"] = "38,39" });
            Assert.Equal(42, ActivityStatusReader.Read(fixture.IndexPath).LastActivityId);
            Assert.Equal(new[] { 38, 39 }, ActivityStatusReader.Read(fixture.IndexPath).Gaps);
            var snapshot = IndexSnapshot.Create(fixture.IndexPath, Path.Combine(fixture.Root, "copies"));
            var service = new WebApp.Services.LastActivityIdService(Microsoft.Extensions.Logging.Abstractions.NullLogger<WebApp.Services.LastActivityIdService>.Instance);
            await service.SetLastActivityIdAsync(snapshot, 43, backup: false);
            Assert.Equal(43, ActivityStatusReader.Read(snapshot).LastActivityId);
            Assert.Equal(42, ActivityStatusReader.Read(fixture.IndexPath).LastActivityId);
            var export = Environment.GetEnvironmentVariable("INDEXTOOLS_SMOKE_EXPORT");
            if (!string.IsNullOrWhiteSpace(export))
            {
                var copy = IndexSnapshot.Create(fixture.IndexPath, export);
                System.IO.Directory.Move(copy, Path.Combine(export, "source"));
            }
        }
    }

    [Fact]
    public void DatedParentResolvesNewestCompleteDirectory()
    {
        using var fixture = new IndexFixture();
        var parent = Path.Combine(fixture.Root, "dated"); System.IO.Directory.CreateDirectory(parent);
        var old = Path.Combine(parent, "20260101000000"); System.IO.Directory.Move(fixture.IndexPath, old);
        System.IO.Directory.CreateDirectory(Path.Combine(parent, "20261009000000"));
        Assert.Equal(old, IndexSnapshot.Resolve(parent));
    }

    [Fact]
    public void SnapshotRefusesOutputInsideSource()
    {
        using var fixture = new IndexFixture();
        Assert.Throws<ArgumentException>(() => IndexSnapshot.Create(fixture.IndexPath, Path.Combine(fixture.IndexPath, "copy")));
    }

    [Fact]
    public void BearerAuthenticationIsAHeaderAndSerializedSettingsContainNoSecretValues()
    {
        using var request = RebuildIndexRequest.Create("https://repository.example.invalid", "", null, "10", false, "IndexOnly", "synthetic-token");
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.DoesNotContain("synthetic-token", request.RequestUri!.ToString());
        using var fixture = new IndexFixture();
        var secret = Path.Combine(fixture.Root, "secret"); File.WriteAllText(secret, "synthetic-secret");
        var settings = new RuntimeSettings { ApiKeyFile = secret };
        Assert.Equal("synthetic-secret", settings.ApiKey);
        Assert.DoesNotContain("synthetic-secret", System.Text.Json.JsonSerializer.Serialize(settings));
    }

    [Fact]
    public void SecretRedactionCoversSqlPasswordAndAuthenticationValues()
    {
        using var fixture = new IndexFixture();
        var sqlFile = Path.Combine(fixture.Root, "sql");
        var keyFile = Path.Combine(fixture.Root, "api");
        const string connection = "Server=sql.example.invalid;User ID=synthetic-user;Password=synthetic-password;";
        File.WriteAllText(sqlFile, connection); File.WriteAllText(keyFile, "synthetic-key");
        var redacted = SecretRedactor.Redact(connection + " synthetic-user synthetic-password synthetic-key synthetic-web-token", new RuntimeSettings { SqlConnectionStringFile = sqlFile, ApiKeyFile = keyFile }, "synthetic-web-token");
        foreach (var value in new[] { connection, "synthetic-user", "synthetic-password", "synthetic-key", "synthetic-web-token" }) Assert.DoesNotContain(value, redacted);
        File.WriteAllText(sqlFile, "Server=sql.example.invalid;User ID=sa;Password=synthetic-password;");
        Assert.Equal("Report saved; login '[redacted]'", SecretRedactor.Redact("Report saved; login 'sa'", new RuntimeSettings { SqlConnectionStringFile = sqlFile }));
    }
}
