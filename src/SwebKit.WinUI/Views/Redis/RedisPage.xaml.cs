using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Redis;

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