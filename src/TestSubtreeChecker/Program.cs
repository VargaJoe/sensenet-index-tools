using System.Diagnostics;

namespace TestSubtreeChecker;

public class SubtreeCheckerTester
{
    public static async Task<int> Main(string[] args)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "sensenet-index-tools.sln"))) root = root.Parent;
        if (root == null) { Console.Error.WriteLine("Run the regression suite from the source checkout: dotnet test test/IndexTools.Tests/IndexTools.Tests.csproj"); return 1; }
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = root.FullName };
        foreach (var argument in new[] { "test", "test/IndexTools.Tests/IndexTools.Tests.csproj", "--filter", "FullyQualifiedName~ComparisonTests", "--nologo" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
