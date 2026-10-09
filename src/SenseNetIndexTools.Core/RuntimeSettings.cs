using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SenseNetIndexTools;

/// <summary>Shared operator configuration. Credentials are read from files, never command arguments.</summary>
public sealed class RuntimeSettings
{
    public string? SourcePath { get; set; }
    public string? SnapshotDirectory { get; set; }
    public string? OutputDirectory { get; set; }
    public string? BackupDirectory { get; set; }
    public string? DataDirectory { get; set; }
    public string? KeyDirectory { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? SqlConnectionStringFile { get; set; }
    public string? ApiKeyFile { get; set; }
    public string? BearerTokenFile { get; set; }
    [JsonIgnore] public string ConnectionString => ReadSecret(SqlConnectionStringFile);
    [JsonIgnore] public string ApiKey => ReadSecret(ApiKeyFile);
    [JsonIgnore] public string BearerToken => ReadSecret(BearerTokenFile);

    public static RuntimeSettings Load()
    {
        var file = Environment.GetEnvironmentVariable("INDEXTOOLS_CONFIG_FILE");
        RuntimeSettings settings;
        try { settings = string.IsNullOrWhiteSpace(file) ? new() : JsonSerializer.Deserialize<RuntimeSettings>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
        catch { throw new InvalidOperationException("Cannot read runtime configuration file."); }
        foreach (var (property, variable) in new[] {
            (nameof(SourcePath), "SOURCE_PATH"), (nameof(SnapshotDirectory), "SNAPSHOT_DIRECTORY"),
            (nameof(OutputDirectory), "OUTPUT_DIRECTORY"), (nameof(BackupDirectory), "BACKUP_DIRECTORY"),
            (nameof(DataDirectory), "DATA_DIRECTORY"), (nameof(KeyDirectory), "KEY_DIRECTORY"),
            (nameof(RepositoryUrl), "REPOSITORY_URL"), (nameof(SqlConnectionStringFile), "SQL_CONNECTION_STRING_FILE"),
            (nameof(ApiKeyFile), "API_KEY_FILE"), (nameof(BearerTokenFile), "BEARER_TOKEN_FILE") })
        {
            var value = Environment.GetEnvironmentVariable("INDEXTOOLS_" + variable);
            if (!string.IsNullOrWhiteSpace(value)) typeof(RuntimeSettings).GetProperty(property)!.SetValue(settings, value);
        }
        if (Environment.GetEnvironmentVariable("INDEXTOOLS_CONTAINER_RUNTIME") == "1")
        {
            settings.SourcePath ??= "/source/index";
            settings.SnapshotDirectory ??= "/state/copies";
            settings.OutputDirectory ??= "/state/reports";
            settings.BackupDirectory ??= "/state/backups";
            settings.DataDirectory ??= "/state/data";
            settings.KeyDirectory ??= "/state/keys";
        }
        return settings;
    }

    public static string ReadSecret(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return File.ReadAllText(path).Trim(); }
        catch { throw new InvalidOperationException("Cannot read configured secret file."); }
    }

    public static Option<string> SqlOption()
    {
        var option = new Option<string>("--connection-string", () => Load().ConnectionString,
            "SQL connection string (prefer INDEXTOOLS_SQL_CONNECTION_STRING_FILE)");
        option.AddValidator(result => { if (string.IsNullOrWhiteSpace(result.GetValueForOption(option))) result.ErrorMessage = "Configure a SQL secret file or provide --connection-string."; });
        return option;
    }

    public static void EnsureWritableCopy(string path)
    {
        var source = Load().SourcePath;
        if (string.IsNullOrWhiteSpace(source)) return;
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (full.Equals(root, comparison) || full.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Source index is read-only. Create an isolated snapshot before writing.");
    }
}
