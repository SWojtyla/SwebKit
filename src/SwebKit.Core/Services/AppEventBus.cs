using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;

namespace SwebKit.Core.Services;

public class AppEventBus : IAppEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    private readonly Lock _lock = new();
    private readonly ILogger<AppEventBus> _logger;

    public AppEventBus(ILogger<AppEventBus> logger)
    {
        _logger = logger;
    }

    public void Subscribe<T>(Action<T> handler)
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = [];
                _handlers[typeof(T)] = list;
            }
            list.Add(handler);
        }
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(typeof(T), out var list))
                list.Remove(handler);
        }
    }

    public void Publish<T>(T @event)
    {
        List<Delegate>? handlers;
        lock (_lock)
        {
            _handlers.TryGetValue(typeof(T), out handlers);
            handlers = handlers?.ToList();
        }

        if (handlers is null) return;
        foreach (var h in handlers)
        {
            try
            {
                ((Action<T>)h)(@event);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event handler threw for event type {EventType}", typeof(T).Name);
            }
        }
    }
}

// Event types
public record CommandPaletteRequestedEvent;
public record NavigateToAreaEvent(string Area);
public record OpenEntityTabEvent(string Area, string EntityPath, string Title);
public record RefreshRequestedEvent(string Area);
public record ServiceBusShortcutEvent(string Action);
public record PortForwardSessionsChangedEvent;
public record OpenPortForwardPanelEvent;
public record ConnectionStateChangedEvent(string Area);
public record ActivityEvent(string Description, string Icon, string Area, DateTimeOffset OccurredAt)
{
    public ActivityEvent(string description, string icon, string area)
        : this(description, icon, area, DateTimeOffset.Now) { }
}
