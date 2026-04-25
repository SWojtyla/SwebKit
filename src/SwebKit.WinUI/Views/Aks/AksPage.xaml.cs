using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Aks;
using SwebKit.WinUI.Views.Shared;

namespace SwebKit.WinUI.Views.Aks;

public sealed partial class AksPage : Page
{
    private bool _initialLoadScheduled;

    public AksPageViewModel ViewModel { get; }

    public AksPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<AksPageViewModel>();
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