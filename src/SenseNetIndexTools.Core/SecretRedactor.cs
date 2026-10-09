using System.Data.SqlClient;
using System.Text;

namespace SenseNetIndexTools;

public static class SecretRedactor
{
    public static string Redact(string? text)
    {
        if (text == null) return "";
        var settings = RuntimeSettings.Load();
        return Redact(text, settings, RuntimeSettings.ReadSecret(Environment.GetEnvironmentVariable("INDEXTOOLS_WEB_TOKEN_FILE")));
    }

    public static string Redact(string? text, RuntimeSettings settings, string webToken = "")
    {
        if (text == null) return "";
        var secrets = new List<string> { settings.ConnectionString, settings.ApiKey, settings.BearerToken, webToken };
        if (!string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            try
            {
                var sql = new SqlConnectionStringBuilder(settings.ConnectionString);
                secrets.Add(sql.Password);
                if (!string.IsNullOrEmpty(sql.UserID)) text = System.Text.RegularExpressions.Regex.Replace(text,
                    @"(?<![A-Za-z0-9_])" + System.Text.RegularExpressions.Regex.Escape(sql.UserID) + @"(?![A-Za-z0-9_])", "[redacted]");
            }
            catch { }
        }
        foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s.Length)) text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        return text;
    }

    public static string RedactJson(string json)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        Walk(node);
        return node!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        static void Walk(System.Text.Json.Nodes.JsonNode? node)
        {
            if (node is System.Text.Json.Nodes.JsonObject obj)
                foreach (var pair in obj.ToArray())
                    if (pair.Value is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text)) obj[pair.Key] = Redact(text);
                    else Walk(pair.Value);
            else if (node is System.Text.Json.Nodes.JsonArray array)
                for (var i = 0; i < array.Count; i++)
                    if (array[i] is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text)) array[i] = Redact(text);
                    else Walk(array[i]);
        }
    }

    public sealed class Writer(TextWriter inner) : TextWriter
    {
        public override Encoding Encoding => inner.Encoding;
        public override void WriteLine(string? value) => inner.WriteLine(Redact(value));
        public override void Write(string? value) => inner.Write(Redact(value));
        public override void Write(char value) => inner.Write(value);
    }
}
