namespace SenseNetIndexTools;

public static class WorkspacePaths
{
    public static bool IsWithin(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return path.Equals(root, comparison) || path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    public static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Index paths must not contain symbolic links.");
    }

    public static void EnsureNoWriter(string path)
    {
        var file = Path.Combine(path, "write.lock");
        if (!File.Exists(file)) return;
        try
        {
            // Read-only exclusive open: never create, truncate, unlock or delete the file.
            using var probe = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("The writer lock is in use or cannot be inspected read-only. Offline copy refused.", error);
        }
    }
}
