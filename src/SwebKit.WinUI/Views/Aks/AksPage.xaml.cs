using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Aks;
using SwebKit.WinUI.Views.Shared;
using Windows.System;
using Windows.UI.Core;

namespace SwebKit.WinUI.Views.Aks;

public sealed partial class AksPage : Page
{
    private bool _initialLoadScheduled;
    private const VirtualKey SlashKey = (VirtualKey)191;

    public AksPageViewModel ViewModel { get; }

    public AksPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<AksPageViewModel>();
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

    private async void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || HasModifierKeys() || IsTextInputOrigin(e.OriginalSource))
        {
            return;
        }

        if (e.Key is VirtualKey.Divide or SlashKey)
        {
            ResourceFilterTextBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            e.Handled = true;
            return;
        }

        var shortcut = e.Key switch
        {
            VirtualKey.Enter => "Enter",
            VirtualKey.Escape => "Escape",
            VirtualKey.L => "l",
            VirtualKey.N => "n",
            VirtualKey.Y => "y",
            VirtualKey.R => "r",
            VirtualKey.S => "s",
            VirtualKey.P => "p",
            VirtualKey.D => "d",
            VirtualKey.I => "i",
            VirtualKey.H => "h",
            VirtualKey.V => "v",
            _ => null,
        };

        if (shortcut is null)
        {
            return;
        }

        if (await ViewModel.HandleKeyboardShortcutAsync(shortcut))
        {
            e.Handled = true;
        }
    }

    private static bool HasModifierKeys()
    {
        return IsKeyDown(VirtualKey.Control)
            || IsKeyDown(VirtualKey.Menu)
            || IsKeyDown(VirtualKey.LeftWindows)
            || IsKeyDown(VirtualKey.RightWindows);
    }

    private static bool IsKeyDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static bool IsTextInputOrigin(object? source)
    {
        return source is TextBox
            || source is AutoSuggestBox
            || source is RichEditBox
            || source is PasswordBox
            || source is ComboBox
            || source is ComboBoxItem;
    }
}