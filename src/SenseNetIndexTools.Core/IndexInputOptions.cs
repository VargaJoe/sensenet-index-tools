using System.CommandLine;
using System.CommandLine.Invocation;

namespace SenseNetIndexTools;

/// <summary>Common local-index or Kubernetes-copy input for CLI operations.</summary>
public sealed class IndexInputOptions
{
    private readonly Option<string> _path;
    private readonly Option<bool> _copy = new("--auto-copy-index", "Copy an index from Kubernetes before the local operation");
    private readonly Option<string?> _kubeconfig = new("--kubeconfig", "Kubeconfig path; omitted uses kubectl's current context");
    private readonly Option<string> _namespace = new("--namespace", () => "default", "Kubernetes namespace");
    private readonly Option<string?> _deployment = new("--deployment", "Kubernetes deployment name");
    private readonly Option<string?> _container = new("--container", "Pod container name");
    private readonly Option<string> _podPath = new("--index-path-in-pod", () => "/app/App_Data/LocalIndex", "Parent directory containing dated index folders");
    private readonly Option<string> _output = new("--copy-output-path", () => Path.Combine(Environment.CurrentDirectory, "IndexBackups"), "Directory for local copies");

    public IndexInputOptions(Command command, Option<string> path)
    {
        _path = path;
        path.IsRequired = false;
        foreach (var option in new Option[] { _copy, _kubeconfig, _namespace, _deployment, _container, _podPath, _output })
            command.AddOption(option);
        command.AddValidator(result => {
            if (result.GetValueForOption(_copy))
            {
                if (string.IsNullOrWhiteSpace(result.GetValueForOption(_deployment))) result.ErrorMessage = "--deployment is required with --auto-copy-index";
                else if (!string.IsNullOrWhiteSpace(result.GetValueForOption(_path))) result.ErrorMessage = $"Use either {_path.Name} or --auto-copy-index.";
            }
            else if (string.IsNullOrWhiteSpace(result.GetValueForOption(_path))) result.ErrorMessage = $"{_path.Name} is required without --auto-copy-index";
        });
    }

    public Task<string> ResolveAsync(InvocationContext context)
    {
        if (!context.ParseResult.GetValueForOption(_copy)) return Task.FromResult(context.ParseResult.GetValueForOption(_path)!);
        return new KubernetesIndexCopier().CopyAsync(new KubernetesIndexCopyOptions {
            Kubeconfig = context.ParseResult.GetValueForOption(_kubeconfig), Namespace = context.ParseResult.GetValueForOption(_namespace)!,
            Deployment = context.ParseResult.GetValueForOption(_deployment)!, Container = context.ParseResult.GetValueForOption(_container),
            IndexPathInPod = context.ParseResult.GetValueForOption(_podPath)!, OutputDirectory = context.ParseResult.GetValueForOption(_output)!
        }, context.GetCancellationToken());
    }
}
