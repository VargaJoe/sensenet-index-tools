using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IndexTools.Tests;

[CollectionDefinition("Runtime defaults", DisableParallelization = true)]
public class RuntimeDefaultsCollection { }

[Collection("Runtime defaults")]
public class WebRuntimeDefaultsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitialLastActivityButtonsReflectConfiguredSource(bool configured)
    {
        using var fixture = new IndexFixture();
        var tokenFile = Path.Combine(fixture.Root, "web-token");
        const string token = "synthetic-token-for-initial-render-regression";
        File.WriteAllText(tokenFile, token);
        var sqlFile = Path.Combine(fixture.Root, "sql");
        File.WriteAllText(sqlFile, "Server=sql.example.invalid;Password=synthetic-password;");
        var configFile = Path.Combine(fixture.Root, "runtime.json");
        File.WriteAllText(configFile, "{}");
        var variables = new Dictionary<string, string?> {
            ["INDEXTOOLS_CONFIG_FILE"] = configFile,
            ["INDEXTOOLS_SOURCE_PATH"] = configured ? fixture.IndexPath : null,
            ["INDEXTOOLS_SQL_CONNECTION_STRING_FILE"] = sqlFile
        };
        var previous = variables.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var pair in variables) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            await using var factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder => {
                builder.UseContentRoot(fixture.Root);
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["AllowRemoteAccess"] = "true", ["WebAccess:TokenFile"] = tokenFile
                }));
            });
            using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["token"] = token }));
            var document = new HtmlParser().ParseDocument(await client.GetStringAsync("/lastactivityid"));
            var buttons = document.QuerySelectorAll("button");
            Assert.Equal(!configured, buttons.Single(b => b.TextContent.Contains("Get Index LastActivityId")).HasAttribute("disabled"));
            Assert.Equal(!configured, buttons.Single(b => b.TextContent.Contains("Compare Index vs Database")).HasAttribute("disabled"));
            Assert.NotNull(document.QuerySelector("#active-repository"));
            Assert.NotNull(document.QuerySelector("#operator-source"));
            // Secret editors are opened deliberately from the common context panel.
            Assert.Null(document.QuerySelector("input[placeholder^='Enter SQL']"));
            Assert.Contains(buttons, button => button.TextContent.Contains("Connection settings"));
        }
        finally
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
