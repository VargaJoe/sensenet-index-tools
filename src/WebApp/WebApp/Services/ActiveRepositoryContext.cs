using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;
using SenseNetIndexTools;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>One operator's circuit and browser-tab selection. Never register as a singleton.</summary>
public sealed class ActiveRepositoryContext
{
    private readonly RuntimeSettings defaults;
    private readonly IDataProtector protector;
    private readonly IHttpClientFactory clients;
    private readonly IJSRuntime js;
    private IndexWorkspace? workspace;
    private bool initialized;
    public event Action? Changed;
    public RepositorySelection Selection { get; private set; }
    public RuntimeSettings Settings { get; private set; }
    public long Revision { get; private set; }
    public bool Busy { get; private set; }
    public bool Ready { get; private set; }
    public IndexWorkspace Workspace => workspace ??= new(Settings, clients.CreateClient("IndexWorkspace"));
    public IndexSource? Source { get { try { var source = Workspace.Sources().SingleOrDefault(s => s.Id == Selection.SourceId); return source != null && Directory.Exists(source.Path) ? source : null; } catch { return null; } } }
    public string IndexPath => Source?.Path ?? (Selection.SourceId == "live" ? Selection.IndexPath : "");
    public bool CanWrite => Source?.Writable == true;

    public ActiveRepositoryContext(IDataProtectionProvider protection, IHttpClientFactory clients, IJSRuntime js)
    {
        defaults = RuntimeSettings.Load();
        protector = protection.CreateProtector("OperatorBrowserSelection.v1");
        this.clients = clients; this.js = js;
        Selection = DefaultSelection(); Settings = BuildSettings(Selection);
    }

    private RepositorySelection DefaultSelection() => new() {
        Name = "Default repository", IndexPath = defaults.SourcePath ?? "", ConnectionString = defaults.ConnectionString,
        RepositoryUrl = defaults.RepositoryUrl ?? "", ApiKey = defaults.ApiKey, BearerToken = defaults.BearerToken,
        RepositoryBackupRoot = defaults.RepositoryBackupRoot ?? "", BackupSourceRoot = defaults.BackupSourceRoot ?? ""
    };

    private RuntimeSettings BuildSettings(RepositorySelection value) => new() {
        SourcePath = string.IsNullOrWhiteSpace(value.IndexPath) ? null : value.IndexPath,
        ConnectionStringValue = value.ConnectionString, RepositoryUrl = value.RepositoryUrl,
        ApiKeyValue = value.ApiKey, BearerTokenValue = value.BearerToken,
        RepositoryBackupRoot = value.RepositoryBackupRoot, BackupSourceRoot = value.BackupSourceRoot,
        SnapshotDirectory = defaults.SnapshotDirectory, BackupDirectory = defaults.BackupDirectory,
        OutputDirectory = defaults.OutputDirectory, DataDirectory = defaults.DataDirectory, KeyDirectory = defaults.KeyDirectory,
        BackupTimeoutSeconds = defaults.BackupTimeoutSeconds, BackupPollMilliseconds = defaults.BackupPollMilliseconds
    };

    public async Task InitializeAsync()
    {
        if (initialized) return;
        initialized = true;
        try {
            var saved = await js.InvokeAsync<string?>("indexToolsSession.read");
            if (!string.IsNullOrWhiteSpace(saved)) {
                var value = JsonSerializer.Deserialize<RepositorySelection>(protector.Unprotect(saved));
                if (value != null) Replace(value);
            }
        }
        catch (System.Security.Cryptography.CryptographicException) { await js.InvokeVoidAsync("indexToolsSession.clear"); }
        catch (JsonException) { await js.InvokeVoidAsync("indexToolsSession.clear"); }
        catch (JSException) { /* Disabled browser storage still permits a private circuit selection. */ }
        finally { Ready = true; Changed?.Invoke(); }
    }

    public async Task UseDefaultAsync() { EnsureIdle(); Replace(DefaultSelection()); await PersistAsync(); }
    public async Task ApplyAsync(SavedConfiguration value)
    {
        EnsureIdle();
        // Replace the whole profile, including empty fields: never mix credentials from two repositories.
        Replace(new() { ConfigurationId = value.Id.ToString(), Name = value.Name, IndexPath = value.IndexPath ?? "",
            ConnectionString = value.ConnectionString ?? "", RepositoryPath = value.RepositoryPath ?? "/Root",
            RepositoryUrl = value.RepositoryUrl ?? "", ApiKey = value.ApiKey ?? "", BearerToken = value.BearerToken ?? "",
            RepositoryBackupRoot = value.RepositoryBackupRoot ?? "", BackupSourceRoot = value.BackupSourceRoot ?? "" });
        await PersistAsync();
    }
    public async Task ApplyManualAsync(RepositorySelection value)
    {
        EnsureIdle(); value.ConfigurationId = null; value.Name = "Manual repository"; value.SourceId = "live";
        Replace(value); await PersistAsync();
    }
    public async Task SelectSourceAsync(string id)
    {
        EnsureIdle(); Workspace.GetSource(id); if (Selection.SourceId == id) return;
        Selection.SourceId = id; Revision++; Changed?.Invoke(); await PersistAsync();
    }
    public IDisposable BeginOperation()
    {
        if (!Ready) throw new InvalidOperationException("The operator session is still loading.");
        EnsureIdle(); Busy = true; Changed?.Invoke();
        return new Operation(this);
    }
    private sealed class Operation(ActiveRepositoryContext context) : IDisposable
    {
        public void Dispose() { context.Busy = false; context.Changed?.Invoke(); }
    }
    private void EnsureIdle() { if (Busy) throw new InvalidOperationException("Wait for the current operation to finish before changing repository or source."); }
    private void Replace(RepositorySelection value)
    {
        Selection = value; Settings = BuildSettings(value); workspace = null; Revision++; Changed?.Invoke();
    }
    public RepositorySelection CopySelection() => JsonSerializer.Deserialize<RepositorySelection>(JsonSerializer.Serialize(Selection))!;
    private async Task PersistAsync()
    {
        try { await js.InvokeVoidAsync("indexToolsSession.write", protector.Protect(JsonSerializer.Serialize(Selection))); }
        catch (JSException) { }
    }
    public Dictionary<string, object> Provenance() => new() {
        ["Repository"] = Selection.Name, ["RepositoryUrl"] = Selection.RepositoryUrl,
        ["IndexSourceId"] = Selection.SourceId, ["IndexPath"] = IndexPath,
        ["SourceKind"] = Source?.Kind.ToString() ?? "Unavailable", ["CapturedUtc"] = Source?.CreatedUtc.ToString("O") ?? ""
    };
}

public sealed class RepositorySelection
{
    public string? ConfigurationId { get; set; }
    public string Name { get; set; } = "Manual repository";
    public string IndexPath { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string RepositoryPath { get; set; } = "/Root";
    public string RepositoryUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string BearerToken { get; set; } = "";
    public string RepositoryBackupRoot { get; set; } = "";
    public string BackupSourceRoot { get; set; } = "";
    public string SourceId { get; set; } = "live";
}
