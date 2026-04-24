using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SwebKit.WinUI.ViewModels.Shell;

namespace SwebKit.WinUI.Controls.Shell;

public sealed partial class ShellContextHeader : UserControl
{
    public event EventHandler? CommandPaletteRequested;

    public ShellContextHeader()
    {
        InitializeComponent();
    }

    private void CommandPaletteButton_Click(object sender, RoutedEventArgs e)
    {
        CommandPaletteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NotificationFlyout_Opening(object sender, object e)
    {
        if (DataContext is ShellChromeViewModel viewModel)
        {
            viewModel.MarkNotificationsSeen();
        }
    }
}