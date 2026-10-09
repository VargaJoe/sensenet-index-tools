using System.CommandLine;
using System.Data.SqlClient;
using System.Text.Json;
using Lucene.Net.Index;
using Lucene.Net.Store;

namespace SenseNetIndexTools;

/// <summary>Opt-in live acceptance. The only REST write is a single non-recursive IndexOnly request.</summary>
public static class VerifyRepositoryCommand
{
    public static Command Create()
    {
        var command = new Command("verify-repository", "Validate a snapshot and compare a selected content with SQL; optionally test a targeted REST rebuild");
        var path = new Option<string>("--path", () => RuntimeSettings.Load().SourcePath ?? "", "Mounted source index");
        var target = new Option<string>("--repository-path", "Exact operator-selected test content path") { IsRequired = true };
        var rebuild = new Option<bool>("--rebuild", () => false, "Rebuild only the selected test content and verify a newer commit plus SQL/index parity");
        command.AddOption(path); command.AddOption(target); command.AddOption(rebuild);
        command.SetHandler(async context => {
            try
            {
                var settings = RuntimeSettings.Load();
                var source = IndexSnapshot.Resolve(context.ParseResult.GetValueForOption(path)!);
                var content = context.ParseResult.GetValueForOption(target)!;
                // Validate target syntax before SQL or REST work.
                var action = RebuildIndexRequest.GetActionUrl(settings.RepositoryUrl ?? "https://repository.example.invalid", content, null);
                var copies = settings.SnapshotDirectory ?? "IndexCopies";
                var beforeVersion = Version(source);
                var snapshot = IndexSnapshot.Create(source, copies);
                var validation = new IndexValidator(snapshot).Validate(true).ToList();
                var errors = validation.Count(r => r.Severity == ValidationSeverity.Error);
                var activity = ActivityStatusReader.Read(snapshot);
                using var sql = new SqlConnection(settings.ConnectionString);
                await sql.OpenAsync(context.GetCancellationToken());
                using var query = new SqlCommand("SELECT MAX(IndexingActivityId) FROM dbo.IndexingActivities", sql);
                var databaseActivity = Convert.ToInt64(await query.ExecuteScalarAsync(context.GetCancellationToken()));
                var comparison = new ContentComparer().CompareContent(snapshot, settings.ConnectionString, content, false, 0);
                var rebuilt = context.ParseResult.GetValueForOption(rebuild);
                int? restStatus = null;
                long? rebuildActivityId = null;
                int? indexActivityAfterRebuild = null;
                if (rebuilt)
                {
                    if (errors > 0 || comparison.Count == 0 || comparison.All(i => !i.InDatabase)) throw new InvalidOperationException("Selected test content or source validation failed; rebuild was not sent.");
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                    using var read = new HttpRequestMessage(HttpMethod.Get, action[..^"/RebuildIndex".Length] + "?$select=Id,Path");
                    AddAuthentication(read, settings);
                    using var before = await http.SendAsync(read, context.GetCancellationToken());
                    before.EnsureSuccessStatusCode();
                    var beforeJson = await before.Content.ReadAsStringAsync(context.GetCancellationToken());
                    var id = ContentId(beforeJson);
                    if (!comparison.Any(i => i.NodeId == id && i.InDatabase)) throw new InvalidOperationException("REST and SQL identify different test content; rebuild was not sent.");
                    using var request = RebuildIndexRequest.Create(settings.RepositoryUrl!, settings.ApiKey, content, null, false, "IndexOnly", settings.BearerToken);
                    using var response = await http.SendAsync(request, context.GetCancellationToken());
                    restStatus = (int)response.StatusCode;
                    response.EnsureSuccessStatusCode();
                    using var activityQuery = new SqlCommand("SELECT MAX(IndexingActivityId) FROM dbo.IndexingActivities WHERE NodeId=@node AND ActivityType='Rebuild' AND IndexingActivityId>@before", sql);
                    activityQuery.Parameters.AddWithValue("@node", id);
                    activityQuery.Parameters.AddWithValue("@before", databaseActivity);
                    var changed = false;
                    for (var attempt = 0; attempt < 12; attempt++)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), context.GetCancellationToken());
                        var activityValue = await activityQuery.ExecuteScalarAsync(context.GetCancellationToken());
                        if (activityValue != DBNull.Value && activityValue != null) rebuildActivityId = Convert.ToInt64(activityValue);
                        var status = ActivityStatusReader.Read(source);
                        if (rebuildActivityId != null && Version(source) != beforeVersion && status.LastActivityId >= rebuildActivityId && !status.Gaps.Contains((int)rebuildActivityId))
                        { changed = true; indexActivityAfterRebuild = status.LastActivityId; break; }
                    }
                    if (!changed) throw new InvalidOperationException("REST accepted the request but its selected-content Rebuild activity was not confirmed in the source index.");
                    snapshot = IndexSnapshot.Create(source, copies);
                    comparison = new ContentComparer().CompareContent(snapshot, settings.ConnectionString, content, false, 0);
                    using var afterRequest = new HttpRequestMessage(HttpMethod.Get, action[..^"/RebuildIndex".Length] + "?$select=Id,Path");
                    AddAuthentication(afterRequest, settings);
                    using var afterResponse = await http.SendAsync(afterRequest, context.GetCancellationToken());
                    afterResponse.EnsureSuccessStatusCode();
                    if (ContentId(await afterResponse.Content.ReadAsStringAsync(context.GetCancellationToken())) != id) throw new InvalidOperationException("REST content identity changed.");
                }
                var mismatches = comparison.Count(i => i.Status != "Match");
                var success = errors == 0 && comparison.Count > 0 && mismatches == 0;
                var summary = JsonSerializer.Serialize(new { Success = success, ValidationErrors = errors, ValidationWarnings = validation.Count(r => r.Severity == ValidationSeverity.Warning),
                    IndexLastActivityId = activity.LastActivityId, DatabaseLastActivityId = databaseActivity, Items = comparison.Count, Mismatches = mismatches,
                    RestRebuildRequested = rebuilt, RestStatus = restStatus, RebuildActivityId = rebuildActivityId, IndexActivityAfterRebuild = indexActivityAfterRebuild, Snapshot = snapshot }, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(summary);
                if (settings.OutputDirectory != null) { System.IO.Directory.CreateDirectory(settings.OutputDirectory); await File.WriteAllTextAsync(Path.Combine(settings.OutputDirectory, "integration-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + ".json"), summary); }
                context.ExitCode = success ? 0 : 1;
            }
            catch (Exception ex) { Console.Error.WriteLine(SecretRedactor.Redact(ex.Message)); context.ExitCode = 1; }
        });
        return command;
    }

    private static long Version(string path) { using var directory = FSDirectory.Open(new DirectoryInfo(path)); using var reader = IndexReader.Open(directory, true); return reader.GetVersion(); }
    private static int ContentId(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("d", out var d)) root = d;
        if (root.TryGetProperty("Id", out var id)) return id.GetInt32();
        throw new InvalidOperationException("REST response did not identify the selected content.");
    }
    private static void AddAuthentication(HttpRequestMessage request, RuntimeSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BearerToken)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.BearerToken);
        else request.Headers.Add("apikey", settings.ApiKey);
    }
}
