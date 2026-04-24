using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Storage;

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