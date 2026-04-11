using Microsoft.Extensions.Logging;
using SwebKit.App.Services;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public sealed class ShellErrorPresenterTests
{
    [Fact]
    public void PresentBackgroundInitializationFailure_ShowsErrorNotificationAndStructuredLog()
    {
        var notifications = new NotificationService(new UiStateRepository());
        var logger = new TestLogger<ShellErrorPresenter>();
        var presenter = new ShellErrorPresenter(logger, notifications);

        presenter.PresentBackgroundInitializationFailure(new InvalidOperationException("config load exploded"));

        var notification = Assert.Single(notifications.All);
        Assert.Equal(NotificationSeverity.Error, notification.Severity);
        Assert.Equal("Shell startup is degraded", notification.Message);
        Assert.Contains("Background initialization could not finish", notification.Detail);
        Assert.Contains("config load exploded", notification.Detail);

        var log = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.Equal("BackgroundInitialization", log.Properties["ShellOperation"]);
        Assert.Equal("Shell startup is degraded", log.Properties["UserImpact"]);
    }

    [Fact]
    public void PresentKeyboardShortcutRegistrationFailure_ShowsWarningNotificationAndStructuredLog()
    {
        var notifications = new NotificationService(new UiStateRepository());
        var logger = new TestLogger<ShellErrorPresenter>();
        var presenter = new ShellErrorPresenter(logger, notifications);

        presenter.PresentKeyboardShortcutRegistrationFailure(new InvalidOperationException("interop unavailable"));

        var notification = Assert.Single(notifications.All);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
        Assert.Equal("Keyboard shortcuts are unavailable", notification.Message);
        Assert.Contains("keyboard shortcuts could not be registered", notification.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("interop unavailable", notification.Detail);

        var log = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Equal("KeyboardShortcutRegistration", log.Properties["ShellOperation"]);
        Assert.Equal("Keyboard shortcuts are unavailable", log.Properties["UserImpact"]);
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> keyValuePairs
                ? keyValuePairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception, properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}