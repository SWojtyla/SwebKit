using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Pipelines;
using SwebKit.WinUI.Views.Shared;

namespace SwebKit.WinUI.Views.Pipelines;

public sealed partial class PipelinesPage : Page
{
    private bool _initialLoadScheduled;

    public PipelinesPageViewModel ViewModel { get; }

    public PipelinesPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<PipelinesPageViewModel>();
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