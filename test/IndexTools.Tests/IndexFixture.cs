using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace IndexTools.Tests;

internal sealed class IndexFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "IndexToolsTest-" + Guid.NewGuid().ToString("N"));
    public string IndexPath => Path.Combine(Root, "index");
    public IndexFixture(int documents = 2)
    {
        System.IO.Directory.CreateDirectory(Root);
        using var directory = FSDirectory.Open(new DirectoryInfo(IndexPath));
        using var analyzer = new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29);
        using var writer = new IndexWriter(directory, analyzer, true, IndexWriter.MaxFieldLength.UNLIMITED);
        for (var i = 0; i < documents; i++)
        {
            var doc = new Document();
            foreach (var pair in new Dictionary<string, string> {
                ["Id"] = "10", ["VersionId"] = (i + 100).ToString(), ["Path"] = "/root/review/item",
                ["Type"] = "file", ["NodeTimestamp"] = "1000", ["VersionTimestamp"] = "2000",
                ["Version"] = "V1.0", ["IsLastPublic"] = "true", ["IsLastDraft"] = "true" })
                doc.Add(new Field(pair.Key, pair.Value, Field.Store.YES, Field.Index.NOT_ANALYZED));
            writer.AddDocument(doc);
        }
        writer.Commit();
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(Root);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("IndexToolsTest-"))
            throw new InvalidOperationException("Test cleanup path is outside the temporary workspace.");
        System.IO.Directory.Delete(resolved, recursive: true);
    }
}

internal sealed class TestEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "WebApp";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = root;
    public string WebRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
