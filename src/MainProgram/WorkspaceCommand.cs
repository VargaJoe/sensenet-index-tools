using System.CommandLine;
using System.Text.Json;

namespace SenseNetIndexTools;

public static class WorkspaceCommand
{
    public static Command Create()
    {
        var command = new Command("workspace", "Prepare sources, compare and repair with the same workflow as the operator UI");
        var action = new Argument<string>("action");
        action.FromAmong("sources", "backup", "recover", "copy", "compare", "cleanup", "repair");
        var source = new Option<string>("--source", () => "live", "Source ID from workspace sources");
        var folder = new Option<string?>("--folder", "New folder name within the configured repository backup root");
        var scope = new Option<string>("--repository-path", () => "/Root/Content", "Comparison scope");
        var recursive = new Option<bool>("--recursive", () => true);
        var report = new Option<string?>("--report", "Comparison ID");
        var ids = new Option<int[]>("--node-id", "Selected report node IDs") { AllowMultipleArgumentsPerToken = true };
        var confirm = new Option<bool>("--confirm", "Apply the explicitly selected repair/cleanup; omitted is preview only");
        var offline = new Option<bool>("--offline", "Confirm all source index writers are stopped before copying a live source");
        command.AddArgument(action);
        foreach (var option in new Option[] { source, folder, scope, recursive, report, ids, confirm, offline }) command.AddOption(option);
        command.AddValidator(result => {
            if (result.GetValueForArgument(action) is "cleanup" or "repair" && string.IsNullOrWhiteSpace(result.GetValueForOption(report)))
                result.ErrorMessage = "--report is required for cleanup and repair. Run workspace compare first.";
        });
        command.SetHandler(async context => {
            try
            {
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
                var workspace = new IndexWorkspace(http);
                var selected = context.ParseResult.GetValueForArgument(action);
                var sourceId = context.ParseResult.GetValueForOption(source)!;
                var reportId = context.ParseResult.GetValueForOption(report);
                var confirmed = context.ParseResult.GetValueForOption(confirm);
                object result = selected switch {
                    "sources" => workspace.Sources(),
                    "backup" => await workspace.BackupAsync(context.ParseResult.GetValueForOption(folder), cancellation: context.GetCancellationToken()),
                    "recover" => new { Discovered = await workspace.RecoverCompletedBackupsAsync(context.GetCancellationToken()) },
                    "copy" => workspace.Copy(sourceId, context.ParseResult.GetValueForOption(offline)),
                    "compare" => await workspace.CompareAsync(sourceId, context.ParseResult.GetValueForOption(scope)!, context.ParseResult.GetValueForOption(recursive), context.GetCancellationToken()),
                    "cleanup" when confirmed => new { Removed = await workspace.CleanAsync(reportId!, true, context.GetCancellationToken()) },
                    "cleanup" => workspace.GetComparison(reportId!).Orphans,
                    "repair" when confirmed => await workspace.RepairAsync(reportId!, context.ParseResult.GetValueForOption(ids) ?? [], true, context.GetCancellationToken()),
                    "repair" => workspace.GetComparison(reportId!).RepairCandidates,
                    _ => throw new ArgumentException("Unknown workspace action.")
                };
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error) { Console.Error.WriteLine(SecretRedactor.Redact(error.Message)); context.ExitCode = 1; }
        });
        return command;
    }
}
