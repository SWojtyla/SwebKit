namespace SwebKit.App.Services;

/// <summary>
/// Platform-abstracted folder picker. Returns the selected folder path,
/// or <c>null</c> when the user cancels.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>Opens the native folder picker and returns the chosen path.</summary>
    Task<string?> PickFolderAsync(string title = "Select folder");
}
