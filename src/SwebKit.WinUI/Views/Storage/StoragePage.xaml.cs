using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.WinUI.ViewModels.Storage;
using SwebKit.WinUI.Views.Shared;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

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
        DeferredPageLoadScheduler.ScheduleOnce(this, ref _initialLoadScheduled, ViewModel.LoadAsync);
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        await ViewModel.DisposeAsync();
    }

    private async void UploadBlob_Click(object sender, RoutedEventArgs e)
    {
        await ShowUploadDialogAsync();
    }

    private async void CopyBlob_Click(object sender, RoutedEventArgs e)
    {
        await ShowCopyDialogAsync();
    }

    private async void EditMetadata_Click(object sender, RoutedEventArgs e)
    {
        await ShowMetadataDialogAsync();
    }

    private async Task ShowUploadDialogAsync()
    {
        if (!ViewModel.CanUploadToSelectedContainer)
        {
            return;
        }

        var file = await PickUploadFileAsync();
        if (file is null)
        {
            return;
        }

        var blobNameBox = new TextBox
        {
            Text = file.Name,
        };

        var contentTypeBox = new TextBox
        {
            Text = file.ContentType ?? string.Empty,
            PlaceholderText = "Content type (optional)",
        };

        var overwriteToggle = new ToggleSwitch
        {
            Header = "Overwrite if the blob already exists",
        };

        var statusText = CreateDialogStatusText();
        var content = new StackPanel
        {
            Spacing = 12,
        };

        AddLabeledElement(content, "Container", new TextBlock
        {
            Text = ViewModel.SelectedContainer?.Name ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
        });
        AddLabeledElement(content, "File", new TextBlock
        {
            Text = file.Path,
            TextWrapping = TextWrapping.Wrap,
        });
        AddLabeledElement(content, "Destination blob name", blobNameBox);
        AddLabeledElement(content, "Content type", contentTypeBox);
        content.Children.Add(overwriteToggle);
        content.Children.Add(statusText);

        var dialog = new ContentDialog
        {
            Title = "Upload blob",
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            PrimaryButtonText = "Upload",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();

            try
            {
                SetDialogStatus(statusText, null);

                await using var source = await file.OpenStreamForReadAsync();
                var result = await ViewModel.UploadBlobAsync(
                    blobNameBox.Text,
                    source,
                    overwriteToggle.IsOn,
                    string.IsNullOrWhiteSpace(contentTypeBox.Text) ? null : contentTypeBox.Text.Trim());

                if (!result.Success)
                {
                    args.Cancel = true;
                    SetDialogStatus(statusText, result.ErrorMessage);
                    return;
                }

                await ViewModel.RefreshBlobsCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                SetDialogStatus(statusText, ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private async Task ShowCopyDialogAsync()
    {
        if (!ViewModel.CanCopySelectedBlob || ViewModel.SelectedContainer is null || ViewModel.SelectedBlob is null)
        {
            return;
        }

        var destinationContainerBox = new ComboBox
        {
            PlaceholderText = "Select destination container",
            ItemsSource = ViewModel.Containers.Select(container => container.Name).ToList(),
        };

        var destinationBlobNameBox = new TextBox
        {
            Text = ViewModel.SelectedBlob.FullName,
        };

        var overwriteToggle = new ToggleSwitch
        {
            Header = "Overwrite if the destination blob already exists",
        };

        var statusText = CreateDialogStatusText();
        var content = new StackPanel
        {
            Spacing = 12,
        };

        AddLabeledElement(content, "Source", new TextBlock
        {
            Text = $"{ViewModel.SelectedContainer.Name}/{ViewModel.SelectedBlob.FullName}",
            TextWrapping = TextWrapping.Wrap,
        });
        AddLabeledElement(content, "Destination container", destinationContainerBox);
        AddLabeledElement(content, "Destination blob name", destinationBlobNameBox);
        content.Children.Add(overwriteToggle);
        content.Children.Add(statusText);

        var dialog = new ContentDialog
        {
            Title = "Copy blob",
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            PrimaryButtonText = "Copy",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();

            try
            {
                SetDialogStatus(statusText, null);

                var result = await ViewModel.CopySelectedBlobAsync(
                    destinationContainerBox.SelectedItem as string ?? string.Empty,
                    destinationBlobNameBox.Text,
                    overwriteToggle.IsOn);

                if (!result.Success)
                {
                    args.Cancel = true;
                    SetDialogStatus(statusText, result.ErrorMessage);
                    return;
                }

                if (string.Equals(destinationContainerBox.SelectedItem as string, ViewModel.SelectedContainer.Name, StringComparison.Ordinal))
                {
                    await ViewModel.RefreshBlobsCommand.ExecuteAsync(null);
                }
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                SetDialogStatus(statusText, ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private async Task ShowMetadataDialogAsync()
    {
        if (!ViewModel.CanEditSelectedBlobMetadata)
        {
            return;
        }

        var metadataBox = new TextBox
        {
            Text = BuildMetadataEditorText(),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 220,
            PlaceholderText = "key=value per line",
        };

        var helperText = new TextBlock
        {
            Text = "Enter one metadata entry per line in key=value format.",
            TextWrapping = TextWrapping.Wrap,
        };

        var statusText = CreateDialogStatusText();
        var content = new StackPanel
        {
            Spacing = 12,
        };

        content.Children.Add(helperText);
        AddLabeledElement(content, "Metadata", metadataBox);
        content.Children.Add(statusText);

        var dialog = new ContentDialog
        {
            Title = "Edit blob metadata",
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            PrimaryButtonText = "Save metadata",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();

            try
            {
                SetDialogStatus(statusText, null);

                if (!TryParseMetadata(metadataBox.Text, out var metadata, out var errorMessage))
                {
                    args.Cancel = true;
                    SetDialogStatus(statusText, errorMessage);
                    return;
                }

                var result = await ViewModel.SaveSelectedBlobMetadataAsync(metadata);
                if (!result.Success)
                {
                    args.Cancel = true;
                    SetDialogStatus(statusText, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                SetDialogStatus(statusText, ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private async Task<StorageFile?> PickUploadFileAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add("*");

        var mainWindow = App.Current.Services.GetRequiredService<MainWindow>();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(mainWindow));

        return await picker.PickSingleFileAsync();
    }

    private string BuildMetadataEditorText()
    {
        return string.Join(
            Environment.NewLine,
            ViewModel.MetadataRows.Select(row => $"{row.Key}={row.Value}"));
    }

    private static bool TryParseMetadata(string text, out Dictionary<string, string> metadata, out string? errorMessage)
    {
        metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        errorMessage = null;

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                errorMessage = $"Line {index + 1} must use key=value format.";
                return false;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                errorMessage = $"Line {index + 1} is missing a metadata key.";
                return false;
            }

            if (!metadata.TryAdd(key, value))
            {
                errorMessage = $"Metadata key '{key}' is duplicated.";
                return false;
            }
        }

        return true;
    }

    private static TextBlock CreateDialogStatusText() => new()
    {
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
    };

    private static void SetDialogStatus(TextBlock statusText, string? message)
    {
        statusText.Text = message ?? string.Empty;
        statusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static void AddLabeledElement(Panel parent, string label, UIElement element)
    {
        parent.Children.Add(new TextBlock { Text = label });
        parent.Children.Add(element);
    }
}