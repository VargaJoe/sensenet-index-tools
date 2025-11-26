using System.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Diagnostics;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Search;
using SenseNet.Search.Lucene29;
using SenseNet.Security.EFCSecurityStore;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

namespace SenseNetIndexTools.Native;

/// <summary>
/// Creates SenseNet indexes using SenseNet's official indexing infrastructure.
/// This implementation follows the pattern from SenseNet's official SnIndexRebuilder.
/// </summary>
public static class SenseNetRealIndexCreator
{
    public static async Task<IndexCreationResult> CreateIndexAsync(IndexCreationOptions options)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var processedCount = 0;

        // Validate connection string
        if (!ValidateConnectionString(options.ConnectionString))
        {
            return new IndexCreationResult
            {
                Success = false,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                ProcessedItemCount = 0,
                Message = "Invalid or empty connection string",
                IndexPath = options.OutputPath
            };
        }

        // Ensure output directory exists
        if (!string.IsNullOrEmpty(options.OutputPath) && !IODirectory.Exists(options.OutputPath))
        {
            IODirectory.CreateDirectory(options.OutputPath);
        }

        Console.WriteLine();
        Console.WriteLine($"🔧 SenseNet Index Creation Tool (Official SenseNet Implementation)");
        Console.WriteLine($"📁 Index path: {options.OutputPath}");
        Console.WriteLine($"🔗 Connection: {options.ConnectionString.Substring(0, Math.Min(50, options.ConnectionString.Length))}...");
        Console.WriteLine($"⚠️  Note: Using SenseNet's built-in indexing");
        Console.WriteLine();

        try
        {
            // Create service provider with proper SenseNet configuration
            var services = CreateServiceProvider(options.ConnectionString);
            Providers.Instance = new Providers(services);

            // Reset blob providers (following official pattern)
            Providers.Instance.ResetBlobProviders(new ConnectionStringOptions { Repository = options.ConnectionString });

            // CRITICAL: Disable outer search engine to prevent processing old indexing activities
            // This is the key insight from the official implementation
            Indexing.IsOuterSearchEngineEnabled = false;

            // Build repository with proper configuration
            var builder = new RepositoryBuilder(services)
                .SetConsole(Console.Out)
                .UseLucene29LocalSearchEngine(
                    services.GetService<ILogger<Lucene29SearchEngine>>(),
                    options.OutputPath ?? "."
                ) as RepositoryBuilder;

            // Disable automatic indexing activity processing during startup
            if (builder != null)
                builder.StartIndexingEngine = false;

            Console.WriteLine("🚀 Starting SenseNet Repository...");
            
            // Start repository with indexing disabled
            using (var repositoryInstance = Repository.Start(builder))
            {
                Console.WriteLine("✅ Repository started successfully (indexing disabled)!");

                // Get total node count for progress tracking
                Console.WriteLine("📊 Getting total node count...");
                var totalNodes = await Providers.Instance.DataStore.GetNodeCountAsync(CancellationToken.None);
                Console.WriteLine($"📈 Total nodes to index: {totalNodes:N0}");

                // Enable indexing for clean rebuild
                Console.WriteLine("� Enabling indexing for clean rebuild...");
                Indexing.IsOuterSearchEngineEnabled = true;
                Providers.Instance.SearchManager.IsOuterEngineEnabled = true;

                // Get the index populator
                var populator = Providers.Instance.SearchManager.GetIndexPopulator();

                // Set up progress monitoring
                var indexCount = 0;
                populator.NodeIndexed += (sender, eventArgs) =>
                {
                    indexCount++;
                    if (indexCount % 1000 == 0)
                    {
                        var percentage = (indexCount * 100.0) / totalNodes;
                        Console.WriteLine($"📊 Indexed {indexCount:N0} / {totalNodes:N0} nodes ({percentage:F1}%)");
                    }
                };

                // Set up error handling
                populator.IndexingError += (sender, eventArgs) =>
                {
                    var errorMessage = $"⚠️  Indexing error for {eventArgs.Path}: {eventArgs.Exception?.Message}";
                    Console.WriteLine(errorMessage);
                };

                Console.WriteLine("� Starting index rebuild from database...");

                // Rebuild index from current database state - THIS IS THE KEY METHOD
                await populator.ClearAndPopulateAllAsync(CancellationToken.None, Console.Out);

                processedCount = indexCount;
            }

            stopwatch.Stop();

            return new IndexCreationResult
            {
                Success = true,
                StartTime = DateTime.UtcNow.Subtract(stopwatch.Elapsed),
                EndTime = DateTime.UtcNow,
                ProcessedItemCount = processedCount,
                Message = $"Successfully rebuilt SenseNet index using SenseNet's indexing infrastructure with {processedCount:N0} content items in {stopwatch.Elapsed.TotalSeconds:F1} seconds.",
                IndexPath = options.OutputPath
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new IndexCreationResult
            {
                Success = false,
                StartTime = DateTime.UtcNow.Subtract(stopwatch.Elapsed),
                EndTime = DateTime.UtcNow,
                ProcessedItemCount = processedCount,
                Message = $"SenseNet index creation failed: {ex.Message}. Stack: {ex.StackTrace}",
                IndexPath = options.OutputPath
            };
        }
        finally
        {
            try
            {
                Repository.Shutdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning during shutdown: {ex.Message}");
            }
        }
    }

    private static ServiceProvider CreateServiceProvider(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SnCrMsSql"] = connectionString,
                ["ConnectionStrings:SnCrMsSqlAspectStore"] = connectionString,
                ["ConnectionStrings:SnCrMsSqlAuditLogStore"] = connectionString,
                ["ConnectionStrings:SignalRDatabase"] = connectionString,
                ["ConnectionStrings:SecurityDatabase"] = connectionString,
                ["sensenet:Security:SecurityDatabaseCommandTimeout"] = "120",
                ["logging:logLevel:default"] = "Warning"
            })
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Warning);
            })
            .AddSenseNet(configuration, (repositoryBuilder, provider) =>
            {
                repositoryBuilder.UseLogger(provider);
            })
            .AddEFCSecurityDataProvider(options =>
            {
                options.ConnectionString = connectionString;
            })
            .AddSenseNetMsSqlProviders();

        return services.BuildServiceProvider();
    }

    private static bool ValidateConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrWhiteSpace(builder.DataSource) &&
                   !string.IsNullOrWhiteSpace(builder.InitialCatalog);
        }
        catch
        {
            return false;
        }
    }
}