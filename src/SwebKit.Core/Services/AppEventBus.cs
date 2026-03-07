using SwebKit.Core.Abstractions;

namespace SwebKit.Core.Services;

public class AppEventBus : IAppEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    private readonly Lock _lock = new();

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
            ((Action<T>)h)(@event);
    }
}

// Event types
public record EnvironmentChangedEvent(Guid ProjectId, Guid EnvironmentId);
public record ProjectChangedEvent(Guid ProjectId);
public record CommandPaletteRequestedEvent;
public record NavigateToAreaEvent(string Area);
public record OpenEntityTabEvent(string Area, string EntityPath, string Title);
public record RefreshRequestedEvent(string Area);
