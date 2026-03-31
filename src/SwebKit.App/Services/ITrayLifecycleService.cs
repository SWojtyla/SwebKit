namespace SwebKit.App.Services;

public interface ITrayLifecycleService : IDisposable
{
    void Initialize(Window window);
}
