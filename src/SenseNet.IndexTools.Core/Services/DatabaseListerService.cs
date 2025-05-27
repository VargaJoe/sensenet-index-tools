using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SenseNet.IndexTools.Core.Services
{
    /// <summary>
    /// Service for listing items from a SenseNet database
    /// </summary>
    public class DatabaseListerService
    {
        private readonly ILogger<DatabaseListerService> _logger;

        public DatabaseListerService(ILogger<DatabaseListerService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Represents an item in the database
        /// </summary>
        public class DbItem
        {
            public int NodeId { get; set; }
            public int VersionId { get; set; }
            public string Path { get; set; } = string.Empty;
            public string NodeType { get; set; } = string.Empty;
        }

        /// <summary>
        /// Result of a database listing operation
        /// </summary>
        public class DbListResult
        {
            public string ConnectionString { get; set; } = string.Empty;
            public string RepositoryPath { get; set; } = string.Empty;
            public bool Recursive { get; set; }
            public int Depth { get; set; }
            public DateTime StartTime { get; set; } = DateTime.Now;
            public DateTime EndTime { get; set; } = DateTime.Now;
            public int TotalItems { get; set; }
            public List<DbItem> Items { get; set; } = new List<DbItem>();
            public List<string> Warnings { get; set; } = new List<string>();
            public List<string> Errors { get; set; } = new List<string>();
        }

        /// <summary>
        /// Lists items from a SenseNet database matching the specified criteria
        /// </summary>
        /// <param name="connectionString">SQL Connection string to the SenseNet database</param>
        /// <param name="repositoryPath">Path in the content repository to list from</param>
        /// <param name="recursive">Whether to list items recursively</param>
        /// <param name="depth">Depth limit (0=all descendants, 1=direct children only)</param>
        /// <returns>List of items found in the database</returns>
        public async Task<DbListResult> ListDatabaseItemsAsync(string connectionString, string repositoryPath, bool recursive, int depth = 0)
        {
            var result = new DbListResult
            {
                ConnectionString = connectionString,
                RepositoryPath = repositoryPath,
                Recursive = recursive,
                Depth = depth,
                StartTime = DateTime.Now
            };

            if (string.IsNullOrEmpty(connectionString))
            {
                result.Errors.Add("Connection string is required.");
                result.EndTime = DateTime.Now;
                return result;
            }

            try
            {
                string sanitizedPath = repositoryPath.Replace("'", "''");
                string sql;

                if (recursive)
                {
                    if (depth > 0)
                    {
                        // We need to count path segments to determine depth
                        int baseSegments = sanitizedPath.Split('/').Length - 1;
                        int maxSegments = baseSegments + depth;
                        
                        sql = @"
                            SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName 
                            FROM Nodes N
                            JOIN Versions V ON N.NodeId = V.NodeId
                            JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                            WHERE (LOWER(N.Path) = LOWER(@path) OR 
                                  (LOWER(N.Path) LIKE LOWER(@pathPattern) AND 
                                   (LEN(N.Path) - LEN(REPLACE(N.Path, '/', ''))) <= @maxSegments))
                            ORDER BY N.Path";
                    }
                    else
                    {
                        sql = @"
                            SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName 
                            FROM Nodes N
                            JOIN Versions V ON N.NodeId = V.NodeId
                            JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                            WHERE (LOWER(N.Path) = LOWER(@path) OR LOWER(N.Path) LIKE LOWER(@pathPattern))
                            ORDER BY N.Path";
                    }
                }
                else
                {
                    // Non-recursive, only immediate children
                    sql = @"
                        SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName 
                        FROM Nodes N
                        JOIN Versions V ON N.NodeId = V.NodeId
                        JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                        WHERE LOWER(N.Path) = LOWER(@path)
                        ORDER BY N.Path";
                }

                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    using var command = new SqlCommand(sql, connection);
                    
                    command.Parameters.AddWithValue("@path", sanitizedPath);
                    
                    if (recursive)
                    {
                        command.Parameters.AddWithValue("@pathPattern", sanitizedPath.TrimEnd('/') + "/%");
                        
                        if (depth > 0)
                        {
                            int baseSegments = sanitizedPath.Split('/').Length - 1;
                            int maxSegments = baseSegments + depth;
                            command.Parameters.AddWithValue("@maxSegments", maxSegments);
                        }
                    }

                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        result.Items.Add(new DbItem
                        {
                            NodeId = reader.GetInt32(0),
                            VersionId = reader.GetInt32(1),
                            Path = reader.GetString(2),
                            NodeType = reader.GetString(3)
                        });
                    }
                }

                result.TotalItems = result.Items.Count;
                
                if (result.Items.Count == 0)
                {
                    result.Warnings.Add($"No items found at path: {repositoryPath}");
                    result.Warnings.Add("Check if the path exists in the content repository.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accessing database: {Message}", ex.Message);
                result.Errors.Add($"Error accessing database: {ex.Message}");
                result.Errors.Add(ex.StackTrace ?? string.Empty);
            }

            result.EndTime = DateTime.Now;
            return result;
        }
    }
}
