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
            // Get the index directory name
            var indexDirInfo = new DirectoryInfo(path);
            var indexDirName = indexDirInfo.Name;

            // Create a "Backups" directory next to the index directory, not inside it
            var parentDir = indexDirInfo.Parent?.FullName ?? ".";
            var backupsRootDir = backupPath ?? Path.Combine(parentDir, "IndexBackups");

            // Make sure the backups root directory exists
            if (!IODirectory.Exists(backupsRootDir))
            {
                IODirectory.CreateDirectory(backupsRootDir);
            }

            // Create a backup directory with timestamp and index name
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPathFinal = Path.Combine(backupsRootDir, $"{indexDirName}_backup_{timestamp}");

            Console.WriteLine($"Creating backup at {backupPathFinal}");
            IODirectory.CreateDirectory(backupPathFinal);

            // Copy all files from the index directory to the backup
            foreach (var file in IODirectory.GetFiles(path))
            {
                File.Copy(file, Path.Combine(backupPathFinal, Path.GetFileName(file)));
            }

            Console.WriteLine("Backup completed successfully.");
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
