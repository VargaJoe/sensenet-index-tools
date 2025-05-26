using System.CommandLine;
using System.Data.SqlClient;

namespace SenseNetIndexTools
{
    public class DatabaseLister
    {
        private class ContentItem
        {
            public int NodeId { get; set; }
            public int VersionId { get; set; }
            public string Path { get; set; } = string.Empty;
            public string NodeType { get; set; } = string.Empty;

            public override string ToString()
            {
                return $"{NodeId}\t{VersionId}\t{Path}\t{NodeType}";
            }
        }

        public static Command Create()
        {
            var command = new Command("list-db", "List content items from the database");

            var connectionStringOption = new Option<string>(
                name: "--connection-string",
                description: "SQL Connection string to the SenseNet database");
            connectionStringOption.IsRequired = true;

            var repositoryPathOption = new Option<string>(
                name: "--repository-path",
                description: "Path in the content repository to check (e.g., /Root/Sites/Default_Site)");
            repositoryPathOption.IsRequired = true;

            var recursiveOption = new Option<bool>(
                name: "--recursive",
                description: "Recursively list all content items under the specified path",
                getDefaultValue: () => true);

            var orderByOption = new Option<string>(
                name: "--order-by",
                description: "Order results by: 'path' (default), 'id', 'version', 'type'",
                getDefaultValue: () => "path");
            orderByOption.FromAmong("path", "id", "version", "type");

            var depthOption = new Option<int>(
                name: "--depth",
                description: "Limit listing to specified depth (1=direct children only, 0=all descendants)",
                getDefaultValue: () => 0);

            command.AddOption(connectionStringOption);
            command.AddOption(repositoryPathOption);
            command.AddOption(recursiveOption);
            command.AddOption(orderByOption);
            command.AddOption(depthOption);

            command.SetHandler((string connectionString, string repositoryPath, bool recursive, string orderBy, int depth) =>
            {
                try
                {
                    Console.WriteLine($"Repository path: {repositoryPath}");
                    Console.WriteLine($"Recursive mode: {(recursive ? "Yes" : "No")}");
                    Console.WriteLine($"Depth limit: {depth}");

                    var items = GetContentItemsFromDatabase(connectionString, repositoryPath, recursive, depth);

                    // Sort items based on order-by option and apply case-insensitive sorting for paths
                    items = orderBy switch
                    {
                        "id" => items.OrderBy(i => i.NodeId).ToList(),
                        "version" => items.OrderBy(i => i.VersionId).ToList(),
                        "type" => items.OrderBy(i => i.NodeType).ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList(),
                        _ => items.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList()
                    };

                    // Apply depth filtering after database query
                    if (depth > 0)
                    {
                        var basePath = repositoryPath.TrimEnd('/');
                        var baseDepth = basePath.Count(c => c == '/');
                        items = items.Where(item => {
                            var itemDepth = item.Path.Count(c => c == '/') - baseDepth;
                            return itemDepth <= depth;
                        }).ToList();
                    }

                    Console.WriteLine($"\nFound {items.Count} items in database under path {repositoryPath}:");
                    
                    if (items.Count > 0)
                    {
                        Console.WriteLine("NodeID\tVersionId\tPath\tNodeType");
                        Console.WriteLine(new string('-', 80));

                        foreach (var item in items)
                        {
                            Console.WriteLine(item.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error listing database items: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    Environment.Exit(1);
                }
            }, connectionStringOption, repositoryPathOption, recursiveOption, orderByOption, depthOption);

            return command;
        }

        private static List<ContentItem> GetContentItemsFromDatabase(string connectionString, string path, bool recursive, int depth)
        {
            var items = new List<ContentItem>();
            
            // Sanitize path for SQL query
            string sanitizedPath = path.Replace("'", "''");

            // Build the SQL query
            string sql;
            if (recursive)
            {
                sql = @"
                    SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName 
                    FROM Nodes N
                    JOIN Versions V ON N.NodeId = V.NodeId
                    JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                    WHERE (N.Path = @path OR N.Path LIKE @pathPattern)
                    ORDER BY N.Path";
            }
            else
            {
                sql = @"
                    SELECT N.NodeId, V.VersionId as VersionId, N.Path, NT.Name as NodeTypeName 
                    FROM Nodes N
                    JOIN Versions V ON N.NodeId = V.NodeId
                    JOIN NodeTypes NT ON N.NodeTypeId = NT.NodeTypeId
                    WHERE N.Path = @path
                    ORDER BY N.Path";
            }

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@path", sanitizedPath);
                    if (recursive)
                    {
                        command.Parameters.AddWithValue("@pathPattern", sanitizedPath + "/%");
                    }
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            items.Add(new ContentItem
                            {
                                NodeId = reader.GetInt32(reader.GetOrdinal("NodeId")),
                                VersionId = reader.GetInt32(reader.GetOrdinal("VersionId")),
                                Path = reader.GetString(reader.GetOrdinal("Path")),
                                NodeType = reader.GetString(reader.GetOrdinal("NodeTypeName"))
                            });
                        }
                    }
                }
            }
            
            return items;
        }
    }
}
