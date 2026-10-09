using System.CommandLine;

namespace SenseNetIndexTools;

public static class SnapshotCommand
{
    public static Command Create()
    {
        var command = new Command("snapshot", "Capture and verify one Lucene commit from a read-only source");
        var source = new Option<string>("--path", () => RuntimeSettings.Load().SourcePath ?? "", "Index or parent containing dated indexes");
        var output = new Option<string>("--output", () => RuntimeSettings.Load().SnapshotDirectory ?? "IndexCopies", "Parent directory for isolated copies");
        command.AddOption(source); command.AddOption(output);
        command.AddValidator(result => { if (string.IsNullOrWhiteSpace(result.GetValueForOption(source))) result.ErrorMessage = "Provide --path or INDEXTOOLS_SOURCE_PATH."; });
        command.SetHandler(context => {
            try { Console.WriteLine(IndexSnapshot.Create(context.ParseResult.GetValueForOption(source)!, context.ParseResult.GetValueForOption(output)!)); }
            catch (Exception ex) { Console.Error.WriteLine(SecretRedactor.Redact(ex.Message)); context.ExitCode = 1; }
        });
        return command;
    }
}
