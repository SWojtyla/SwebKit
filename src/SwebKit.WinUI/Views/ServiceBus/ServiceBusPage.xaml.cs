using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.WinUI.Services;
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
        content.Children.Add(statusText);
        AddLabeledControl(content, "Message ID", messageIdBox);
        AddLabeledControl(content, "Subject", subjectBox);
        AddLabeledControl(content, "Correlation ID", correlationIdBox);
        AddLabeledControl(content, "Content-Type", contentTypeBox);
        AddLabeledControl(content, "Application properties", propertiesBox);
        AddLabeledControl(content, "Body", bodyBox);
        content.Children.Add(scheduleToggle);
        content.Children.Add(schedulePanel);

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
                ViewModel.ActiveTab?.EntityPath,
                templateCount,
                scheduleToggle.IsOn);

            targetEntityText.Text = state.TargetEntityText;
            templateSummaryText.Text = state.TemplateSummaryText;
            schedulePanel.Visibility = state.SchedulePanelVisibility;
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
                await ViewModel.SendOrScheduleActiveMessageAsync(draft);
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

    private static ScheduledMessageItemViewModel? TryGetScheduledMessage(object sender) =>
        sender is Button button ? button.DataContext as ScheduledMessageItemViewModel : null;

    private static void AddLabeledControl(Panel parent, string label, Control control)
    {
        parent.Children.Add(new TextBlock { Text = label });
        parent.Children.Add(control);
    }
}