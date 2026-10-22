using SenseNetIndexTools;
using WebApp.Models;

namespace WebApp.Services;

public sealed class OperatorReportService
{
    private readonly ActiveRepositoryContext? context;
    private readonly IndexWorkspace? fixedWorkspace;
    private readonly ReportStorageService reports;
    public OperatorReportService(ActiveRepositoryContext context, ReportStorageService reports) { this.context = context; this.reports = reports; }
    public OperatorReportService(IndexWorkspace workspace, ReportStorageService reports) { fixedWorkspace = workspace; this.reports = reports; }
    private IndexWorkspace workspace => context?.Workspace ?? fixedWorkspace!;
    public async Task<IndexComparison> OpenComparisonAsync(string id, ConfigurationService configurations)
    {
        var comparison = workspace.GetComparison(id);
        if (context != null)
        {
            var saved = (await reports.GetReportsAsync()).SingleOrDefault(report => report.Id == id);
            if (saved?.ConfigurationId is { } profileId && profileId != context.Selection.ConfigurationId)
            {
                if (!Guid.TryParse(profileId, out var parsed)) throw new InvalidOperationException("The report's repository profile is invalid.");
                var profile = await configurations.GetConfigurationAsync(parsed);
                if (profile != null) await context.ApplyAsync(profile);
                else if (!workspace.Sources().Any(source => source.Id == comparison.SourceId && source.RepositoryBinding == comparison.RepositoryBinding))
                    throw new InvalidOperationException("The report's repository profile no longer exists. Select its repository manually.");
            }
            else if (saved?.ConfigurationId == null && saved?.ConfigurationName == "Default repository")
                await context.UseDefaultAsync();
        }
        if (context != null) await context.SelectSourceAsync(comparison.SourceId);
        return comparison;
    }
    public async Task<IndexComparison> CompareAsync(string sourceId, string scope, bool recursive, CancellationToken cancellation)
    {
        var comparison = await workspace.CompareAsync(sourceId, scope, recursive, cancellation);
        var source = workspace.GetSource(sourceId);
        var content = new SubtreeIndexChecker().GenerateSubtreeReportFromItems(comparison.Items, scope, recursive, "detailed", "html");
        var markdown = new SubtreeIndexChecker().GenerateSubtreeReportFromItems(comparison.Items, scope, recursive, "detailed", "md");
        await reports.SaveReportAsync(new StoredReport {
            Id = comparison.Id, Name = $"{source.Name}: {scope}", ReportType = "subtree-check", Content = content, Format = "html",
            ConfigurationId = context?.Selection.ConfigurationId, ConfigurationName = context?.Selection.Name,
            Parameters = new() { ["IndexSourceId"] = sourceId, ["SourceKind"] = source.Kind.ToString(),
                ["CapturedUtc"] = source.CreatedUtc, ["IndexPath"] = source.Path, ["RepositoryPath"] = scope, ["ComparisonId"] = comparison.Id },
            Statistics = new() { ["Items"] = comparison.Items.Count, ["Differences"] = comparison.Items.Count(i => i.Status != "Match"), ["Orphans"] = comparison.Orphans.Count }
        }, new Dictionary<string, string> { ["html"] = content, ["md"] = markdown });
        return comparison;
    }

    public async Task SaveActionAsync(string name, object result)
    {
        await reports.SaveReportAsync(new StoredReport { Name = name, ReportType = "maintenance", Format = "md",
            ConfigurationId = context?.Selection.ConfigurationId, ConfigurationName = context?.Selection.Name, Parameters = context?.Provenance() ?? new(),
            Content = "```json\n" + System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n```" });
    }
}
