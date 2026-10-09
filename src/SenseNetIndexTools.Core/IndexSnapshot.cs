using System.Security.Cryptography;
using System.Text.Json;
using Lucene.Net.Index;
using Lucene.Net.Store;
using IODirectory = System.IO.Directory;

namespace SenseNetIndexTools;

/// <summary>Copy exactly one Lucene 2.9 commit; never copy an arbitrary live directory tree.</summary>
public static class IndexSnapshot
{
    public static string Resolve(string path)
    {
        var root = Path.GetFullPath(path);
        RejectLink(root);
        if (IODirectory.EnumerateFiles(root, "segments_*").Any() || File.Exists(Path.Combine(root, "segments"))) return root;
        var candidates = IODirectory.EnumerateDirectories(root)
            .Where(p => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), "^[0-9]{14}$"))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            RejectLink(candidate);
            if (IODirectory.EnumerateFiles(candidate, "segments_*").Any()) return candidate;
        }
        throw new InvalidOperationException("No Lucene index or dated index directory was found.");
    }

    public static string Create(string source, string outputDirectory, int attempts = 3)
    {
        source = Resolve(source);
        var output = Path.GetFullPath(outputDirectory);
        if (IsWithin(output, source)) throw new ArgumentException("Snapshot output must be outside the source index.");
        IODirectory.CreateDirectory(output);
        RejectLink(output);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var destination = Path.Combine(output, "index-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N"));
            var partial = destination + ".partial";
            IODirectory.CreateDirectory(partial);
            try
            {
                using var directory = FSDirectory.Open(new DirectoryInfo(source));
                using var reader = IndexReader.Open(directory, true);
                var commit = reader.GetIndexCommit();
                var names = commit.GetFileNames().Cast<string>().OrderBy(n => n, StringComparer.Ordinal).ToArray();
                var hashes = new Dictionary<string, string>();
                foreach (var name in names)
                {
                    if (Path.GetFileName(name) != name || name == "write.lock") throw new InvalidOperationException("Invalid commit file name.");
                    var original = Path.Combine(source, name);
                    RejectLink(original);
                    var hash = Hash(original);
                    var copied = Path.Combine(partial, name);
                    File.Copy(original, copied);
                    if (hash != Hash(copied) || hash != Hash(original)) throw new IOException("Commit changed during copy.");
                    hashes[name] = hash;
                }
                using (var copy = FSDirectory.Open(new DirectoryInfo(partial)))
                using (var check = IndexReader.Open(copy, true))
                {
                    if (check.NumDocs() != reader.NumDocs() || check.MaxDoc() != reader.MaxDoc()) throw new IOException("Snapshot document count differs.");
                    if (!check.GetIndexCommit().GetFileNames().Cast<string>().OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(names)) throw new IOException("Snapshot commit differs.");
                }
                File.WriteAllText(Path.Combine(partial, "snapshot-manifest.json"), JsonSerializer.Serialize(new {
                    Format = "Lucene29", CreatedUtc = DateTime.UtcNow, Documents = reader.NumDocs(), Files = hashes
                }, new JsonSerializerOptions { WriteIndented = true }));
                IODirectory.Move(partial, destination);
                return destination;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Only remove our freshly-created partial directory, never a supplied source/output tree.
                IODirectory.Delete(partial, true);
                if (attempt + 1 == attempts) throw new IOException("Unable to capture a consistent Lucene commit. Retry or supply a storage snapshot.");
            }
            catch { IODirectory.Delete(partial, true); throw; }
        }
        throw new ArgumentOutOfRangeException(nameof(attempts));
    }

    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static bool IsWithin(string path, string root) => path.Equals(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static void RejectLink(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Index snapshot paths must not contain symbolic links.");
    }
}
