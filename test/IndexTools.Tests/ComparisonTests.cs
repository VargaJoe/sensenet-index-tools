using SenseNetIndexTools;
using Xunit;

namespace IndexTools.Tests;

public class ComparisonTests
{
    private static ContentItem Db(int version = 100) => new() {
        NodeId = 10, VersionId = version, Path = "/Root/Review/Item", NodeType = "File",
        TimestampNumeric = 1000, VersionTimestampNumeric = 2000
    };
    private static ContentItem Index(int version = 100) => new() {
        IndexNodeId = "10", IndexVersionId = version.ToString(), Path = "/root/review/item", NodeType = "file",
        IndexTimestamp = "1000", IndexVersionTimestamp = "2000"
    };

    [Theory]
    [InlineData("1000", "2000", "Match")]
    [InlineData("1000", "9999", "Timestamp mismatch")]
    [InlineData("9999", "2000", "Timestamp mismatch")]
    [InlineData(null, "2000", "Timestamp unavailable")]
    [InlineData("1000", null, "Timestamp unavailable")]
    [InlineData("not-a-number", "2000", "Timestamp unavailable")]
    public void BothTimestampsMustBeAvailableAndEqual(string? node, string? version, string status)
    {
        var index = Index(); index.IndexTimestamp = node; index.IndexVersionTimestamp = version;
        Assert.Equal(status, Assert.Single(ContentComparer.CompareItems([Db()], [index])).Status);
    }

    [Fact]
    public void PublishedDraftAndExtraIndexDuplicatesArePreserved()
    {
        var result = ContentComparer.CompareItems([Db(100), Db(101)], [Index(100), Index(101), Index(101)]);
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.Count(i => i.Status == "Match"));
        Assert.Single(result, i => i.Status == "Index only");
    }

    [Fact]
    public void DifferentIdsOnSamePathRemainSeparate()
    {
        var index = Index(); index.IndexNodeId = "99";
        var result = ContentComparer.CompareItems([Db()], [index]);
        Assert.Single(result, i => i.Status == "DB only");
        Assert.Single(result, i => i.Status == "Index only");
    }

    [Fact]
    public void PathCaseSeparatorsAndTrailingSlashAreNormalized()
    {
        var db = Db(); db.Path = "\\Root\\Review\\Item\\";
        Assert.Equal("Match", Assert.Single(ContentComparer.CompareItems([db], [Index()])).Status);
    }

    [Fact]
    public void MoreThanTenThousandDocumentsAreReadWithoutTruncation()
    {
        using var fixture = new IndexFixture(10001);
        Assert.Equal(10001, ContentComparer.ReadIndexItems(fixture.IndexPath, "/Root/Review").Count);
    }

    [Fact]
    public void SubtreeReportPreservesVersionCounts()
    {
        var items = ContentComparer.CompareItems([Db(100), Db(101)], [Index(100), Index(101)]);
        var report = new SubtreeIndexChecker().GenerateSubtreeReportFromItems(items, "/Root/Review", format: "md");
        Assert.Contains("- Matched Items: 2", report);
        Assert.All(items, i => Assert.Equal("Match", i.Status));
    }
}
