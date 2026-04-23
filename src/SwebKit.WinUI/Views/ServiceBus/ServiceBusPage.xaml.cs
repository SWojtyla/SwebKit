using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.Core.Models;
using SwebKit.WinUI.ViewModels.ServiceBus;

namespace SwebKit.WinUI.Views.ServiceBus;

public sealed partial class ServiceBusPage : Page
{
    public ServiceBusPageViewModel ViewModel { get; }

    public ServiceBusPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ServiceBusPageViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        await ViewModel.DisposeAsync();
    }

    private async void SendActiveMessage_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var messageIdBox = new TextBox { Text = Guid.NewGuid().ToString() };
        var subjectBox = new TextBox();
        var correlationIdBox = new TextBox();
        var bodyBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            MinHeight = 180,
        };

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "Message ID" });
        content.Children.Add(messageIdBox);
        content.Children.Add(new TextBlock { Text = "Subject" });
        content.Children.Add(subjectBox);
        content.Children.Add(new TextBlock { Text = "Correlation ID" });
        content.Children.Add(correlationIdBox);
        content.Children.Add(new TextBlock { Text = "Body" });
        content.Children.Add(bodyBox);

        var dialog = new ContentDialog
        {
            Title = "Send message",
            Content = content,
            PrimaryButtonText = "Send",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var message = new SbMessage
        {
            MessageId = string.IsNullOrWhiteSpace(messageIdBox.Text) ? Guid.NewGuid().ToString() : messageIdBox.Text.Trim(),
            Subject = string.IsNullOrWhiteSpace(subjectBox.Text) ? null : subjectBox.Text.Trim(),
            CorrelationId = string.IsNullOrWhiteSpace(correlationIdBox.Text) ? null : correlationIdBox.Text.Trim(),
            Body = bodyBox.Text ?? string.Empty,
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            await ViewModel.SendActiveMessageAsync(message);
        }
        catch (Exception ex)
        {
            var errorDialog = new ContentDialog
            {
                Title = "Send failed",
                Content = ex.Message,
                CloseButtonText = "Close",
                XamlRoot = Content.XamlRoot,
            };

            await errorDialog.ShowAsync();
        }
    }
}