using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SenseNetIndexTools;

public static class RebuildIndexRequest
{
    public static string GetActionUrl(string repoUrl, string? contentPath, string? contentId)
    {
        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out var repo) ||
            (repo.Scheme != Uri.UriSchemeHttps && repo.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(repo.UserInfo) || !string.IsNullOrEmpty(repo.Query) || !string.IsNullOrEmpty(repo.Fragment))
            throw new ArgumentException("Repository URL must be an absolute HTTP(S) URL without credentials, query or fragment.");
        if (string.IsNullOrWhiteSpace(contentPath) == string.IsNullOrWhiteSpace(contentId))
            throw new ArgumentException("Specify exactly one content path or ID.");

        var prefix = repo.AbsoluteUri.TrimEnd('/') + "/odata.svc";
        if (!string.IsNullOrWhiteSpace(contentId))
        {
            if (!int.TryParse(contentId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ArgumentException("Content ID must be a positive integer.");
            return $"{prefix}/Content({id})/RebuildIndex";
        }

        var path = contentPath!.TrimEnd('/');
        if (path != "/Root" && !path.StartsWith("/Root/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Content path must start with /Root.");
        var parts = path.Split('/').Skip(1).ToArray();
        if (parts.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".."))
            throw new ArgumentException("Content path contains an invalid segment.");
        var parent = parts.Length == 1 ? "/" : "/" + string.Join("/", parts.SkipLast(1).Select(Uri.EscapeDataString));
        var name = Uri.EscapeDataString(parts[^1].Replace("'", "''"));
        return $"{prefix}{parent}('{name}')/RebuildIndex";
    }

    public static HttpRequestMessage Create(string repoUrl, string apiKey, string? path, string? id,
        bool recursive, string rebuildLevel)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API key is required.");
        if (rebuildLevel is not ("IndexOnly" or "DatabaseOnly" or "IndexAndDatabase"))
            throw new ArgumentException("Invalid rebuild level.");
        var request = new HttpRequestMessage(HttpMethod.Post, GetActionUrl(repoUrl, path, id));
        request.Headers.Add("apikey", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new { Recursive = recursive, RebuildLevel = rebuildLevel }),
            Encoding.UTF8, "application/json");
        return request;
    }
}
