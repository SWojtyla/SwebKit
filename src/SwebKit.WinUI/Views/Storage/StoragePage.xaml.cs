using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Storage;
using SwebKit.WinUI.Views.Shared;

namespace SwebKit.WinUI.Views.Storage;

public sealed partial class StoragePage : Page
{
    private bool _initialLoadScheduled;

    public StoragePageViewModel ViewModel { get; }

    public StoragePage()
    {
        ViewModel = App.Current.Services.GetRequiredService<StoragePageViewModel>();

        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DeferredPageLoadScheduler.ScheduleOnce(this, ref _initialLoadScheduled, ViewModel.LoadAsync);
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        await ViewModel.DisposeAsync();
    }
}