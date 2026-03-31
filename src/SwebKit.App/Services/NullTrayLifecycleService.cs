namespace SwebKit.App.Services;

internal sealed class NullTrayLifecycleService : ITrayLifecycleService
{
    public void Initialize(Window window)
    {
        _ = window;
    }

    public void Dispose()
    {
    }
}
