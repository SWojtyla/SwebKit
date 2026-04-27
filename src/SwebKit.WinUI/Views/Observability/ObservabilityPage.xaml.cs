using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Observability;
using SwebKit.WinUI.Views.Shared;
using Windows.System;
using Windows.UI.Core;

namespace SwebKit.WinUI.Views.Observability;

public sealed partial class ObservabilityPage : Page
{
    private bool _initialLoadScheduled;

    public ObservabilityPageViewModel ViewModel { get; }

    public ObservabilityPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ObservabilityPageViewModel>();

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

    private async void OnAdvancedQueryTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || e.Key != VirtualKey.Enter || !IsKeyDown(VirtualKey.Control))
        {
            return;
        }

        e.Handled = true;
        await ViewModel.RunLogsQueryCommand.ExecuteAsync(null);
    }

    private static bool IsKeyDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}