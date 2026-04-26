using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.ServiceBus;
using SwebKit.WinUI.ViewModels.Settings;
using SwebKit.WinUI.Views.Settings;
using Windows.ApplicationModel.DataTransfer;

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

        try
        {
            await ViewModel.LoadAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.Current.Services
                .GetRequiredService<IShellErrorPresenter>()
                .PresentPageActivationFailure(nameof(ServiceBusPage), ex);
        }
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        await ViewModel.DisposeAsync();
    }

    private void ManageNamespaces_Click(object sender, RoutedEventArgs e)
    {
        App.Current.Services
            .GetRequiredService<IShellNavigationService>()
            .NavigateTo("settings", new SettingsNavigationRequest(SettingsSections.ServiceBus));
    }

    private async void SendActiveMessage_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is null)
        {
            return;
        }

        var draft = ViewModel.CreateComposeDraft();
        var dialog = CreateComposeDialog(draft);
        await dialog.ShowAsync();
    }

    private async void BatchSendActiveMessages_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is not { IsDlq: false, IsScheduled: false } activeTab)
        {
            return;
        }

        await ShowBatchSendDialogAsync(activeTab);
    }

    private async void EditSelectedMessage_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedMessage() is not { } selectedMessage)
        {
            return;
        }

        var draft = ViewModel.CreateComposeDraftFromMessage(selectedMessage);
        var dialog = CreateComposeDialog(draft);
        await dialog.ShowAsync();
    }

    private async void ReplaySelectedMessage_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is not { SupportsReplay: true })
        {
            return;
        }

        if (TryGetSelectedMessage() is not { } selectedMessage)
        {
            return;
        }

        var draft = ViewModel.CreateReplayDraftFromMessage(selectedMessage);
        var dialog = CreateComposeDialog(draft);
        await dialog.ShowAsync();
    }

    private async void ScheduleSelectedMessage_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedMessage() is not { } selectedMessage)
        {
            return;
        }

        var draft = ViewModel.CreateComposeDraftFromMessage(selectedMessage, scheduleForLater: true);
        var dialog = CreateComposeDialog(draft);
        await dialog.ShowAsync();
    }

    private async void SaveSelectedMessageAsTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedMessage() is not { } selectedMessage)
        {
            return;
        }

        var draft = ViewModel.CreateComposeDraftFromMessage(selectedMessage);
        await ShowSaveTemplateDialogAsync(draft, selectedMessage.Subject ?? "Untitled template");
    }

    private void CopySelectedMessageBody_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedMessage() is not { } selectedMessage)
        {
            return;
        }

        CopyToClipboard(selectedMessage.Body);
        GetNotificationService().ShowSuccess("Message body copied", selectedMessage.MessageId);
    }

    private void CopySelectedMessageJson_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedMessage() is not { } selectedMessage)
        {
            return;
        }

        CopyToClipboard(SerializeFullMessage(selectedMessage));
        GetNotificationService().ShowSuccess("Full message copied", selectedMessage.MessageId);
    }

    private void FilterSelectedMessageSession_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab?.SelectedMessage?.SessionId is not { Length: > 0 } sessionId)
        {
            return;
        }

        ViewModel.ActiveTab.FilterText = sessionId;
        GetNotificationService().ShowSuccess("Filtered to session", sessionId);
    }

    private async void ResubmitSelectedDeadLetter_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab?.SelectedMessage?.SequenceNumber is not long)
        {
            return;
        }

        var request = ServiceBusPagePresentation.BuildResubmitDeadLetterConfirmation(ViewModel.ActiveTab);

        await ExecuteConfirmedActionAsync(
            title: request.Title,
            message: request.Message,
            primaryButtonText: request.PrimaryButtonText,
            failureTitle: request.FailureTitle,
            action: () => ViewModel.ResubmitSelectedDeadLetterCommand.ExecuteAsync(null));
    }

    private async void BatchReplaySelectedDeadLetters_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is not { CanBatchReplayDeadLetter: true } activeTab)
        {
            return;
        }

        await ShowBatchDeadLetterReplayDialogAsync(activeTab);
    }

    private async void CompleteSelectedDeadLetter_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab?.SelectedMessage?.SequenceNumber is not long)
        {
            return;
        }

        var request = ServiceBusPagePresentation.BuildCompleteDeadLetterConfirmation(ViewModel.ActiveTab);

        await ExecuteConfirmedActionAsync(
            title: request.Title,
            message: request.Message,
            primaryButtonText: request.PrimaryButtonText,
            failureTitle: request.FailureTitle,
            action: () => ViewModel.CompleteSelectedDeadLetterCommand.ExecuteAsync(null));
    }

    private async void DeleteFilteredMessages_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is not { IsMessageTab: true } activeTab || activeTab.VisibleMessages.Count == 0)
        {
            return;
        }

        var title = activeTab.IsDlq ? "Complete filtered dead-letter messages" : "Delete filtered messages";
        var message = activeTab.IsDlq
            ? $"Complete {activeTab.VisibleMessages.Count} currently visible dead-letter message(s) from {activeTab.EntityPath}? This removes them from the dead-letter queue."
            : $"Delete {activeTab.VisibleMessages.Count} currently visible message(s) from {activeTab.EntityPath}?";
        var primaryButtonText = activeTab.IsDlq ? "Complete filtered" : "Delete filtered";

        if (!await ShowConfirmationDialogAsync(title, message, primaryButtonText))
        {
            return;
        }

        try
        {
            var deleted = await ViewModel.DeleteFilteredMessagesAsync();
            GetNotificationService().ShowSuccess(
                activeTab.IsDlq ? "Filtered dead-letter messages completed" : "Filtered messages deleted",
                $"{deleted} message(s) processed.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await ShowMessageDialogAsync("Delete filtered messages failed", ex.Message);
        }
    }

    private async void PurgeAllMessages_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is not { IsMessageTab: true } activeTab)
        {
            return;
        }

        var title = activeTab.IsDlq ? "Purge dead-letter queue" : "Purge active messages";
        var message = activeTab.IsDlq
            ? $"Permanently remove all dead-letter messages from {activeTab.EntityPath}?"
            : $"Permanently remove all active messages from {activeTab.EntityPath}?";

        if (!await ShowConfirmationDialogAsync(title, message, "Purge all"))
        {
            return;
        }

        try
        {
            var deleted = await ViewModel.PurgeActiveTabMessagesAsync();
            GetNotificationService().ShowSuccess("Purge complete", $"{deleted} message(s) removed.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await ShowMessageDialogAsync("Purge failed", ex.Message);
        }
    }

    private async void ExportVisibleMessages_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is not { IsMessageTab: true } activeTab || activeTab.VisibleMessages.Count == 0)
        {
            return;
        }

        try
        {
            var json = ViewModel.ExportVisibleMessagesAsJson();
            var destinationPath = BuildExportPath($"servicebus-{SanitizeFileName(activeTab.EntityPath)}-{(activeTab.IsDlq ? "dlq" : "active")}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(destinationPath, json);
            GetNotificationService().ShowSuccess("Export complete", destinationPath);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await ShowMessageDialogAsync("Export failed", ex.Message);
        }
    }

    private async void CancelScheduledMessage_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetScheduledMessage(sender) is not { } scheduledMessage)
        {
            return;
        }

        var request = ServiceBusPagePresentation.BuildCancelScheduledConfirmation(scheduledMessage);

        await ExecuteConfirmedActionAsync(
            title: request.Title,
            message: request.Message,
            primaryButtonText: request.PrimaryButtonText,
            failureTitle: request.FailureTitle,
            action: () => ViewModel.CancelScheduledMessageCommand.ExecuteAsync(scheduledMessage));
    }

    private async void RemoveScheduledMessage_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetScheduledMessage(sender) is not { } scheduledMessage)
        {
            return;
        }

        var request = ServiceBusPagePresentation.BuildRemoveScheduledConfirmation(scheduledMessage);

        await ExecuteConfirmedActionAsync(
            title: request.Title,
            message: request.Message,
            primaryButtonText: request.PrimaryButtonText,
            failureTitle: request.FailureTitle,
            action: () => ViewModel.RemoveScheduledMessageCommand.ExecuteAsync(scheduledMessage));
    }

    private ContentDialog CreateComposeDialog(ServiceBusComposeDraft draft)
    {
        var targetEntityText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
        };

        var templateSummaryText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
        };

        var statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var templatePicker = new ComboBox
        {
            PlaceholderText = "Select a saved template",
            DisplayMemberPath = nameof(SbMessageTemplate.Name),
        };

        var applyTemplateButton = new Button
        {
            Content = "Apply",
        };

        var templateNameBox = new TextBox
        {
            PlaceholderText = "Template name",
        };

        var saveTemplateButton = new Button
        {
            Content = "Save current",
        };

        var messageIdBox = new TextBox();
        var subjectBox = new TextBox();
        var correlationIdBox = new TextBox();
        var contentTypeBox = new TextBox();
        var propertiesBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 100,
            PlaceholderText = "key=value per line",
        };

        var bodyBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 220,
        };

        var replayNamespacePicker = new ComboBox
        {
            DisplayMemberPath = nameof(ComposeNamespaceOption.Label),
        };

        var replayTargetEntityBox = new TextBox();

        var replayOverrideSubjectBox = new TextBox
        {
            PlaceholderText = "Keep original subject",
        };

        var replayOverrideCorrelationIdBox = new TextBox
        {
            PlaceholderText = "Keep original correlation ID",
        };

        var replayPropertyRenamesBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            PlaceholderText = "oldKey=newKey per line",
        };

        var replayPropertyRemovesBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            PlaceholderText = "propertyKey per line",
        };

        var replayTargetPanel = new StackPanel
        {
            Spacing = 12,
            Visibility = Visibility.Collapsed,
        };

        var replayTargetRow = new Grid
        {
            ColumnSpacing = 8,
        };
        replayTargetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        replayTargetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var replayNamespacePanel = new StackPanel { Spacing = 4 };
        replayNamespacePanel.Children.Add(new TextBlock { Text = "Target namespace" });
        replayNamespacePanel.Children.Add(replayNamespacePicker);
        Grid.SetColumn(replayNamespacePanel, 0);

        var replayEntityPanel = new StackPanel { Spacing = 4 };
        replayEntityPanel.Children.Add(new TextBlock { Text = "Target entity" });
        replayEntityPanel.Children.Add(replayTargetEntityBox);
        Grid.SetColumn(replayEntityPanel, 1);

        replayTargetRow.Children.Add(replayNamespacePanel);
        replayTargetRow.Children.Add(replayEntityPanel);

        var replayRemapPanel = new StackPanel { Spacing = 12 };
        AddLabeledControl(replayRemapPanel, "Override subject", replayOverrideSubjectBox);
        AddLabeledControl(replayRemapPanel, "Override correlation ID", replayOverrideCorrelationIdBox);
        AddLabeledControl(replayRemapPanel, "Property renames", replayPropertyRenamesBox);
        AddLabeledControl(replayRemapPanel, "Property removes", replayPropertyRemovesBox);

        replayTargetPanel.Children.Add(replayTargetRow);
        replayTargetPanel.Children.Add(new Expander
        {
            Header = "Remap rules (optional)",
            Content = replayRemapPanel,
        });

        var scheduleToggle = new ToggleSwitch
        {
            Header = "Schedule for later",
        };

        var scheduleDatePicker = new DatePicker();
        var scheduleTimePicker = new TimePicker();

        var schedulePanel = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
        };

        AddLabeledControl(schedulePanel, "Scheduled date", scheduleDatePicker);
        AddLabeledControl(schedulePanel, "Scheduled time", scheduleTimePicker);

        var templateRow = new Grid
        {
            ColumnSpacing = 8,
        };
        templateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        templateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(templatePicker, 0);
        Grid.SetColumn(applyTemplateButton, 1);
        templateRow.Children.Add(templatePicker);
        templateRow.Children.Add(applyTemplateButton);

        var templateSaveRow = new Grid
        {
            ColumnSpacing = 8,
        };
        templateSaveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        templateSaveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(templateNameBox, 0);
        Grid.SetColumn(saveTemplateButton, 1);
        templateSaveRow.Children.Add(templateNameBox);
        templateSaveRow.Children.Add(saveTemplateButton);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(targetEntityText);
        content.Children.Add(new TextBlock { Text = "Saved templates" });
        content.Children.Add(templateSummaryText);
        content.Children.Add(templateRow);
        content.Children.Add(new TextBlock { Text = "Save current payload as template" });
        content.Children.Add(templateSaveRow);
        content.Children.Add(replayTargetPanel);
        content.Children.Add(statusText);
        AddLabeledControl(content, "Message ID", messageIdBox);
        AddLabeledControl(content, "Subject", subjectBox);
        AddLabeledControl(content, "Correlation ID", correlationIdBox);
        AddLabeledControl(content, "Content-Type", contentTypeBox);
        AddLabeledControl(content, "Application properties", propertiesBox);
        AddLabeledControl(content, "Body", bodyBox);
        content.Children.Add(scheduleToggle);
        content.Children.Add(schedulePanel);

        var replayNamespaceOptions = ViewModel.Namespaces
            .Where(namespaceItem => namespaceItem.Client is not null && namespaceItem.Namespace.Id != ViewModel.ActiveTab?.NamespaceId)
            .OrderBy(namespaceItem => namespaceItem.Alias, StringComparer.OrdinalIgnoreCase)
            .Select(namespaceItem => new ComposeNamespaceOption(namespaceItem.Namespace.Id, namespaceItem.Alias))
            .Prepend(new ComposeNamespaceOption(null, "Same namespace"))
            .ToList();
        replayNamespacePicker.ItemsSource = replayNamespaceOptions;

        var dialog = new ContentDialog
        {
            Title = "Compose message",
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = 720,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            PrimaryButtonText = "Send",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        void RefreshComposePresentation(int templateCount)
        {
            var state = ServiceBusPagePresentation.BuildComposeDialogState(
                draft.IsReplay
                    ? string.IsNullOrWhiteSpace(replayTargetEntityBox.Text) ? ViewModel.ActiveTab?.EntityPath : replayTargetEntityBox.Text.Trim()
                    : ViewModel.ActiveTab?.EntityPath,
                templateCount,
                scheduleToggle.IsOn,
                draft.IsReplay);

            dialog.Title = state.DialogTitle;
            targetEntityText.Text = state.TargetEntityText;
            templateSummaryText.Text = state.TemplateSummaryText;
            schedulePanel.Visibility = state.SchedulePanelVisibility;
            replayTargetPanel.Visibility = state.ReplayPanelVisibility;
            scheduleToggle.Visibility = state.ScheduleToggleVisibility;
            dialog.PrimaryButtonText = state.PrimaryButtonText;
        }

        void SetStatus(string? text)
        {
            statusText.Text = text ?? string.Empty;
            statusText.Visibility = string.IsNullOrWhiteSpace(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        void UpdateDraftFromInputs()
        {
            draft.MessageId = messageIdBox.Text ?? string.Empty;
            draft.Subject = subjectBox.Text ?? string.Empty;
            draft.CorrelationId = correlationIdBox.Text ?? string.Empty;
            draft.ContentType = contentTypeBox.Text ?? string.Empty;
            draft.PropertiesText = propertiesBox.Text ?? string.Empty;
            draft.Body = bodyBox.Text ?? string.Empty;
            draft.TargetNamespaceId = replayNamespacePicker.SelectedItem is ComposeNamespaceOption namespaceOption
                ? namespaceOption.NamespaceId
                : null;
            draft.TargetEntityPath = replayTargetEntityBox.Text ?? string.Empty;
            draft.ReplayOverrideSubject = replayOverrideSubjectBox.Text ?? string.Empty;
            draft.ReplayOverrideCorrelationId = replayOverrideCorrelationIdBox.Text ?? string.Empty;
            draft.ReplayPropertyRenamesText = replayPropertyRenamesBox.Text ?? string.Empty;
            draft.ReplayPropertyRemovalsText = replayPropertyRemovesBox.Text ?? string.Empty;
            draft.IsScheduled = scheduleToggle.IsOn;
            draft.ScheduledDate = scheduleDatePicker.Date;
            draft.ScheduledTime = scheduleTimePicker.Time;
        }

        void ApplyDraftToInputs()
        {
            messageIdBox.Text = draft.MessageId;
            subjectBox.Text = draft.Subject;
            correlationIdBox.Text = draft.CorrelationId;
            contentTypeBox.Text = draft.ContentType;
            propertiesBox.Text = draft.PropertiesText;
            bodyBox.Text = draft.Body;
            replayTargetEntityBox.Text = string.IsNullOrWhiteSpace(draft.TargetEntityPath)
                ? ViewModel.ActiveTab?.EntityPath ?? string.Empty
                : draft.TargetEntityPath;
            replayOverrideSubjectBox.Text = draft.ReplayOverrideSubject;
            replayOverrideCorrelationIdBox.Text = draft.ReplayOverrideCorrelationId;
            replayPropertyRenamesBox.Text = draft.ReplayPropertyRenamesText;
            replayPropertyRemovesBox.Text = draft.ReplayPropertyRemovalsText;
            replayNamespacePicker.SelectedItem = replayNamespaceOptions.FirstOrDefault(option => option.NamespaceId == draft.TargetNamespaceId)
                ?? replayNamespaceOptions.FirstOrDefault();
            scheduleToggle.IsOn = draft.IsScheduled;
            scheduleDatePicker.Date = draft.ScheduledDate;
            scheduleTimePicker.Time = draft.ScheduledTime;
        }

        void RefreshTemplatePicker(SbMessageTemplate? selectedTemplate = null)
        {
            var templates = ViewModel.MessageTemplates.ToList();
            templatePicker.ItemsSource = templates;

            if (selectedTemplate is not null)
            {
                templatePicker.SelectedItem = templates.FirstOrDefault(template => template.Id == selectedTemplate.Id);
            }

            RefreshComposePresentation(templates.Count);
            applyTemplateButton.IsEnabled = templatePicker.SelectedItem is SbMessageTemplate;
        }

        void RefreshDialogState()
        {
            RefreshComposePresentation(ViewModel.MessageTemplates.Count);
            applyTemplateButton.IsEnabled = templatePicker.SelectedItem is SbMessageTemplate;
        }

        templatePicker.SelectionChanged += (_, _) => RefreshDialogState();
        scheduleToggle.Toggled += (_, _) => RefreshDialogState();
        replayNamespacePicker.SelectionChanged += (_, _) => RefreshDialogState();
        replayTargetEntityBox.TextChanged += (_, _) => RefreshDialogState();
        applyTemplateButton.Click += (_, _) =>
        {
            if (templatePicker.SelectedItem is not SbMessageTemplate selectedTemplate)
            {
                SetStatus("Select a saved template first.");
                return;
            }

            UpdateDraftFromInputs();
            ViewModel.ApplyTemplateToComposeDraft(draft, selectedTemplate);
            ApplyDraftToInputs();
            SetStatus($"Applied template '{selectedTemplate.Name}'.");
        };

        saveTemplateButton.Click += async (_, _) =>
        {
            try
            {
                UpdateDraftFromInputs();
                var savedTemplate = await ViewModel.SaveComposeTemplateAsync(templateNameBox.Text, draft);
                templateNameBox.Text = string.Empty;
                RefreshTemplatePicker(savedTemplate);
                SetStatus($"Saved template '{savedTemplate.Name}'.");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();

            try
            {
                SetStatus(null);
                UpdateDraftFromInputs();
                await ViewModel.ExecuteComposeDraftAsync(draft);
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                SetStatus(ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        };

        ApplyDraftToInputs();
        RefreshTemplatePicker();
        RefreshDialogState();
        return dialog;
    }

    private async Task ShowBatchSendDialogAsync(ServiceBusTabViewModel activeTab)
    {
        var targetEntityBox = new TextBox
        {
            Text = activeTab.EntityPath,
        };

        var jsonInputBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 240,
            PlaceholderText = "[{ \"body\": \"hello\", \"subject\": \"test\" }]",
        };

        var helperText = new TextBlock
        {
            Text = "JSON array of message objects. 'body' is required; 'messageId', 'correlationId', 'subject', 'contentType', and 'applicationProperties' are optional.",
            TextWrapping = TextWrapping.Wrap,
        };

        var statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var previewSummaryText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var previewList = new ListView
        {
            MaxHeight = 320,
            SelectionMode = ListViewSelectionMode.None,
            Visibility = Visibility.Collapsed,
        };

        var previewOverflowText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var content = new StackPanel
        {
            Spacing = 12,
        };

        AddLabeledControl(content, "Target entity", targetEntityBox);
        content.Children.Add(helperText);
        AddLabeledControl(content, "JSON payload", jsonInputBox);
        content.Children.Add(statusText);
        content.Children.Add(previewSummaryText);
        content.Children.Add(previewList);
        content.Children.Add(previewOverflowText);

        var dialog = new ContentDialog
        {
            Title = "Batch send messages",
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = 720,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            PrimaryButtonText = "Validate & preview",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        IReadOnlyList<BatchSendEntry>? validatedEntries = null;

        void SetStatus(string? text)
        {
            statusText.Text = text ?? string.Empty;
            statusText.Visibility = string.IsNullOrWhiteSpace(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        void ShowImportState()
        {
            previewSummaryText.Visibility = Visibility.Collapsed;
            previewList.Visibility = Visibility.Collapsed;
            previewOverflowText.Visibility = Visibility.Collapsed;
            targetEntityBox.IsReadOnly = false;
            jsonInputBox.IsReadOnly = false;
            dialog.PrimaryButtonText = "Validate & preview";
            dialog.IsPrimaryButtonEnabled = true;
            dialog.SecondaryButtonText = string.Empty;
            dialog.CloseButtonText = "Cancel";
        }

        void ShowPreviewState(IReadOnlyList<BatchSendEntry> entries)
        {
            var validCount = entries.Count(static entry => entry.IsValid);
            var invalidCount = entries.Count - validCount;

            previewSummaryText.Text = $"{validCount} valid, {invalidCount} invalid (will be skipped) → {targetEntityBox.Text.Trim()}";
            previewSummaryText.Visibility = Visibility.Visible;
            previewList.ItemsSource = BuildBatchPreviewLines(entries);
            previewList.Visibility = Visibility.Visible;
            previewOverflowText.Text = entries.Count > 50
                ? $"Showing the first 50 of {entries.Count} parsed entries."
                : string.Empty;
            previewOverflowText.Visibility = entries.Count > 50 ? Visibility.Visible : Visibility.Collapsed;
            targetEntityBox.IsReadOnly = true;
            jsonInputBox.IsReadOnly = true;
            dialog.PrimaryButtonText = validCount > 0 ? $"Send {validCount} message(s)" : "Send 0 message(s)";
            dialog.IsPrimaryButtonEnabled = validCount > 0;
            dialog.SecondaryButtonText = "Back";
            dialog.CloseButtonText = "Cancel";
        }

        dialog.SecondaryButtonClick += (_, args) =>
        {
            if (validatedEntries is null)
            {
                return;
            }

            args.Cancel = true;
            validatedEntries = null;
            SetStatus(null);
            ShowImportState();
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();

            try
            {
                SetStatus(null);

                if (validatedEntries is null)
                {
                    var targetEntityPath = targetEntityBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(targetEntityPath))
                    {
                        args.Cancel = true;
                        SetStatus("Target entity is required.");
                        return;
                    }

                    validatedEntries = ServiceBusBatchSendWorkflow.ParseEntries(jsonInputBox.Text.Trim());
                    ShowPreviewState(validatedEntries);
                    args.Cancel = true;
                    return;
                }

                var result = await ServiceBusBatchSendWorkflow.SendAsync(
                    activeTab.Client,
                    targetEntityBox.Text,
                    validatedEntries);

                if (string.Equals(targetEntityBox.Text.Trim(), activeTab.EntityPath, StringComparison.OrdinalIgnoreCase))
                {
                    await ViewModel.RefreshActiveTabCommand.ExecuteAsync(null);
                }

                await ShowMessageDialogAsync("Batch send results", BuildBatchSendResultMessage(result));
            }
            catch (OperationCanceledException)
            {
                args.Cancel = true;
                SetStatus("Batch send canceled.");
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                SetStatus(ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        };

        ShowImportState();
        await dialog.ShowAsync();
    }

    private async Task ShowBatchDeadLetterReplayDialogAsync(ServiceBusTabViewModel activeTab)
    {
        var targetEntityBox = new TextBox
        {
            Text = activeTab.EntityPath,
        };

        var overrideSubjectBox = new TextBox
        {
            PlaceholderText = "Keep original subject",
        };

        var overrideCorrelationIdBox = new TextBox
        {
            PlaceholderText = "Keep original correlation ID",
        };

        var propertyRenamesBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            PlaceholderText = "oldKey=newKey per line",
        };

        var propertyRemovesBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            PlaceholderText = "propertyKey per line",
        };

        var helperText = new TextBlock
        {
            Text = "Replay re-sends the selected dead-letter messages to the target entity and completes them from the DLQ.",
            TextWrapping = TextWrapping.Wrap,
        };

        var statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var previewSummaryText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var previewList = new ListView
        {
            MaxHeight = 280,
            SelectionMode = ListViewSelectionMode.None,
            Visibility = Visibility.Collapsed,
        };

        var content = new StackPanel
        {
            Spacing = 12,
        };

        AddLabeledControl(content, "Target entity", targetEntityBox);
        content.Children.Add(helperText);
        AddLabeledControl(content, "Override subject", overrideSubjectBox);
        AddLabeledControl(content, "Override correlation ID", overrideCorrelationIdBox);
        AddLabeledControl(content, "Property renames", propertyRenamesBox);
        AddLabeledControl(content, "Property removes", propertyRemovesBox);
        content.Children.Add(statusText);
        content.Children.Add(previewSummaryText);
        content.Children.Add(previewList);

        var dialog = new ContentDialog
        {
            Title = "Batch replay dead-letter messages",
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = 720,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            PrimaryButtonText = "Review & confirm",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        ServiceBusBatchReplayRequest? request = null;

        void SetStatus(string? text)
        {
            statusText.Text = text ?? string.Empty;
            statusText.Visibility = string.IsNullOrWhiteSpace(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        void ShowEditState()
        {
            previewSummaryText.Visibility = Visibility.Collapsed;
            previewList.Visibility = Visibility.Collapsed;
            targetEntityBox.IsReadOnly = false;
            overrideSubjectBox.IsReadOnly = false;
            overrideCorrelationIdBox.IsReadOnly = false;
            propertyRenamesBox.IsReadOnly = false;
            propertyRemovesBox.IsReadOnly = false;
            dialog.PrimaryButtonText = "Review & confirm";
            dialog.IsPrimaryButtonEnabled = true;
            dialog.SecondaryButtonText = string.Empty;
            dialog.CloseButtonText = "Cancel";
        }

        void ShowPreviewState(ServiceBusBatchReplayRequest replayRequest)
        {
            previewSummaryText.Text = $"{activeTab.BatchSelectionCount} selected message(s) from {activeTab.EntityPath} -> {replayRequest.TargetEntityPath}";
            previewSummaryText.Visibility = Visibility.Visible;
            previewList.ItemsSource = BuildBatchReplayPreviewLines(activeTab.SelectedMessages, replayRequest);
            previewList.Visibility = Visibility.Visible;
            targetEntityBox.IsReadOnly = true;
            overrideSubjectBox.IsReadOnly = true;
            overrideCorrelationIdBox.IsReadOnly = true;
            propertyRenamesBox.IsReadOnly = true;
            propertyRemovesBox.IsReadOnly = true;
            dialog.PrimaryButtonText = $"Replay {activeTab.BatchSelectionCount} message(s)";
            dialog.SecondaryButtonText = "Back";
            dialog.CloseButtonText = "Cancel";
        }

        ServiceBusBatchReplayRequest BuildRequest() => new()
        {
            TargetEntityPath = targetEntityBox.Text ?? string.Empty,
            OverrideSubject = overrideSubjectBox.Text ?? string.Empty,
            OverrideCorrelationId = overrideCorrelationIdBox.Text ?? string.Empty,
            PropertyRenamesText = propertyRenamesBox.Text ?? string.Empty,
            PropertyRemovalsText = propertyRemovesBox.Text ?? string.Empty,
        };

        dialog.SecondaryButtonClick += (_, args) =>
        {
            if (request is null)
            {
                return;
            }

            args.Cancel = true;
            request = null;
            SetStatus(null);
            ShowEditState();
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();

            try
            {
                SetStatus(null);

                if (request is null)
                {
                    request = BuildRequest();
                    if (string.IsNullOrWhiteSpace(request.TargetEntityPath))
                    {
                        args.Cancel = true;
                        SetStatus("Target entity is required.");
                        return;
                    }

                    ShowPreviewState(request);
                    args.Cancel = true;
                    return;
                }

                var result = await ViewModel.ReplaySelectedDeadLettersAsync(request);
                await ShowMessageDialogAsync("Batch replay results", BuildBatchSendResultMessage(result));
            }
            catch (OperationCanceledException)
            {
                args.Cancel = true;
                SetStatus("Batch replay canceled.");
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                SetStatus(ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        };

        ShowEditState();
        await dialog.ShowAsync();
    }

    private async Task ShowSaveTemplateDialogAsync(ServiceBusComposeDraft draft, string defaultTemplateName)
    {
        var templateNameBox = new TextBox
        {
            Text = defaultTemplateName,
            PlaceholderText = "Template name",
        };

        var statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var content = new StackPanel
        {
            Spacing = 12,
        };
        AddLabeledControl(content, "Template name", templateNameBox);
        content.Children.Add(new TextBlock
        {
            Text = "Body, subject, content-type, correlation ID, and application properties will be saved.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(statusText);

        var dialog = new ContentDialog
        {
            Title = "Save message as template",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();

            try
            {
                statusText.Visibility = Visibility.Collapsed;
                var savedTemplate = await ViewModel.SaveComposeTemplateAsync(templateNameBox.Text, draft);
                GetNotificationService().ShowSuccess("Template saved", savedTemplate.Name);
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                statusText.Text = ex.Message;
                statusText.Visibility = Visibility.Visible;
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private async Task ExecuteConfirmedActionAsync(
        string title,
        string message,
        string primaryButtonText,
        string failureTitle,
        Func<Task> action)
    {
        if (!await ShowConfirmationDialogAsync(title, message, primaryButtonText))
        {
            return;
        }

        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await ShowMessageDialogAsync(failureTitle, ex.Message);
        }
    }

    private async Task<bool> ShowConfirmationDialogAsync(string title, string message, string primaryButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowMessageDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
        };

        await dialog.ShowAsync();
    }

    private static IReadOnlyList<string> BuildBatchPreviewLines(IReadOnlyList<BatchSendEntry> entries)
    {
        return entries
            .Take(50)
            .Select(entry => entry.IsValid
                ? $"✓ {Truncate(entry.MessageId, 16)} | {Truncate(entry.Subject ?? "-", 18)} | {Truncate(entry.CorrelationId ?? "-", 16)} | {Truncate(entry.Body, 32)}"
                : $"✗ {Truncate(entry.MessageId, 16)} | {entry.ValidationError}")
            .ToList();
    }

    private static string BuildBatchSendResultMessage(BatchOperationResult result)
    {
        if (result.Errors.Count == 0)
        {
            return result.SummaryLine;
        }

        var errorLines = result.Errors
            .Take(10)
            .Select(error => $"- {error.MessageId}: {error.Reason}");

        return string.Join(
            Environment.NewLine,
            new[] { result.SummaryLine, string.Empty, "Errors:" }.Concat(errorLines));
    }

    private static IReadOnlyList<string> BuildBatchReplayPreviewLines(
        IReadOnlyList<SbMessage> messages,
        ServiceBusBatchReplayRequest request)
    {
        var lines = messages
            .Take(25)
            .Select(message => $"{Truncate(message.MessageId, 20)} -> {Truncate(request.TargetEntityPath, 24)}")
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.OverrideSubject))
        {
            lines.Add($"Subject override: {request.OverrideSubject.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(request.OverrideCorrelationId))
        {
            lines.Add($"Correlation override: {request.OverrideCorrelationId.Trim()}");
        }

        return lines;
    }

    private static string SerializeFullMessage(SbMessage message)
    {
        return System.Text.Json.JsonSerializer.Serialize(
            new
            {
                messageId = message.MessageId,
                correlationId = message.CorrelationId,
                subject = message.Subject,
                contentType = message.ContentType,
                deliveryCount = message.DeliveryCount,
                enqueuedAt = message.EnqueuedAt,
                sequenceNumber = message.SequenceNumber,
                sessionId = message.SessionId,
                deadLetterReason = message.DeadLetterReason,
                deadLetterErrorDescription = message.DeadLetterErrorDescription,
                applicationProperties = message.ApplicationProperties,
                body = message.Body,
            },
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            });
    }

    private static ScheduledMessageItemViewModel? TryGetScheduledMessage(object sender) =>
        sender is Button button ? button.DataContext as ScheduledMessageItemViewModel : null;

    private void VisibleMessages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.ActiveTab is not { } activeTab || sender is not ListView listView)
        {
            return;
        }

        var selectedItems = listView.SelectedItems.OfType<ServiceBusVisibleMessageItemViewModel>().ToList();
        var selectedMessages = selectedItems.Select(item => item.Message).ToList();
        activeTab.SetSelectedMessages(selectedMessages);
        activeTab.SelectedMessage = (listView.SelectedItem as ServiceBusVisibleMessageItemViewModel)?.Message ?? selectedMessages.LastOrDefault();
    }

    private SbMessage? TryGetSelectedMessage() => ViewModel.ActiveTab?.SelectedMessage;

    private INotificationService GetNotificationService() =>
        App.Current.Services.GetRequiredService<INotificationService>();

    private static void AddLabeledControl(Panel parent, string label, Control control)
    {
        parent.Children.Add(new TextBlock { Text = label });
        parent.Children.Add(control);
    }

    private static void CopyToClipboard(string value)
    {
        var package = new DataPackage();
        package.SetText(value);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value.Length > maxLength
            ? value[..maxLength] + "…"
            : value;
    }

    private static string BuildExportPath(string fileName)
    {
        var downloadsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        Directory.CreateDirectory(downloadsFolder);

        var sanitizedFileName = SanitizeFileName(fileName);
        var candidatePath = Path.Combine(downloadsFolder, sanitizedFileName);
        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sanitizedFileName);
        var extension = Path.GetExtension(sanitizedFileName);

        for (var index = 1; index < 1000; index++)
        {
            candidatePath = Path.Combine(downloadsFolder, $"{fileNameWithoutExtension} ({index}){extension}");
            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return Path.Combine(downloadsFolder, $"{Guid.NewGuid():N}{extension}");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(character => invalidChars.Contains(character) ? '_' : character));
    }

    private sealed record ComposeNamespaceOption(Guid? NamespaceId, string Label);
}