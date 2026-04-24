using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Aks;

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