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

    public void Subscribe<T>(Func<T, Task> asyncHandler)
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = [];
                _handlers[typeof(T)] = list;
            }
            list.Add(asyncHandler);
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

    public void Unsubscribe<T>(Func<T, Task> asyncHandler)
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(typeof(T), out var list))
                list.Remove(asyncHandler);
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

    public async Task PublishAsync<T>(T @event)
    {
        List<Delegate>? handlers;
        lock (_lock)
        {
            _handlers.TryGetValue(typeof(T), out handlers);
            handlers = handlers?.ToList();
        }

        if (handlers is null) return;

        // Execute sync handlers first
        foreach (var h in handlers)
        {
            if (h is Action<T> syncHandler)
            {
                try
                {
                    syncHandler(@event);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sync event handler threw for event type {EventType}", typeof(T).Name);
                }
            }
        }

        // Execute async handlers concurrently
        var asyncTasks = new List<Task>();
        foreach (var h in handlers)
        {
            if (h is Func<T, Task> asyncHandler)
            {
                asyncTasks.Add(InvokeAsyncHandler(asyncHandler, @event));
            }
        }

        if (asyncTasks.Count > 0)
            await Task.WhenAll(asyncTasks);
    }

    private async Task InvokeAsyncHandler<T>(Func<T, Task> handler, T @event)
    {
        try
        {
            await handler(@event);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Async event handler threw for event type {EventType}", typeof(T).Name);
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
public record FocusFilterRequestedEvent(string Area);
