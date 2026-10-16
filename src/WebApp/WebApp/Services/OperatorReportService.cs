using SenseNetIndexTools;
using WebApp.Models;

namespace WebApp.Services;

public sealed class OperatorReportService(IndexWorkspace workspace, ReportStorageService reports)
{
    public async Task<IndexComparison> CompareAsync(string sourceId, string scope, bool recursive, CancellationToken cancellation)
    {
        var comparison = await workspace.CompareAsync(sourceId, scope, recursive, cancellation);
        var source = workspace.GetSource(sourceId);
        var content = new SubtreeIndexChecker().GenerateSubtreeReportFromItems(comparison.Items, scope, recursive, "detailed", "html");
        await reports.SaveReportAsync(new StoredReport {
            Id = comparison.Id, Name = $"{source.Name}: {scope}", ReportType = "subtree-check", Content = content, Format = "html",
            Parameters = new() { ["IndexSourceId"] = sourceId, ["SourceKind"] = source.Kind.ToString(),
                ["CapturedUtc"] = source.CreatedUtc, ["IndexPath"] = source.Path, ["RepositoryPath"] = scope, ["ComparisonId"] = comparison.Id },
            Statistics = new() { ["Items"] = comparison.Items.Count, ["Differences"] = comparison.Items.Count(i => i.Status != "Match"), ["Orphans"] = comparison.Orphans.Count }
        });
        return comparison;
    }

    public async Task SaveActionAsync(string name, object result)
    {
        await reports.SaveReportAsync(new StoredReport { Name = name, ReportType = "maintenance", Format = "md",
            Content = "```json\n" + System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n```" });
    }
}
