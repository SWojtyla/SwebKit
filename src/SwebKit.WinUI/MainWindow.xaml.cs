using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.Views.ServiceBus;
using SwebKit.WinUI.ViewModels.Shell;
using SwebKit.WinUI.Views.Settings;
using SwebKit.WinUI.Views.Shell;
using Windows.System;

namespace SwebKit.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }
    public CommandPaletteViewModel CommandPaletteViewModel { get; }

    public MainWindow(MainWindowViewModel viewModel, CommandPaletteViewModel commandPaletteViewModel)
    {
        ViewModel = viewModel;
        CommandPaletteViewModel = commandPaletteViewModel;

        InitializeComponent();
        Title = "SwebKit";

        // Load persisted shell state (nav-pane expansion)
        ViewModel.LoadPersistedState();

        // Service-layer navigation requests drive the Frame
        ViewModel.NavigationRequested += NavigateToArea;

        // Ctrl+K — command palette
        RegisterCommandPaletteAccelerator();
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavigationViewItem item && item.Tag is string area)
        {
            ViewModel.OnAreaSelected(area);
            NavigateToArea(area);
        }
    }

    private void NavigateToArea(string area)
    {
        // Sync NavigationView selection when navigation is triggered from service layer
        foreach (var menuItem in NavView.MenuItems)
        {
            if (menuItem is NavigationViewItem navItem && navItem.Tag as string == area)
            {
                NavView.SelectedItem = navItem;
                break;
            }
        }
        foreach (var footerItem in NavView.FooterMenuItems)
        {
            if (footerItem is NavigationViewItem navItem && navItem.Tag as string == area)
            {
                NavView.SelectedItem = navItem;
                break;
            }
        }

        var pageType = area switch
        {
            "service-bus" => typeof(ServiceBusPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(PlaceholderPage),
        };

        ContentFrame.Navigate(pageType, area);
    }

    // ── Command palette ────────────────────────────────────────────────────────

    private void RegisterCommandPaletteAccelerator()
    {
        try
        {
            var accelerator = new KeyboardAccelerator
            {
                Key = VirtualKey.K,
                Modifiers = VirtualKeyModifiers.Control,
            };
            accelerator.Invoked += (_, _) => _ = ShowCommandPaletteAsync();
            RootGrid.KeyboardAccelerators.Add(accelerator);
        }
        catch (Exception ex)
        {
            var errorPresenter = App.Current.Services.GetRequiredService<IShellErrorPresenter>();
            errorPresenter.PresentKeyboardShortcutRegistrationFailure(ex);
        }
    }

    private async Task ShowCommandPaletteAsync()
    {
        CommandPaletteViewModel.Refresh(string.Empty);

        var searchBox = new AutoSuggestBox
        {
            PlaceholderText = "Type a command\u2026",
            QueryIcon = new SymbolIcon(Symbol.Find),
            MinWidth = 480,
        };

        var commandList = new ListView
        {
            MaxHeight = 320,
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = CommandPaletteViewModel.Suggestions,
        };

        searchBox.TextChanged += (_, _) =>
        {
            CommandPaletteViewModel.Refresh(searchBox.Text);
            commandList.ItemsSource = CommandPaletteViewModel.Suggestions;
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(searchBox);
        panel.Children.Add(commandList);

        var dialog = new ContentDialog
        {
            Title = "Command Palette",
            Content = panel,
            PrimaryButtonText = "Run",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && commandList.SelectedItem is AppCommand command)
            await CommandPaletteViewModel.ExecuteCommand.ExecuteAsync(command);
    }
}
