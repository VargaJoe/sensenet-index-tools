using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SenseNetIndexTools
{
    public class RebuildIndexCommand
    {
        public static Command Create()
        {
            var command = new Command("rebuild-index", "Rebuild index for specific content items using SenseNet API");

            var repoUrlOption = new Option<string>(
                name: "--repo-url",
                getDefaultValue: () => RuntimeSettings.Load().RepositoryUrl ?? "",
                description: "SenseNet repository URL (e.g., https://repository.example.invalid)");

            var apiKeyOption = new Option<string>(
                name: "--api-key",
                getDefaultValue: () => RuntimeSettings.Load().ApiKey,
                description: "API key (prefer a configured secret file)");

            var contentPathOption = new Option<string>(
                name: "--content-path",
                description: "Content path to rebuild index for (e.g., /Root/Sites/Default_Site)");

            var contentIdOption = new Option<string>(
                name: "--content-id",
                description: "Content ID to rebuild index for (numeric ID)");

            var filePathOption = new Option<string>(
                name: "--file-path",
                description: "Path to file containing list of content paths/IDs to process (one per line)");

            var recursiveOption = new Option<bool>(
                name: "--recursive",
                description: "Rebuild index recursively for child content",
                getDefaultValue: () => false);

            var rebuildLevelOption = new Option<string>(
                name: "--rebuild-level",
                description: "Rebuild level: IndexOnly, DatabaseOnly, or IndexAndDatabase",
                getDefaultValue: () => "IndexOnly");
            rebuildLevelOption.AddValidator(result =>
            {
                var value = result.GetValueForOption(rebuildLevelOption);
                if (value != "IndexOnly" && value != "DatabaseOnly" && value != "IndexAndDatabase")
                {
                    result.ErrorMessage = "Rebuild level must be IndexOnly, DatabaseOnly, or IndexAndDatabase";
                }
            });

            var verboseOption = new Option<bool>(
                name: "--verbose",
                description: "Enable verbose logging",
                getDefaultValue: () => false);

            command.AddOption(repoUrlOption);
            command.AddOption(apiKeyOption);
            command.AddOption(contentPathOption);
            command.AddOption(contentIdOption);
            command.AddOption(filePathOption);
            command.AddOption(recursiveOption);
            command.AddOption(rebuildLevelOption);
            command.AddOption(verboseOption);

            // Ensure either content-path, content-id, or file-path is provided
            command.AddValidator(result =>
            {
                if (string.IsNullOrWhiteSpace(result.GetValueForOption(repoUrlOption))) result.ErrorMessage = "Repository URL is required.";
                if (string.IsNullOrWhiteSpace(result.GetValueForOption(apiKeyOption)) && string.IsNullOrWhiteSpace(RuntimeSettings.Load().BearerToken)) result.ErrorMessage = "Configure an API key or bearer token secret file.";
                var contentPath = result.GetValueForOption(contentPathOption);
                var contentId = result.GetValueForOption(contentIdOption);
                var filePath = result.GetValueForOption(filePathOption);

                if (new[] { contentPath, contentId, filePath }.Count(v => !string.IsNullOrWhiteSpace(v)) != 1)
                {
                    result.ErrorMessage = "Specify exactly one of --content-path, --content-id, or --file-path";
                }
            });

            command.SetHandler(async (context) =>
            {
                var repoUrl = context.ParseResult.GetValueForOption(repoUrlOption)!;
                var apiKey = context.ParseResult.GetValueForOption(apiKeyOption)!;
                var contentPath = context.ParseResult.GetValueForOption(contentPathOption);
                var contentId = context.ParseResult.GetValueForOption(contentIdOption);
                var filePath = context.ParseResult.GetValueForOption(filePathOption);
                var recursive = context.ParseResult.GetValueForOption(recursiveOption);
                var rebuildLevel = context.ParseResult.GetValueForOption(rebuildLevelOption);
                var verbose = context.ParseResult.GetValueForOption(verboseOption);

                try
                {
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        // Process file with multiple items
                        await ProcessFileAsync(filePath!, repoUrl, apiKey, recursive, rebuildLevel!, verbose);
                    }
                    else
                    {
                        // Process single item
                        await RebuildIndexAsync(repoUrl, apiKey, contentPath, contentId, recursive, rebuildLevel!, verbose);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    if (verbose)
                    {
                        Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                    Environment.Exit(1);
                }
            });

            return command;
        }

        private static async Task ProcessFileAsync(string filePath, string repoUrl, string apiKey, bool recursive, string rebuildLevel, bool verbose)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var lines = await File.ReadAllLinesAsync(filePath);
            var processedCount = 0;
            var successCount = 0;
            var errorCount = 0;

            Console.WriteLine($"Processing {lines.Length} items from file: {filePath}");

            foreach (var line in lines)
            {
                var item = line.Trim();
                if (string.IsNullOrWhiteSpace(item) || item.StartsWith("#"))
                {
                    continue; // Skip empty lines and comments
                }

                processedCount++;
                Console.Write($"[{processedCount}] Processing: {item}... ");

                try
                {
                    string? contentPath = null;
                    string? contentId = null;

                    if (item.StartsWith("/Root"))
                    {
                        contentPath = item;
                    }
                    else if (int.TryParse(item, out _))
                    {
                        contentId = item;
                    }
                    else
                    {
                        Console.WriteLine("SKIP - Invalid format (not a path or numeric ID)");
                        errorCount++;
                        continue;
                    }

                    await RebuildIndexAsync(repoUrl, apiKey, contentPath, contentId, recursive, rebuildLevel, verbose);
                    Console.WriteLine("SUCCESS");
                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR - {ex.Message}");
                    errorCount++;
                }

                // Small delay to avoid overwhelming the server
                await Task.Delay(100);
            }

            Console.WriteLine($"\nProcessing complete:");
            Console.WriteLine($"  Total items: {processedCount}");
            Console.WriteLine($"  Successful: {successCount}");
            Console.WriteLine($"  Errors: {errorCount}");
            if (processedCount == 0 || errorCount > 0)
                throw new InvalidOperationException("Batch rebuild contained errors or no targets.");
        }

        private static async Task RebuildIndexAsync(string repoUrl, string apiKey, string? contentPath, string? contentId, bool recursive, string rebuildLevel, bool verbose)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SenseNet-Index-Tools/1.0");

            using var request = RebuildIndexRequest.Create(repoUrl, apiKey, contentPath, contentId, recursive, rebuildLevel, RuntimeSettings.Load().BearerToken);
            if (verbose) Console.WriteLine($"Request URL: {request.RequestUri}");
            using var response = await httpClient.SendAsync(request);
            if (verbose) Console.WriteLine($"Response status: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"API call failed with status {response.StatusCode}");
        }
    }
}
