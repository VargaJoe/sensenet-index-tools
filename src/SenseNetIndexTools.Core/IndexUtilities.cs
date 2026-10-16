using System;
using System.IO;
using System.Linq;
using IODirectory = System.IO.Directory;

namespace SenseNetIndexTools
{
    public static class IndexUtilities
    {
        public static void CreateBackup(string path, string? backupPath = null)
        {
            var output = backupPath ?? RuntimeSettings.Load().BackupDirectory ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "IndexBackups");
            Console.WriteLine($"Backup completed: {IndexSnapshot.Create(path, output)}");
        }

        // Helper method to check if a directory is a valid Lucene index
        public static bool IsValidLuceneIndex(string path)
        {
            if (!IODirectory.Exists(path))
            {
                Console.Error.WriteLine($"Directory does not exist: {path}");
                return false;
            }

            // Look for common Lucene index files
            var files = IODirectory.GetFiles(path);

            // Check for segments file which is typically present in Lucene indices
            if (!files.Any(f => Path.GetFileName(f).StartsWith("segments")))
            {
                Console.Error.WriteLine("No segments file found. This doesn't appear to be a valid Lucene index.");
                return false;
            }
            // If we have segments files, consider it a valid index
            // Lucene indices can use either compound .cfs files or individual component files (.fdt, .fdx, etc.)
            return true;
        }
    }
}
