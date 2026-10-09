using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebApp.Models;
using WebApp.Services;
using Xunit;

namespace IndexTools.Tests;

public class WebTests
{
    [Fact]
    public async Task WebStartsAndOperationPagesRender()
    {
        using var fixture = new IndexFixture();
        var tokenFile = Path.Combine(fixture.Root, "web-token");
        const string token = "synthetic-web-token-for-regression-tests";
        File.WriteAllText(tokenFile, token);
        await using var factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(fixture.Root);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["AllowRemoteAccess"] = "true", ["WebAccess:TokenFile"] = tokenFile
            }));
        });
        using var scope = factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RebuildIndexService>());
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
        foreach (var path in new[] { "/", "/validation", "/subtree-check", "/lastactivityid", "/rebuild-index", "/reports", "/settings" })
            Assert.True((await client.GetAsync(path)).IsSuccessStatusCode, path);
    }

    [Fact]
    public async Task RemoteModeRequiresTokenAndRejectsUnauthenticatedPages()
    {
        using var fixture = new IndexFixture();
        var tokenFile = Path.Combine(fixture.Root, "web-token");
        File.WriteAllText(tokenFile, "synthetic-web-token-for-regression-tests");
        await using var factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder => {
            builder.UseContentRoot(fixture.Root);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["AllowRemoteAccess"] = "true", ["WebAccess:TokenFile"] = tokenFile, ["WebAccess:HttpsRedirect"] = "false"
            }));
        });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
        Assert.Equal("/login", (await client.GetAsync("/settings")).Headers.Location!.ToString());
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["token"] = "wrong" }))).StatusCode);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["token"] = "synthetic-web-token-for-regression-tests" }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, login.StatusCode);
        Assert.DoesNotContain("secure", login.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant());
    }

    [Fact]
    public async Task RemoteRequestsAreDeniedByDefault()
    {
        using var fixture = new IndexFixture();
        await using var factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder => builder.UseContentRoot(fixture.Root));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.1");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task ConnectionStringIsEncryptedOnDiskAndRoundTrips()
    {
        using var fixture = new IndexFixture();
        var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(fixture.Root, "keys")));
        var env = new TestEnvironment(fixture.Root);
        var service = new ConfigurationService(NullLogger<ConfigurationService>.Instance, env, provider);
        const string connection = "Server=example.invalid;Password=synthetic-secret;";
        Assert.True((await service.CreateConfigurationAsync(new() { Name = "Test", ConnectionString = connection })).Success);
        var persisted = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "Data", "configurations.json"));
        Assert.DoesNotContain("synthetic-secret", persisted);
        Assert.Contains("protected:v1:", persisted);
        var reopened = new ConfigurationService(NullLogger<ConfigurationService>.Instance, env, provider);
        Assert.Equal(connection, Assert.Single(await reopened.GetAllConfigurationsAsync()).ConnectionString);
    }

    [Fact]
    public async Task LegacyPlaintextConfigurationLoadsAndIsProtectedOnSave()
    {
        using var fixture = new IndexFixture();
        var data = Path.Combine(fixture.Root, "Data"); System.IO.Directory.CreateDirectory(data);
        var file = Path.Combine(data, "configurations.json");
        await File.WriteAllTextAsync(file, "[{\"Name\":\"Legacy\",\"ConnectionString\":\"synthetic-secret\"}]");
        var service = new ConfigurationService(NullLogger<ConfigurationService>.Instance, new TestEnvironment(fixture.Root));
        Assert.Equal("synthetic-secret", Assert.Single(await service.GetAllConfigurationsAsync()).ConnectionString);
        Assert.True((await service.CreateConfigurationAsync(new() { Name = "New" })).Success);
        Assert.DoesNotContain("synthetic-secret", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task CorruptIndexIsNotReportedAsSuccessfulValidation()
    {
        using var fixture = new IndexFixture();
        var bad = Path.Combine(fixture.Root, "bad"); System.IO.Directory.CreateDirectory(bad);
        await File.WriteAllTextAsync(Path.Combine(bad, "segments_1"), "corrupt");
        var reports = new ReportStorageService(NullLogger<ReportStorageService>.Instance, new TestEnvironment(fixture.Root));
        var service = new IndexValidationService(NullLogger<IndexValidationService>.Instance, reports);
        var result = await service.ValidateIndexAsync(new() { IndexPath = bad, CreateBackup = false, Detailed = true });
        Assert.False(result.Success);
    }
}
