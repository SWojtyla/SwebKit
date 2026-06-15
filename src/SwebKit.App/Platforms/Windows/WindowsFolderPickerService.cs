using SwebKit.App.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SwebKit.App.Platforms.Windows;

/// <summary>
/// Windows implementation of <see cref="IFolderPickerService"/> that uses the
/// WinRT <see cref="FolderPicker"/> API.
/// </summary>
internal sealed class WindowsFolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(string title = "Select folder")
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            CommitButtonText = title,
        };
        picker.FileTypeFilter.Add("*");

        // Initialize with the main window handle (required for unpackaged apps)
        var nativeWindow = Application.Current?.Windows[0]?.Handler?.PlatformView
            as Microsoft.UI.Xaml.Window;
        if (nativeWindow is not null)
        {
            var hwnd = WindowNative.GetWindowHandle(nativeWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
