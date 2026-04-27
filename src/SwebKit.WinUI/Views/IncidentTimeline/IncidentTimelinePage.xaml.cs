using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.Core.Models;
using SwebKit.WinUI.ViewModels.IncidentTimeline;
using SwebKit.WinUI.Views.Shared;

namespace SwebKit.WinUI.Views.IncidentTimeline;

public sealed partial class IncidentTimelinePage : Page
{
    private bool _initialLoadScheduled;

    public IncidentTimelinePageViewModel ViewModel { get; }

    public IncidentTimelinePage()
    {
        ViewModel = App.Current.Services.GetRequiredService<IncidentTimelinePageViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DeferredPageLoadScheduler.ScheduleOnce(
            this,
            ref _initialLoadScheduled,
            () => ViewModel.LoadAsync(e.Parameter as IncidentInvestigationSeed));
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        await ViewModel.DisposeAsync();
    }
}