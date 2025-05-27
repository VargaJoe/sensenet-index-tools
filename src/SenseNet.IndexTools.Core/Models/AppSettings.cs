using System.Collections.Generic;

namespace SenseNet.IndexTools.Core.Models
{
    /// <summary>
    /// Application settings that can be configured by users
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Default index paths that will be pre-populated in forms
        /// </summary>
        public List<IndexPath> DefaultIndexPaths { get; set; } = new List<IndexPath>();
        
        /// <summary>
        /// Default database connection strings that will be pre-populated in forms
        /// </summary>
        public List<DatabaseConnection> DefaultDatabaseConnections { get; set; } = new List<DatabaseConnection>();
        
        /// <summary>
        /// Default backup settings
        /// </summary>
        public BackupSettings BackupSettings { get; set; } = new BackupSettings();
    }

    /// <summary>
    /// Represents a saved index path
    /// </summary>
    public class IndexPath
    {
        /// <summary>
        /// Name for this index path (e.g. "Production Index", "Test Index")
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Full path to the index directory
        /// </summary>
        public string Path { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a saved database connection
    /// </summary>
    public class DatabaseConnection
    {
        /// <summary>
        /// Name for this connection (e.g. "Production DB", "Test DB")
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Connection string
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;
    }

    /// <summary>
    /// Settings for backup operations
    /// </summary>
    public class BackupSettings
    {
        /// <summary>
        /// Whether to create backups by default
        /// </summary>
        public bool CreateBackupsByDefault { get; set; } = true;
        
        /// <summary>
        /// Default path for storing backups
        /// </summary>
        public string DefaultBackupPath { get; set; } = string.Empty;
    }
}
