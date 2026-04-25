using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Redis;
using SwebKit.WinUI.Views.Shared;

namespace SwebKit.WinUI.Views.Redis;

public sealed partial class RedisPage : Page
{
    private bool _initialLoadScheduled;

    public RedisPageViewModel ViewModel { get; }

    public RedisPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<RedisPageViewModel>();
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