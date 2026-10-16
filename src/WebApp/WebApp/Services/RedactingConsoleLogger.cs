using SenseNetIndexTools;

namespace WebApp.Services;

public sealed class RedactingConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName);
    public void Dispose() { }
    private sealed class Logger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) Console.WriteLine(SecretRedactor.Redact($"{level}: {category}: {formatter(state, exception)} {exception}"));
        }
    }
}
