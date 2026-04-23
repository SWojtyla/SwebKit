using Microsoft.UI.Xaml;

namespace SwebKit.WinUI.Services;

public interface ITrayLifecycleService : IDisposable
{
    void Initialize(Window window);
}
