using System.CommandLine;
using SenseNetIndexTools.Native;

var rootCommand = new RootCommand("SenseNet Index Creation Tool - Using SenseNet's Real Indexing Engine");

var connectionStringOption = new Option<string>(
    name: "--connection-string",
    description: "SQL Connection string to the SenseNet database");
connectionStringOption.IsRequired = true;

var repositoryPathOption = new Option<string>(
    name: "--repository-path",
    description: "Path in the content repository to index (e.g., /Root)",
    getDefaultValue: () => "/Root");

var outputPathOption = new Option<string?>(
    name: "--output-path",
    description: "Directory where the new index will be created");

var recursiveOption = new Option<bool>(
    name: "--recursive",
    description: "Recursively process all content items under the specified path",
    getDefaultValue: () => true);

var batchSizeOption = new Option<int>(
    name: "--batch-size",
    description: "Number of items to process in each batch",
    getDefaultValue: () => 100);

var maxItemsOption = new Option<int>(
    name: "--max-items",
    description: "Maximum number of items to index (0 = no limit)",
    getDefaultValue: () => 0);

var createSubfolderOption = new Option<bool>(
    name: "--create-subfolder",
    description: "Create timestamped subfolder for the index",
    getDefaultValue: () => false);

var formatOption = new Option<string>(
    name: "--format",
    description: "Report output format: md, html",
    getDefaultValue: () => "md");

var outputReportOption = new Option<string?>(
    name: "--output-report",
    description: "Save the report to a file");

var forceOption = new Option<bool>(
    name: "--force-reindex",
    description: "Force recreation of index if it already exists",
    getDefaultValue: () => false);

var verboseOption = new Option<bool>(
    name: "--verbose",
    description: "Enable verbose logging",
    getDefaultValue: () => false);

rootCommand.AddOption(connectionStringOption);
rootCommand.AddOption(repositoryPathOption);
rootCommand.AddOption(outputPathOption);
rootCommand.AddOption(recursiveOption);
rootCommand.AddOption(batchSizeOption);
rootCommand.AddOption(maxItemsOption);
rootCommand.AddOption(createSubfolderOption);
rootCommand.AddOption(formatOption);
rootCommand.AddOption(outputReportOption);
rootCommand.AddOption(forceOption);
rootCommand.AddOption(verboseOption);

rootCommand.SetHandler(async (context) =>
{
    try
    {
        var options = new IndexCreationOptions
        {
            ConnectionString = context.ParseResult.GetValueForOption(connectionStringOption)!,
            RepositoryPath = context.ParseResult.GetValueForOption(repositoryPathOption)!,
            OutputPath = context.ParseResult.GetValueForOption(outputPathOption),
            Recursive = context.ParseResult.GetValueForOption(recursiveOption),
            BatchSize = context.ParseResult.GetValueForOption(batchSizeOption),
            MaxItems = context.ParseResult.GetValueForOption(maxItemsOption),
            CreateSubfolder = context.ParseResult.GetValueForOption(createSubfolderOption),
            OutputFormat = context.ParseResult.GetValueForOption(formatOption)!,
            ForceReindex = context.ParseResult.GetValueForOption(forceOption),
            Verbose = context.ParseResult.GetValueForOption(verboseOption)
        };

        var outputReport = context.ParseResult.GetValueForOption(outputReportOption);

        Console.WriteLine("🔬 SenseNet Index Creation Tool (Using SenseNet's Real Indexing Engine)");
        Console.WriteLine("====================================================================");
        Console.WriteLine($"Repository: {options.RepositoryPath}");
        Console.WriteLine($"Connection: {options.ConnectionString.Substring(0, Math.Min(50, options.ConnectionString.Length))}...");
        Console.WriteLine($"Output: {options.OutputPath ?? "Default location"}");
        Console.WriteLine();

        var result = await SenseNetRealIndexCreator.CreateIndexAsync(options);

        if (result.Success)
        {
            Console.WriteLine("✅ Index creation completed successfully!");
            Console.WriteLine($"📁 Index path: {result.IndexPath}");
            Console.WriteLine($"📊 Processed items: {result.ProcessedItemCount:N0}");
            Console.WriteLine($"⏱️ Duration: {result.Duration.TotalSeconds:F1} seconds");
            Console.WriteLine();
            Console.WriteLine("Details:");
            Console.WriteLine(result.Message);

            if (!string.IsNullOrEmpty(outputReport) && !string.IsNullOrEmpty(result.ReportContent))
            {
                await File.WriteAllTextAsync(outputReport, result.ReportContent);
                Console.WriteLine($"📄 Report saved to: {outputReport}");
            }
        }
        else
        {
            Console.Error.WriteLine("❌ Index creation failed!");
            Console.Error.WriteLine($"Error: {result.Message}");
            Environment.Exit(1);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"❌ Fatal error: {ex.Message}");
        if (context.ParseResult.GetValueForOption(verboseOption))
        {
            Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        Environment.Exit(1);
    }
});

return await rootCommand.InvokeAsync(args);