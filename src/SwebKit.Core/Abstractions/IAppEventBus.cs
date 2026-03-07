namespace SwebKit.Core.Abstractions;

public interface IAppEventBus
{
    void Subscribe<T>(Action<T> handler);
    void Unsubscribe<T>(Action<T> handler);
    void Publish<T>(T @event);
}
