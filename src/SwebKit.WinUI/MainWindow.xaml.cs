using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.Views.Aks;
using SwebKit.WinUI.Views.Dashboard;
using SwebKit.WinUI.Views.Observability;
using SwebKit.WinUI.Views.Pipelines;
using SwebKit.WinUI.Views.Redis;
using SwebKit.WinUI.Views.ServiceBus;
using SwebKit.WinUI.Views.Storage;
using SwebKit.WinUI.Views.IncidentTimeline;
using SwebKit.WinUI.ViewModels.Shell;
using SwebKit.WinUI.Views.Settings;
using SwebKit.WinUI.Views.Shell;
using Windows.System;

namespace SwebKit.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }
    public ShellChromeViewModel ShellChrome { get; }
    public CommandPaletteViewModel CommandPaletteViewModel { get; }
    private readonly ThemeCoordinator _themeCoordinator;
    private bool _isSyncingNavigationSelection;
    private string? _currentArea;
    private object? _currentNavigationParameter;

    public MainWindow(
        MainWindowViewModel viewModel,
        ShellChromeViewModel shellChrome,
        CommandPaletteViewModel commandPaletteViewModel,
        ThemeCoordinator themeCoordinator)
    {
        ViewModel = viewModel;
        ShellChrome = shellChrome;
        CommandPaletteViewModel = commandPaletteViewModel;
        _themeCoordinator = themeCoordinator;

        InitializeComponent();
        Title = "SwebKit";
        _themeCoordinator.AttachShellRoot(RootGrid);

        // Load persisted shell state (nav-pane expansion)
        ViewModel.LoadPersistedState();

        // Service-layer navigation requests drive the Frame
        ViewModel.NavigationRequested += NavigateToArea;

        // Ctrl+K — command palette
        RegisterCommandPaletteAccelerator();

        var initialArea = string.IsNullOrWhiteSpace(ViewModel.CurrentArea) ? "dashboard" : ViewModel.CurrentArea;
        ViewModel.OnAreaSelected(initialArea);
        NavigateToArea(initialArea);
    }

    private void ShellHeader_CommandPaletteRequested(object sender, EventArgs e)
    {
        _ = ShowCommandPaletteAsync();
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (_isSyncingNavigationSelection)
        {
            return;
        }

        if (e.SelectedItem is NavigationViewItem item && item.Tag is string area)
        {
            ViewModel.OnAreaSelected(area);
            NavigateToArea(area);
        }
    }

    private void NavigateToArea(string area, object? parameter = null)
    {
        // Sync NavigationView selection when navigation is triggered from service layer
        _isSyncingNavigationSelection = true;
        try
        {
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
        }
        finally
        {
            _isSyncingNavigationSelection = false;
        }

        var pageType = area switch
        {
            "dashboard" => typeof(DashboardPage),
            "aks" => typeof(AksPage),
            "incident-timeline" => typeof(IncidentTimelinePage),
            "observability" => typeof(ObservabilityPage),
            "pipelines" => typeof(PipelinesPage),
            "redis" => typeof(RedisPage),
            "service-bus" => typeof(ServiceBusPage),
            "settings" => typeof(SettingsPage),
            "storage" => typeof(StoragePage),
            _ => typeof(PlaceholderPage),
        };

        if (string.Equals(_currentArea, area, StringComparison.OrdinalIgnoreCase)
            && ContentFrame.CurrentSourcePageType == pageType
            && Equals(_currentNavigationParameter, parameter ?? area))
        {
            return;
        }

        _currentArea = area;
        _currentNavigationParameter = parameter ?? area;
        ContentFrame.Navigate(pageType, _currentNavigationParameter);
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
