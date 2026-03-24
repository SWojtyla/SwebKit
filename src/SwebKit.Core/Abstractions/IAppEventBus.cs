namespace SwebKit.Core.Abstractions;

public interface IAppEventBus
{
    void Subscribe<T>(Action<T> handler);
    void Subscribe<T>(Func<T, Task> asyncHandler);
    void Unsubscribe<T>(Action<T> handler);
    void Unsubscribe<T>(Func<T, Task> asyncHandler);
    void Publish<T>(T @event);
    Task PublishAsync<T>(T @event);
}
