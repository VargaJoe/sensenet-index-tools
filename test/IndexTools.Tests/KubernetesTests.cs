using System.Text.Json;
using SenseNetIndexTools;
using Xunit;

namespace IndexTools.Tests;

public class KubernetesTests
{
    [Fact]
    public async Task UsesDeploymentSelectorReadyPodSameContainerAndChildProcessWorkingDirectory()
    {
        using var fixture = new IndexFixture();
        var before = Environment.CurrentDirectory;
        var calls = new List<(IReadOnlyList<string> Args, string? Cwd)>();
        var copier = CreateCopier(fixture, calls);
        var result = await copier.CopyAsync(new() { Deployment = "repository", Namespace = "testing", OutputDirectory = Path.Combine(fixture.Root, "copies") });
        Assert.Equal(before, Environment.CurrentDirectory);
        Assert.True(System.IO.Directory.Exists(result));
        Assert.Equal(2, ContentComparer.ReadIndexItems(result, "/Root/Review").Count);
        Assert.Contains(calls, call => call.Args.Contains("component=repo"));
        Assert.DoesNotContain(calls, call => call.Args.Contains("app=repository"));
        var copy = Assert.Single(calls, call => call.Args.Contains("cp"));
        Assert.NotNull(copy.Cwd);
        Assert.Contains("sensenet", copy.Args);
        Assert.Contains("ready-pod:/app/App_Data/LocalIndex/20261008110000/.", copy.Args);
        Assert.DoesNotContain(calls.SelectMany(c => c.Args), a => a.Contains("--kubeconfig"));
    }

    [Fact]
    public async Task FailedCopyIsNotReturnedAsUsableIndex()
    {
        using var fixture = new IndexFixture();
        var calls = new List<(IReadOnlyList<string>, string?)>();
        var copier = CreateCopier(fixture, calls, failCopy: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => copier.CopyAsync(new() { Deployment = "repo", OutputDirectory = Path.Combine(fixture.Root, "copies") }));
        Assert.All(System.IO.Directory.GetDirectories(Path.Combine(fixture.Root, "copies")), d => Assert.EndsWith(".partial", d));
    }

    [Fact]
    public async Task NoReadyPodDoesNotAttemptCopy()
    {
        using var fixture = new IndexFixture();
        var calls = new List<(IReadOnlyList<string>, string?)>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateCopier(fixture, calls, ready: false).CopyAsync(new() { Deployment = "repo" }));
        Assert.DoesNotContain(calls, c => c.Item1.Contains("cp"));
    }

    private static KubernetesIndexCopier CreateCopier(IndexFixture fixture,
        List<(IReadOnlyList<string>, string?)> calls, bool failCopy = false, bool ready = true) => new((args, cwd, token) =>
    {
        calls.Add((args, cwd));
        if (args.Contains("deployment")) return Result(JsonSerializer.Serialize(new {
            spec = new { selector = new { matchLabels = new Dictionary<string, string> { ["component"] = "repo" } } }
        }));
        if (args.Contains("pods")) return Result(JsonSerializer.Serialize(new {
            items = new[] { new {
                metadata = new { name = "ready-pod" },
                status = new { phase = "Running", conditions = new[] { new { type = "Ready", status = ready ? "True" : "False" } } },
                spec = new { containers = new[] { new { name = "sidecar" }, new { name = "sensenet" } } }
            } }
        }));
        if (args.Contains("ls")) return Result("20251001000000\n20261008110000\nwrite.lock\n");
        if (args.Contains("cp"))
        {
            if (failCopy) return Task.FromResult(new KubernetesCommandResult(1, "", "copy interrupted"));
            foreach (var file in System.IO.Directory.GetFiles(fixture.IndexPath))
                File.Copy(file, Path.Combine(cwd!, args[^1], Path.GetFileName(file)));
        }
        return Result("");
    });

    private static Task<KubernetesCommandResult> Result(string text) => Task.FromResult(new KubernetesCommandResult(0, text, ""));
}
