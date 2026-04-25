using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Settings;

namespace SwebKit.WinUI.Views.Settings;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var request = e.Parameter switch
        {
            SettingsNavigationRequest navigationRequest => navigationRequest,
            string area when string.Equals(area, "settings", StringComparison.OrdinalIgnoreCase) => null,
            string section => new SettingsNavigationRequest(section),
            _ => null,
        };

        await ViewModel.LoadAsync(request);
        DevOpsPatBox.Password = ViewModel.DevOpsPat;
    }

    private void DevOpsPatBox_PasswordChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            ViewModel.DevOpsPat = passwordBox.Password;
        }
    }
}
