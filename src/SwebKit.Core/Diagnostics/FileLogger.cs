using Microsoft.Extensions.Logging;

namespace SwebKit.Core.Diagnostics;

/// <summary>
/// <see cref="ILogger"/> implementation bound to a <see cref="FileLoggerProvider"/> and a resolved
/// feature bucket. Never throws, never blocks the caller — entries are posted to the provider's
/// bounded channel via <c>TryWrite</c>.
/// </summary>
internal sealed class FileLogger : ILogger
{
    private readonly FileLoggerProvider _provider;
    private readonly string _categoryName;
    private readonly string _feature;
    private readonly AsyncLocal<Scope?> _currentScope = new();

    public FileLogger(FileLoggerProvider provider, string categoryName, string feature)
    {
        _provider = provider;
        _categoryName = categoryName;
        _feature = feature;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        var scope = new Scope(this, state, _currentScope.Value);
        _currentScope.Value = scope;
        return scope;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel == LogLevel.None)
            return false;

        var settings = _provider.Settings;
        return settings.Enabled && logLevel >= settings.MinimumLevel;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var rawMessage = formatter(state, exception) ?? string.Empty;
        var message = LogRedactor.Redact(rawMessage) ?? rawMessage;
        var exceptionText = exception is null ? null : LogEntry.SerializeAndRedactException(exception);
        var scopeState = BuildScopeState();

        var entry = new LogEntry(
            Timestamp: DateTimeOffset.Now,
            Level: logLevel,
            Category: _categoryName,
            Feature: _feature,
            EventId: eventId.Id,
            Message: message,
            Exception: exceptionText,
            ScopeState: scopeState);

        _provider.TryEnqueue(entry);
    }

    private IReadOnlyDictionary<string, string>? BuildScopeState()
    {
        Dictionary<string, string>? flattened = null;

        for (var scope = _currentScope.Value; scope is not null; scope = scope.Parent)
        {
            if (scope.State is not IEnumerable<KeyValuePair<string, object>> pairs)
                continue;

            flattened ??= new Dictionary<string, string>();
            foreach (var kvp in pairs)
            {
                var value = kvp.Value?.ToString();
                flattened.TryAdd(kvp.Key, LogRedactor.RedactScopeValue(kvp.Key, value) ?? string.Empty);
            }
        }

        return flattened is { Count: > 0 } ? flattened : null;
    }

    private sealed class Scope : IDisposable
    {
        private readonly FileLogger _logger;
        private bool _disposed;

        public Scope(FileLogger logger, object? state, Scope? parent)
        {
            _logger = logger;
            State = state;
            Parent = parent;
        }

        public object? State { get; }

        public Scope? Parent { get; }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _logger._currentScope.Value = Parent;
        }
    }
}
