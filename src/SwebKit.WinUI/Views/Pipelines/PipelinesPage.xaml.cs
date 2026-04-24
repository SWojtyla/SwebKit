using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Pipelines;

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

        if (_initialLoadScheduled)
        {
            return;
        }

        _initialLoadScheduled = true;
        Loaded += HandleInitialPageLoadAsync;
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        await ViewModel.DisposeAsync();
    }

    private async void HandleInitialPageLoadAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= HandleInitialPageLoadAsync;

        await Task.Yield();
        await ViewModel.LoadAsync();
    }
}