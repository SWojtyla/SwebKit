using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.Views.Shared;

public static class DeferredPageLoadScheduler
{
    public static void ScheduleOnce(Page page, ref bool initialLoadScheduled, Func<Task> loadAsync)
    {
        if (initialLoadScheduled)
        {
            return;
        }

        initialLoadScheduled = true;

        RoutedEventHandler? handler = null;
        handler = async (sender, args) =>
        {
            page.Loaded -= handler;

            try
            {
                await Task.Yield();
                await loadAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Current.Services
                    .GetRequiredService<IShellErrorPresenter>()
                    .PresentPageActivationFailure(page.GetType().Name, ex);
            }
        };

        page.Loaded += handler;
    }
}