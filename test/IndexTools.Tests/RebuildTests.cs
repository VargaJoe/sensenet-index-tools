using System.Net;
using System.CommandLine;
using Microsoft.Extensions.Logging.Abstractions;
using SenseNetIndexTools;
using WebApp.Services;
using Xunit;

namespace IndexTools.Tests;

public class RebuildTests
{
    [Theory]
    [InlineData("/Root/Content/My File", "https://example.invalid/odata.svc/Root/Content('My%20File')/RebuildIndex")]
    [InlineData("/Root", "https://example.invalid/odata.svc/('Root')/RebuildIndex")]
    [InlineData("/Root/Content/O'Brien", "https://example.invalid/odata.svc/Root/Content('O%27%27Brien')/RebuildIndex")]
    public void PathsUseODataEntitySyntax(string path, string expected) =>
        Assert.Equal(expected, RebuildIndexRequest.GetActionUrl("https://example.invalid/", path, null));

    [Fact]
    public void KeyIsInHeaderAndNeverInUrl()
    {
        using var request = RebuildIndexRequest.Create("https://example.invalid", "synthetic-key", null, "123", true, "IndexOnly");
        Assert.Equal("https://example.invalid/odata.svc/Content(123)/RebuildIndex", request.RequestUri!.AbsoluteUri);
        Assert.Equal("synthetic-key", Assert.Single(request.Headers.GetValues("apikey")));
        Assert.Empty(request.RequestUri.Query);
    }

    [Fact]
    public void SingleIdDoesNotRequireDefaultInputFile()
    {
        Assert.Empty(RebuildIndexCommand.Create().Parse("--repo-url https://example.invalid --api-key synthetic-key --content-id 123").Errors);
        Assert.NotEmpty(RebuildIndexCommand.Create().Parse("--repo-url https://example.invalid --api-key synthetic-key --content-id 123 --file-path targets.txt").Errors);
    }

    [Fact]
    public async Task WebSingleTargetSendsRequestAndReportsFailure()
    {
        using var handler = new CaptureHandler(HttpStatusCode.BadRequest);
        var service = new RebuildIndexService(new HttpClient(handler), NullLogger<RebuildIndexService>.Instance);
        var result = await service.RebuildIndexAsync(new() { RepoUrl = "https://example.invalid", ApiKey = "synthetic-key", ContentId = "123" });
        Assert.False(result.Success);
        Assert.Equal(1, result.FailedItems);
        Assert.Equal("synthetic-key", handler.ApiKey);
        Assert.DoesNotContain("synthetic-key", handler.Url);
    }

    [Fact]
    public async Task EmptyBatchFailsInsteadOfReportingSuccess()
    {
        using var fixture = new IndexFixture();
        var file = Path.Combine(fixture.Root, "targets.txt");
        await File.WriteAllTextAsync(file, "# comments only\n");
        using var handler = new CaptureHandler(HttpStatusCode.NoContent);
        var service = new RebuildIndexService(new HttpClient(handler), NullLogger<RebuildIndexService>.Instance);
        Assert.False((await service.RebuildIndexAsync(new() { FilePath = file })).Success);
        Assert.Null(handler.Url);
    }

    private sealed class CaptureHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? ApiKey { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            ApiKey = request.Headers.GetValues("apikey").Single();
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
