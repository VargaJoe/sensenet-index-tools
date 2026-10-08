using System.Diagnostics;
using System.Text.Json;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Directory = System.IO.Directory;

namespace SenseNetIndexTools;

public sealed record KubernetesCommandResult(int ExitCode, string Output, string Error);

public sealed class KubernetesIndexCopyOptions
{
    public string? Kubeconfig { get; set; }
    public string Namespace { get; set; } = "default";
    public string Deployment { get; set; } = "";
    public string? Container { get; set; }
    public string IndexPathInPod { get; set; } = "/app/App_Data/LocalIndex";
    public string OutputDirectory { get; set; } = Path.Combine(Environment.CurrentDirectory, "IndexBackups");
}

public sealed class KubernetesIndexCopier
{
    private readonly Func<IReadOnlyList<string>, string?, CancellationToken, Task<KubernetesCommandResult>> _run;

    public KubernetesIndexCopier() : this(RunKubectlAsync) { }
    public KubernetesIndexCopier(Func<IReadOnlyList<string>, string?, CancellationToken, Task<KubernetesCommandResult>> run) => _run = run;

    public async Task<string> CopyAsync(KubernetesIndexCopyOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Deployment) || string.IsNullOrWhiteSpace(options.Namespace))
            throw new ArgumentException("Deployment and namespace are required.");
        if (string.IsNullOrWhiteSpace(options.IndexPathInPod) || !options.IndexPathInPod.StartsWith('/'))
            throw new ArgumentException("Index path in pod must be an absolute path.");
        var common = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Kubeconfig)) common.AddRange(["--kubeconfig", options.Kubeconfig]);
        common.AddRange(["--namespace", options.Namespace]);

        async Task<string> Run(string[] args, string? cwd = null)
        {
            var result = await _run(common.Concat(args).ToArray(), cwd, cancellationToken);
            if (result.ExitCode != 0) throw new InvalidOperationException($"kubectl {args[0]} failed ({result.ExitCode}): {result.Error}");
            return result.Output;
        }

        using var deployment = JsonDocument.Parse(await Run(["get", "deployment", options.Deployment, "-o", "json"]));
        var selector = GetSelector(deployment.RootElement.GetProperty("spec").GetProperty("selector"));
        using var pods = JsonDocument.Parse(await Run(["get", "pods", "-l", selector, "-o", "json"]));
        var readyPods = pods.RootElement.GetProperty("items").EnumerateArray()
            .Where(p => p.GetProperty("status").GetProperty("phase").GetString() == "Running" &&
                !p.GetProperty("metadata").TryGetProperty("deletionTimestamp", out _) &&
                p.GetProperty("status").TryGetProperty("conditions", out var conditions) &&
                conditions.EnumerateArray().Any(c => c.GetProperty("type").GetString() == "Ready" && c.GetProperty("status").GetString() == "True"))
            .OrderBy(p => p.GetProperty("metadata").GetProperty("name").GetString(), StringComparer.Ordinal).ToArray();
        if (readyPods.Length == 0) throw new InvalidOperationException("No running, ready pod found for deployment.");
        var pod = readyPods[0];
        var podName = pod.GetProperty("metadata").GetProperty("name").GetString()!;
        var containers = pod.GetProperty("spec").GetProperty("containers").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()!).ToArray();
        var container = options.Container ?? (containers.Contains("sensenet") ? "sensenet" : containers.Length == 1 ? containers[0] : null);
        if (container == null || !containers.Contains(container))
            throw new InvalidOperationException("Specify --container for a pod with multiple containers, using an existing container name.");

        var folders = (await Run(["exec", podName, "-c", container, "--", "ls", "-1", options.IndexPathInPod]))
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim()).Where(f => f.Length is 14 or 15 && f.StartsWith("20") && f.All(char.IsAsciiDigit))
            .OrderDescending(StringComparer.Ordinal).ToArray();
        if (folders.Length == 0) throw new InvalidOperationException("No dated index directory found in pod.");
        var sourcePath = options.IndexPathInPod.TrimEnd('/') + "/" + folders[0];
        await Run(["exec", podName, "-c", container, "--", "test", "-d", sourcePath]);

        var output = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(output);
        var name = $"auto-copy-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var partialName = name + ".partial";
        var partial = Path.Combine(output, partialName);
        Directory.CreateDirectory(partial);
        Console.WriteLine($"Copying index from {options.Namespace}/{podName}, container {container}, directory {sourcePath}");
        await Run(["cp", "-c", container, $"{podName}:{sourcePath}/.", partialName], output);

        // A successful cp exit code is insufficient: ensure the local copy can actually be read.
        using (var directory = FSDirectory.Open(new DirectoryInfo(partial)))
        using (var reader = IndexReader.Open(directory, true)) { _ = reader.NumDocs(); }
        var destination = Path.Combine(output, name);
        Directory.Move(partial, destination);
        Console.WriteLine($"Local index copy: {destination}");
        return destination;
    }

    private static string GetSelector(JsonElement selector)
    {
        var parts = new List<string>();
        if (selector.TryGetProperty("matchLabels", out var labels))
            parts.AddRange(labels.EnumerateObject().OrderBy(p => p.Name).Select(p => $"{p.Name}={p.Value.GetString()}"));
        if (selector.TryGetProperty("matchExpressions", out var expressions))
            foreach (var expression in expressions.EnumerateArray())
            {
                var key = expression.GetProperty("key").GetString();
                var op = expression.GetProperty("operator").GetString();
                var values = expression.TryGetProperty("values", out var v) ? string.Join(",", v.EnumerateArray().Select(x => x.GetString())) : "";
                parts.Add(op switch {
                    "In" => $"{key} in ({values})", "NotIn" => $"{key} notin ({values})",
                    "Exists" => key!, "DoesNotExist" => $"!{key}",
                    _ => throw new InvalidOperationException("Unsupported deployment selector operator.")
                });
            }
        if (parts.Count == 0) throw new InvalidOperationException("Deployment selector is empty.");
        return string.Join(",", parts);
    }

    private static async Task<KubernetesCommandResult> RunKubectlAsync(IReadOnlyList<string> args, string? cwd, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("kubectl") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (cwd != null) start.WorkingDirectory = cwd;
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return new KubernetesCommandResult(process.ExitCode, await stdout, await stderr);
    }
}
