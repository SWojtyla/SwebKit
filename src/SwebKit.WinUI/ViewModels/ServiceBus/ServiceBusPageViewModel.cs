using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.ViewModels.ServiceBus;

public sealed partial class ServiceBusPageViewModel : ObservableObject, IAsyncDisposable
{
    private const int PeekCount = 50;

    private readonly AppStateService _appState;
    private readonly ICredentialStore _credentialStore;
    private readonly IServiceBusClientFactory _serviceBusClientFactory;
    private readonly IServiceBusNamespaceBootstrapper _bootstrapper;
    private readonly ScheduledMessageRepository _scheduledMessageRepository;
    private readonly UiStateRepository _uiState;
    private readonly OperatorWorkspaceService _workspaceService;
    private CancellationTokenSource _loadCts = new();
    private bool _loaded;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ConnectionStringInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial ServiceBusTabViewModel? ActiveTab { get; set; }

    public ObservableCollection<ServiceBusNamespaceItemViewModel> Namespaces { get; } = [];

    public ObservableCollection<ServiceBusTabViewModel> Tabs { get; } = [];

    public bool HasNamespaces => Namespaces.Count > 0;

    public bool ShowEmptyState => !HasNamespaces;

    public bool CanMutate => !IsBusy;

    public bool HasActiveTab => ActiveTab is not null;

    public bool ShowTabEmptyState => !HasActiveTab;

    public bool CanRefreshActiveTab => ActiveTab is not null && !IsBusy;

    public bool CanLoadMoreActiveTab => ActiveTab?.CanLoadMore == true;

    public bool CanSendActiveMessage => ActiveTab is { IsDlq: false, IsScheduled: false } && !IsBusy;

    public bool CanResubmitSelectedDeadLetter =>
        ActiveTab?.IsDlq == true &&
        ActiveTab.SelectedMessage?.SequenceNumber is not null &&
        !IsBusy;

    public bool CanCompleteSelectedDeadLetter =>
        ActiveTab?.IsDlq == true &&
        ActiveTab.SelectedMessage?.SequenceNumber is not null &&
        !IsBusy;

    public IReadOnlyList<SbMessageTemplate> MessageTemplates => _appState.MessageTemplates;

    public ServiceBusPageViewModel(
        AppStateService appState,
        ICredentialStore credentialStore,
        IServiceBusClientFactory serviceBusClientFactory,
        IServiceBusNamespaceBootstrapper bootstrapper,
        ScheduledMessageRepository scheduledMessageRepository,
        UiStateRepository uiState,
        OperatorWorkspaceService workspaceService)
    {
        _appState = appState;
        _credentialStore = credentialStore;
        _serviceBusClientFactory = serviceBusClientFactory;
        _bootstrapper = bootstrapper;
        _scheduledMessageRepository = scheduledMessageRepository;
        _uiState = uiState;
        _workspaceService = workspaceService;

        _workspaceService.RegisterRestoreHandler("service-bus", RestoreWorkspaceAsync);

        Namespaces.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNamespaces));
            OnPropertyChanged(nameof(ShowEmptyState));
        };

        Tabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActiveTab));
            OnPropertyChanged(nameof(ShowTabEmptyState));
            OnPropertyChanged(nameof(CanRefreshActiveTab));
            OnPropertyChanged(nameof(CanLoadMoreActiveTab));
            LoadMoreActiveTabCommand.NotifyCanExecuteChanged();
        };
    }

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        await _scheduledMessageRepository.LoadAsync();
        _loaded = true;
        await ReloadAsync();
        await _workspaceService.ApplyPendingRestoreAsync("service-bus");
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        await _appState.WhenInitializedAsync();

        await CancelLoadAsync();
        _loadCts = new CancellationTokenSource();

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await DisposeClientsAsync();
            Namespaces.Clear();
            Tabs.Clear();
            ActiveTab = null;

            var initialStates = _bootstrapper.BuildInitialStates(
                _appState.ServiceBusNamespaces,
                cachedSnapshots: new Dictionary<Guid, ServiceBusNamespaceBootstrapSnapshot>(),
                _appState.UseDemoData);

            foreach (var state in initialStates)
            {
                Namespaces.Add(new ServiceBusNamespaceItemViewModel(
                    state.Namespace,
                    OpenScheduledNamespaceAsync,
                    RemoveNamespaceAsync)
                {
                    Client = state.Client,
                    IsConnecting = state.ShouldConnect,
                    ConnectionError = state.ConnectionError,
                });
            }

            var connectTasks = Namespaces
                .Where(namespaceItem => namespaceItem.IsConnecting)
                .Select(namespaceItem => ConnectNamespaceAsync(namespaceItem, _loadCts.Token));

            await Task.WhenAll(connectTasks);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddNamespaceAsync()
    {
        var raw = ConnectionStringInput.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            ErrorMessage = "Connection string is required.";
            return;
        }

        string fullyQualifiedNamespace;
        try
        {
            fullyQualifiedNamespace = _serviceBusClientFactory.ParseFullyQualifiedNamespace(raw);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Invalid connection string: {ex.Message}";
            return;
        }

        if (_appState.ServiceBusNamespaces.Any(ns => string.Equals(ns.FullyQualifiedNamespace, fullyQualifiedNamespace, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = "This namespace is already added.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var credentialKey = $"sb:ns:{Guid.NewGuid()}";
            _credentialStore.Save(credentialKey, raw);

            var serviceBusNamespace = new ServiceBusNamespace
            {
                Alias = fullyQualifiedNamespace.Split('.')[0],
                FullyQualifiedNamespace = fullyQualifiedNamespace,
                CredentialKey = credentialKey,
            };

            await _appState.AddServiceBusNamespaceAsync(serviceBusNamespace);

            var namespaceItem = new ServiceBusNamespaceItemViewModel(serviceBusNamespace, OpenScheduledNamespaceAsync, RemoveNamespaceAsync)
            {
                IsConnecting = true,
            };

            Namespaces.Add(namespaceItem);

            ConnectionStringInput = string.Empty;
            await ConnectNamespaceAsync(namespaceItem, _loadCts.Token);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveNamespaceAsync(ServiceBusNamespaceItemViewModel? namespaceItem)
    {
        if (namespaceItem is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            if (namespaceItem.Client is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }

            _credentialStore.Delete(namespaceItem.Namespace.CredentialKey);
            CloseTabsForNamespace(namespaceItem.Namespace.Id);
            Namespaces.Remove(namespaceItem);
            await _appState.RemoveServiceBusNamespaceAsync(namespaceItem.Namespace.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectNamespaceAsync(ServiceBusNamespaceItemViewModel namespaceItem, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _bootstrapper.ConnectAsync(namespaceItem.Namespace, cancellationToken);
            namespaceItem.Client = result.Client;
            namespaceItem.ConnectionError = result.ConnectionError;

            if (result.Client is not null)
            {
                await LoadNamespaceEntitiesAsync(namespaceItem, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            namespaceItem.ConnectionError = ex.Message;
        }
        finally
        {
            namespaceItem.IsConnecting = false;
        }
    }

    private async Task CancelLoadAsync()
    {
        if (_loadCts.IsCancellationRequested)
        {
            return;
        }

        await _loadCts.CancelAsync();
        _loadCts.Dispose();
    }

    private async Task DisposeClientsAsync()
    {
        foreach (var namespaceItem in Namespaces)
        {
            if (namespaceItem.Client is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _workspaceService.UnregisterRestoreHandler("service-bus");
        await CancelLoadAsync();
        await DisposeClientsAsync();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanMutate));
        OnPropertyChanged(nameof(CanRefreshActiveTab));
        OnPropertyChanged(nameof(CanLoadMoreActiveTab));
        OnPropertyChanged(nameof(CanSendActiveMessage));
        OnPropertyChanged(nameof(CanResubmitSelectedDeadLetter));
        OnPropertyChanged(nameof(CanCompleteSelectedDeadLetter));
        LoadMoreActiveTabCommand.NotifyCanExecuteChanged();
        ResubmitSelectedDeadLetterCommand.NotifyCanExecuteChanged();
        CompleteSelectedDeadLetterCommand.NotifyCanExecuteChanged();
    }

    partial void OnActiveTabChanging(ServiceBusTabViewModel? oldValue, ServiceBusTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnActiveTabPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnActiveTabPropertyChanged;
        }
    }

    partial void OnActiveTabChanged(ServiceBusTabViewModel? value)
    {
        foreach (var tab in Tabs)
        {
            tab.IsActive = ReferenceEquals(tab, value);
        }

        OnPropertyChanged(nameof(HasActiveTab));
        OnPropertyChanged(nameof(ShowTabEmptyState));
        OnPropertyChanged(nameof(CanRefreshActiveTab));
        OnPropertyChanged(nameof(CanLoadMoreActiveTab));
        OnPropertyChanged(nameof(CanSendActiveMessage));
        OnPropertyChanged(nameof(CanResubmitSelectedDeadLetter));
        OnPropertyChanged(nameof(CanCompleteSelectedDeadLetter));
        LoadMoreActiveTabCommand.NotifyCanExecuteChanged();
        ResubmitSelectedDeadLetterCommand.NotifyCanExecuteChanged();
        CompleteSelectedDeadLetterCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectTab(ServiceBusTabViewModel? tab)
    {
        if (tab is not null)
        {
            ActiveTab = tab;
            _ = PublishWorkspaceSnapshotAsync(recordRecent: true);
        }
    }

    [RelayCommand]
    private void CloseTab(ServiceBusTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        Tabs.Remove(tab);
        if (ReferenceEquals(ActiveTab, tab))
        {
            ActiveTab = Tabs.LastOrDefault();
        }

        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
    }

    [RelayCommand]
    private async Task RefreshActiveTabAsync()
    {
        if (ActiveTab is null)
        {
            return;
        }

        if (ActiveTab.IsScheduled)
        {
            await RefreshScheduledMessagesAsync(ActiveTab);
            return;
        }

        await LoadTabMessagesAsync(ActiveTab, _loadCts.Token);
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreActiveTab))]
    private async Task LoadMoreActiveTabAsync()
    {
        if (ActiveTab is null || !ActiveTab.CanLoadMore)
        {
            return;
        }

        ActiveTab.ExpandRequestedWindow();
        await LoadTabMessagesAsync(ActiveTab, _loadCts.Token);
    }

    [RelayCommand]
    private async Task CancelScheduledMessageAsync(ScheduledMessageItemViewModel? scheduledMessage)
    {
        if (scheduledMessage is null || ActiveTab is null || !ActiveTab.IsScheduled)
        {
            return;
        }

        if (!scheduledMessage.CanCancel)
        {
            ActiveTab.ScheduledActionError = "This scheduled entry is already due for enqueue and can no longer be canceled from the broker.";
            return;
        }

        IsBusy = true;
        ActiveTab.ScheduledActionError = null;

        try
        {
            await ActiveTab.Client.CancelScheduledMessageAsync(scheduledMessage.EntityPath, scheduledMessage.SequenceNumber, _loadCts.Token);
            await _scheduledMessageRepository.RemoveAsync(scheduledMessage.Id);
            await RefreshScheduledMessagesAsync(ActiveTab);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ActiveTab.ScheduledActionError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveScheduledMessageAsync(ScheduledMessageItemViewModel? scheduledMessage)
    {
        if (scheduledMessage is null || ActiveTab is null || !ActiveTab.IsScheduled)
        {
            return;
        }

        IsBusy = true;
        ActiveTab.ScheduledActionError = null;

        try
        {
            await _scheduledMessageRepository.RemoveAsync(scheduledMessage.Id);
            await RefreshScheduledMessagesAsync(ActiveTab);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ActiveTab.ScheduledActionError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanResubmitSelectedDeadLetter))]
    private async Task ResubmitSelectedDeadLetterAsync()
    {
        if (ActiveTab?.SelectedMessage?.SequenceNumber is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await ActiveTab.Client.ResubmitDeadLetterAsync(
                ActiveTab.EntityPath,
                [ActiveTab.SelectedMessage.SequenceNumber.Value.ToString()],
                targetEntityPath: null,
                remapRules: null,
                ct: _loadCts.Token);

            await LoadTabMessagesAsync(ActiveTab, _loadCts.Token);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCompleteSelectedDeadLetter))]
    private async Task CompleteSelectedDeadLetterAsync()
    {
        if (ActiveTab?.SelectedMessage?.SequenceNumber is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await ActiveTab.Client.CompleteDeadLetterAsync(
                ActiveTab.EntityPath,
                [ActiveTab.SelectedMessage.SequenceNumber.Value.ToString()],
                _loadCts.Token);

            await LoadTabMessagesAsync(ActiveTab, _loadCts.Token);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SendActiveMessageAsync(SbMessage message)
    {
        if (ActiveTab is null || ActiveTab.IsDlq)
        {
            throw new InvalidOperationException("An active queue or subscription tab is required to send a message.");
        }

        IsBusy = true;

        try
        {
            await ActiveTab.Client.SendMessageAsync(ActiveTab.EntityPath, message, _loadCts.Token);
            await LoadTabMessagesAsync(ActiveTab, _loadCts.Token);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public ServiceBusComposeDraft CreateComposeDraft()
    {
        var defaultScheduleTime = DateTimeOffset.Now.AddMinutes(15);

        return new ServiceBusComposeDraft
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json",
            ScheduledDate = new DateTimeOffset(
                defaultScheduleTime.Year,
                defaultScheduleTime.Month,
                defaultScheduleTime.Day,
                0,
                0,
                0,
                defaultScheduleTime.Offset),
            ScheduledTime = defaultScheduleTime.TimeOfDay,
        };
    }

    public ServiceBusComposeDraft CreateComposeDraftFromMessage(SbMessage message, bool scheduleForLater = false)
    {
        ArgumentNullException.ThrowIfNull(message);

        var draft = CreateComposeDraft();
        draft.MessageId = string.IsNullOrWhiteSpace(message.MessageId) ? draft.MessageId : message.MessageId;
        draft.Subject = message.Subject ?? string.Empty;
        draft.CorrelationId = message.CorrelationId ?? string.Empty;
        draft.ContentType = message.ContentType ?? draft.ContentType;
        draft.Body = message.Body;
        draft.PropertiesText = SerializeProperties(
            message.ApplicationProperties.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value?.ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase));
        draft.IsScheduled = scheduleForLater;
        return draft;
    }

    public ServiceBusComposeDraft CreateReplayDraftFromMessage(SbMessage message)
    {
        var draft = CreateComposeDraftFromMessage(message);
        draft.MessageId = Guid.NewGuid().ToString();
        draft.IsReplay = true;
        draft.TargetEntityPath = ActiveTab?.EntityPath ?? string.Empty;
        return draft;
    }

    public void ApplyTemplateToComposeDraft(ServiceBusComposeDraft draft, SbMessageTemplate template)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(template);

        draft.Body = template.Body;
        draft.ContentType = template.ContentType ?? "application/json";
        draft.Subject = template.Subject ?? string.Empty;
        draft.CorrelationId = template.CorrelationId ?? string.Empty;
        draft.PropertiesText = SerializeProperties(template.Properties);
    }

    public async Task<SbMessageTemplate> SaveComposeTemplateAsync(string templateName, ServiceBusComposeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var trimmedName = templateName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new InvalidOperationException("Template name is required.");
        }

        if (_appState.MessageTemplates.Any(template => string.Equals(template.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Template name must be unique.");
        }

        var properties = ParseProperties(draft.PropertiesText);
        var template = new SbMessageTemplate
        {
            Name = trimmedName,
            Body = draft.Body,
            ContentType = NormalizeOptional(draft.ContentType),
            Subject = NormalizeOptional(draft.Subject),
            CorrelationId = NormalizeOptional(draft.CorrelationId),
            Properties = properties,
        };

        await _appState.SaveMessageTemplateAsync(template);
        OnPropertyChanged(nameof(MessageTemplates));
        return template;
    }

    public Task<ServiceBusComposeResult> ExecuteComposeDraftAsync(ServiceBusComposeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return draft.IsReplay
            ? ReplayMessageAsync(draft)
            : SendOrScheduleActiveMessageAsync(draft);
    }

    public async Task<BatchOperationResult> ReplaySelectedDeadLettersAsync(ServiceBusBatchReplayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ActiveTab is null || !ActiveTab.IsDlq)
        {
            throw new InvalidOperationException("A dead-letter workspace is required to replay selected messages.");
        }

        var targetEntityPath = string.IsNullOrWhiteSpace(request.TargetEntityPath)
            ? ActiveTab.EntityPath
            : request.TargetEntityPath.Trim();
        if (string.IsNullOrWhiteSpace(targetEntityPath))
        {
            throw new InvalidOperationException("Replay target entity is required.");
        }

        var selectedSequenceNumbers = ActiveTab.SelectedMessages
            .Where(message => message.SequenceNumber is not null)
            .Select(message => message.SequenceNumber!.Value.ToString())
            .ToList();
        if (selectedSequenceNumbers.Count == 0)
        {
            throw new InvalidOperationException("Select at least one dead-letter message with a sequence number to replay.");
        }

        var result = new BatchOperationResult
        {
            Skipped = ActiveTab.SelectedMessages.Count - selectedSequenceNumbers.Count,
        };
        var remapRules = BuildReplayRemapRules(
            request.OverrideSubject,
            request.OverrideCorrelationId,
            request.PropertyRenamesText,
            request.PropertyRemovalsText);

        IsBusy = true;

        try
        {
            foreach (var chunk in selectedSequenceNumbers.Chunk(10))
            {
                try
                {
                    await ActiveTab.Client.ResubmitDeadLetterAsync(
                        ActiveTab.EntityPath,
                        chunk,
                        string.Equals(targetEntityPath, ActiveTab.EntityPath, StringComparison.OrdinalIgnoreCase) ? null : targetEntityPath,
                        remapRules,
                        _loadCts.Token);
                    result.Succeeded += chunk.Length;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Failed += chunk.Length;
                    result.Errors.Add(new BatchOperationItemError
                    {
                        MessageId = $"chunk of {chunk.Length}",
                        Reason = ex.Message.Length > 120 ? ex.Message[..120] + "..." : ex.Message,
                    });
                }
            }

            await LoadTabMessagesAsync(ActiveTab, _loadCts.Token);
            ActiveTab.ClearSelectedMessages();
            ActiveTab.SelectedMessage = null;
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<int> DeleteFilteredMessagesAsync()
    {
        if (ActiveTab is null || !ActiveTab.IsMessageTab)
        {
            throw new InvalidOperationException("An active message workspace is required to delete filtered messages.");
        }

        var sequenceNumbers = ActiveTab.GetVisibleMessageSequenceNumbers();
        if (sequenceNumbers.Count == 0)
        {
            throw new InvalidOperationException("No filtered messages are available for deletion.");
        }

        IsBusy = true;

        try
        {
            var deleted = 0;
            if (ActiveTab.IsDlq)
            {
                await ActiveTab.Client.CompleteDeadLetterAsync(
                    ActiveTab.EntityPath,
                    sequenceNumbers.Select(sequenceNumber => sequenceNumber.ToString(CultureInfo.InvariantCulture)).ToList(),
                    _loadCts.Token);
                deleted = sequenceNumbers.Count;
            }
            else
            {
                deleted = await ActiveTab.Client.CompleteMessagesAsync(ActiveTab.EntityPath, sequenceNumbers, _loadCts.Token);
            }

            ActiveTab.ClearSelectedMessages();
            ActiveTab.SelectedMessage = null;
            await LoadTabMessagesAsync(ActiveTab, _loadCts.Token);
            return deleted;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<int> PurgeActiveTabMessagesAsync()
    {
        if (ActiveTab is null || !ActiveTab.IsMessageTab)
        {
            throw new InvalidOperationException("An active message workspace is required to purge messages.");
        }

        IsBusy = true;

        try
        {
            var deleted = await ActiveTab.Client.PurgeMessagesAsync(ActiveTab.EntityPath, ActiveTab.IsDlq, _loadCts.Token);
            ActiveTab.ClearSelectedMessages();
            ActiveTab.SelectedMessage = null;
            await LoadTabMessagesAsync(ActiveTab, _loadCts.Token);
            return deleted;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string ExportVisibleMessagesAsJson()
    {
        if (ActiveTab is null || !ActiveTab.IsMessageTab)
        {
            throw new InvalidOperationException("An active message workspace is required to export messages.");
        }

        return System.Text.Json.JsonSerializer.Serialize(
            ActiveTab.GetVisibleMessages(),
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            });
    }

    public async Task<ServiceBusComposeResult> SendOrScheduleActiveMessageAsync(ServiceBusComposeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (ActiveTab is null || ActiveTab.IsDlq || ActiveTab.IsScheduled)
        {
            throw new InvalidOperationException("An active queue or subscription tab is required to compose a message.");
        }

        var applicationProperties = ParseProperties(draft.PropertiesText)
            .ToDictionary(static pair => pair.Key, static pair => (object)pair.Value, StringComparer.OrdinalIgnoreCase);

        var message = new SbMessage
        {
            MessageId = string.IsNullOrWhiteSpace(draft.MessageId) ? Guid.NewGuid().ToString() : draft.MessageId.Trim(),
            CorrelationId = NormalizeOptional(draft.CorrelationId),
            Subject = NormalizeOptional(draft.Subject),
            ContentType = NormalizeOptional(draft.ContentType),
            Body = draft.Body,
            ApplicationProperties = applicationProperties,
        };

        if (!draft.IsScheduled)
        {
            await SendActiveMessageAsync(message);
            return ServiceBusComposeResult.Sent(message.MessageId);
        }

        var scheduledEnqueueTime = BuildScheduledEnqueueTime(draft);
        if (scheduledEnqueueTime <= DateTimeOffset.Now)
        {
            throw new InvalidOperationException("Scheduled enqueue time must be in the future.");
        }

        IsBusy = true;

        try
        {
            var sequenceNumber = await ActiveTab.Client.ScheduleMessageAsync(
                ActiveTab.EntityPath,
                message,
                scheduledEnqueueTime,
                _loadCts.Token);

            var scheduledEntry = new ScheduledMessageEntry
            {
                NamespaceId = ActiveTab.NamespaceId,
                EntityPath = ActiveTab.EntityPath,
                SequenceNumber = sequenceNumber,
                ScheduledEnqueueTime = scheduledEnqueueTime,
                MessageId = message.MessageId,
                Subject = message.Subject,
                CorrelationId = message.CorrelationId,
            };

            await _scheduledMessageRepository.AddAsync(scheduledEntry);
            RefreshScheduledTabForNamespace(ActiveTab.NamespaceId);
            return ServiceBusComposeResult.Scheduled(message.MessageId, sequenceNumber, scheduledEntry);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ServiceBusComposeResult> ReplayMessageAsync(ServiceBusComposeDraft draft)
    {
        if (ActiveTab is null || ActiveTab.IsScheduled)
        {
            throw new InvalidOperationException("An active or dead-letter workspace is required to replay a message.");
        }

        var targetEntityPath = string.IsNullOrWhiteSpace(draft.TargetEntityPath)
            ? ActiveTab.EntityPath
            : draft.TargetEntityPath.Trim();
        if (string.IsNullOrWhiteSpace(targetEntityPath))
        {
            throw new InvalidOperationException("Replay target entity is required.");
        }

        var targetClient = ResolveReplayTargetClient(draft.TargetNamespaceId);
        var applicationProperties = ParseProperties(draft.PropertiesText)
            .ToDictionary(static pair => pair.Key, static pair => (object)pair.Value, StringComparer.OrdinalIgnoreCase);

        var message = new SbMessage
        {
            MessageId = string.IsNullOrWhiteSpace(draft.MessageId) ? Guid.NewGuid().ToString() : draft.MessageId.Trim(),
            CorrelationId = NormalizeOptional(draft.CorrelationId),
            Subject = NormalizeOptional(draft.Subject),
            ContentType = NormalizeOptional(draft.ContentType),
            Body = draft.Body,
            ApplicationProperties = applicationProperties,
        };

        ApplyReplayRemapRules(message, BuildReplayRemapRules(
            draft.ReplayOverrideSubject,
            draft.ReplayOverrideCorrelationId,
            draft.ReplayPropertyRenamesText,
            draft.ReplayPropertyRemovalsText));

        IsBusy = true;

        try
        {
            await targetClient.SendMessageAsync(targetEntityPath, message, _loadCts.Token);

            if (!ActiveTab.IsDlq
                && ReferenceEquals(targetClient, ActiveTab.Client)
                && string.Equals(targetEntityPath, ActiveTab.EntityPath, StringComparison.OrdinalIgnoreCase))
            {
                await LoadTabMessagesAsync(ActiveTab, _loadCts.Token);
            }

            return ServiceBusComposeResult.Sent(message.MessageId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadNamespaceEntitiesAsync(ServiceBusNamespaceItemViewModel namespaceItem, CancellationToken cancellationToken)
    {
        if (namespaceItem.Client is null)
        {
            return;
        }

        namespaceItem.IsEntityLoading = true;
        namespaceItem.EntityLoadError = null;
        namespaceItem.ClearEntities();

        try
        {
            var queues = await namespaceItem.Client.ListQueuesAsync(cancellationToken);
            namespaceItem.SetQueues(queues.Select(entity => CreateEntityItem(namespaceItem, entity)));

            var topics = await namespaceItem.Client.ListTopicsAsync(cancellationToken);
            namespaceItem.SetTopics(topics.Select(entity => CreateEntityItem(namespaceItem, entity)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            namespaceItem.EntityLoadError = ex.Message;
        }
        finally
        {
            namespaceItem.IsEntityLoading = false;
        }
    }

    private ServiceBusEntityItemViewModel CreateEntityItem(ServiceBusNamespaceItemViewModel namespaceItem, SbEntityInfo entity)
    {
        return new ServiceBusEntityItemViewModel(
            namespaceItem,
            entity,
            item => OpenEntityAsync(item, isDlq: false),
            item => OpenEntityAsync(item, isDlq: true),
            ToggleTopicAsync,
            ToggleEntityEnabledAsync);
    }

    private async Task ToggleTopicAsync(ServiceBusEntityItemViewModel topicItem)
    {
        if (!topicItem.IsTopic)
        {
            return;
        }

        topicItem.IsExpanded = !topicItem.IsExpanded;
        if (!topicItem.IsExpanded || topicItem.Children.Count > 0 || topicItem.NamespaceItem.Client is null)
        {
            return;
        }

        topicItem.IsLoadingChildren = true;
        topicItem.ChildLoadError = null;

        try
        {
            var subscriptions = await topicItem.NamespaceItem.Client.ListSubscriptionsAsync(topicItem.Name, _loadCts.Token);
            topicItem.SetChildren(subscriptions.Select(subscription => CreateEntityItem(topicItem.NamespaceItem, subscription)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            topicItem.ChildLoadError = ex.Message;
        }
        finally
        {
            topicItem.IsLoadingChildren = false;
        }
    }

    private async Task ToggleEntityEnabledAsync(ServiceBusEntityItemViewModel entityItem)
    {
        var client = entityItem.NamespaceItem.Client;
        if (client is null)
        {
            return;
        }

        var shouldEnable = entityItem.Entity.IsDisabled;
        entityItem.OperationMessage = null;

        try
        {
            if (entityItem.Entity.IsSubscription)
            {
                if (!TryResolveSubscription(entityItem.Entity, out var topicName, out var subscriptionName))
                {
                    throw new InvalidOperationException($"Unable to resolve topic/subscription for '{entityItem.Entity.EntityPath}'.");
                }

                await client.SetSubscriptionEnabledAsync(topicName, subscriptionName, shouldEnable, _loadCts.Token);
            }
            else if (entityItem.Entity.IsTopic)
            {
                await client.SetTopicEnabledAsync(entityItem.Entity.Name, shouldEnable, _loadCts.Token);
            }
            else
            {
                await client.SetQueueEnabledAsync(entityItem.Entity.Name, shouldEnable, _loadCts.Token);
            }

            entityItem.Entity.IsDisabled = !shouldEnable;
            entityItem.OperationMessage = shouldEnable
                ? $"Enabled {entityItem.Name}."
                : $"Disabled {entityItem.Name}.";
            entityItem.NotifyStatusChanged();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            entityItem.OperationMessage = $"Failed to update {entityItem.Name}: {ex.Message}";
        }
    }

    private async Task OpenEntityAsync(ServiceBusEntityItemViewModel entityItem, bool isDlq)
    {
        if (!entityItem.SupportsMessageModes || entityItem.NamespaceItem.Client is null)
        {
            return;
        }

        var id = CreateTabId(entityItem.NamespaceItem.Namespace.Id, entityItem.Entity.EntityPath, isDlq);
        var existing = Tabs.FirstOrDefault(tab => string.Equals(tab.Id, id, StringComparison.Ordinal));
        if (existing is not null)
        {
            ActiveTab = existing;
            _ = PublishWorkspaceSnapshotAsync(recordRecent: true);
            return;
        }

        var tab = new ServiceBusTabViewModel(entityItem.NamespaceItem, entityItem.Entity, isDlq, _uiState, PeekCount);
        Tabs.Add(tab);
        ActiveTab = tab;
        await LoadTabMessagesAsync(tab, _loadCts.Token);
        await PublishWorkspaceSnapshotAsync(recordRecent: true);
    }

    private async Task OpenScheduledNamespaceAsync(ServiceBusNamespaceItemViewModel namespaceItem)
    {
        if (namespaceItem.Client is null)
        {
            return;
        }

        var id = CreateScheduledTabId(namespaceItem.Namespace.Id);
        var existing = Tabs.FirstOrDefault(tab => string.Equals(tab.Id, id, StringComparison.Ordinal));
        if (existing is not null)
        {
            ActiveTab = existing;
            await RefreshScheduledMessagesAsync(existing);
            await PublishWorkspaceSnapshotAsync(recordRecent: true);
            return;
        }

        var tab = ServiceBusTabViewModel.CreateScheduled(namespaceItem, _uiState);
        Tabs.Add(tab);
        ActiveTab = tab;
        await RefreshScheduledMessagesAsync(tab);
        await PublishWorkspaceSnapshotAsync(recordRecent: true);
    }

    private async Task LoadTabMessagesAsync(ServiceBusTabViewModel tab, CancellationToken cancellationToken)
    {
        tab.IsLoading = true;
        tab.ErrorMessage = null;

        try
        {
            var requestCount = tab.RequestedWindowSize > 0 ? tab.RequestedWindowSize : PeekCount;
            var messages = tab.IsDlq
                ? await tab.Client.PeekDeadLetterAsync(tab.EntityPath, requestCount, cancellationToken)
                : await tab.Client.PeekMessagesAsync(tab.EntityPath, requestCount, cancellationToken);

            long? totalAvailableCount = null;
            try
            {
                var stats = await tab.Client.GetEntityStatsAsync(tab.EntityPath, cancellationToken);
                totalAvailableCount = tab.IsDlq ? stats.DeadLetterMessageCount : stats.ActiveMessageCount;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                totalAvailableCount = null;
            }

            tab.SetMessages(messages, totalAvailableCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            tab.ErrorMessage = ex.Message;
            tab.ClearMessages();
        }
        finally
        {
            tab.IsLoading = false;
        }
    }

    private Task RefreshScheduledMessagesAsync(ServiceBusTabViewModel tab)
    {
        tab.IsLoading = true;
        tab.ErrorMessage = null;

        try
        {
            var entries = _scheduledMessageRepository
                .GetByNamespace(tab.NamespaceId)
                .OrderBy(entry => entry.ScheduledEnqueueTime)
                .Select(entry => new ScheduledMessageItemViewModel(entry));

            tab.SetScheduledMessages(entries);
        }
        catch (Exception ex)
        {
            tab.ErrorMessage = ex.Message;
            tab.ClearScheduledMessages();
        }
        finally
        {
            tab.IsLoading = false;
        }

        return Task.CompletedTask;
    }

    private void RefreshScheduledTabForNamespace(Guid namespaceId)
    {
        var scheduledTab = Tabs.FirstOrDefault(tab => tab.IsScheduled && tab.NamespaceId == namespaceId);
        if (scheduledTab is not null)
        {
            RefreshScheduledMessagesAsync(scheduledTab);
        }
    }

    private void CloseTabsForNamespace(Guid namespaceId)
    {
        var tabsToRemove = Tabs.Where(tab => tab.NamespaceId == namespaceId).ToList();
        foreach (var tab in tabsToRemove)
        {
            Tabs.Remove(tab);
        }

        if (ActiveTab is not null && ActiveTab.NamespaceId == namespaceId)
        {
            ActiveTab = Tabs.LastOrDefault();
        }

        _ = PublishWorkspaceSnapshotAsync(recordRecent: false);
    }

    private async Task PublishWorkspaceSnapshotAsync(bool recordRecent)
    {
        var snapshot = BuildWorkspaceSnapshot();
        if (snapshot is null)
        {
            _workspaceService.ClearCurrentSnapshot("service-bus");
            return;
        }

        await _workspaceService.PublishSnapshotAsync(snapshot, recordRecent);
    }

    private WorkspaceSnapshot? BuildWorkspaceSnapshot()
    {
        if (ActiveTab is null)
        {
            return null;
        }

        return new WorkspaceSnapshot
        {
            Resource = new OperatorResourceReference
            {
                Key = $"service-bus:{ActiveTab.Id}",
                Area = "service-bus",
                Kind = ActiveTab.IsScheduled ? "scheduled" : "entity",
                DisplayName = ActiveTab.Title,
                DisplayPath = string.IsNullOrWhiteSpace(ActiveTab.EntityPath)
                    ? ActiveTab.NamespaceAlias
                    : $"{ActiveTab.NamespaceAlias}/{ActiveTab.EntityPath}",
                Summary = ActiveTab.NamespaceAlias,
                Icon = "📨",
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["namespaceId"] = ActiveTab.NamespaceId.ToString("D"),
                    ["entityPath"] = ActiveTab.EntityPath,
                    ["mode"] = ActiveTab.IsScheduled ? "scheduled" : ActiveTab.IsDlq ? "dlq" : "active",
                },
            },
            RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["activeTabId"] = ActiveTab.Id,
                ["namespaceId"] = ActiveTab.NamespaceId.ToString("D"),
                ["entityPath"] = ActiveTab.EntityPath,
                ["mode"] = ActiveTab.IsScheduled ? "scheduled" : ActiveTab.IsDlq ? "dlq" : "active",
                ["tabType"] = ActiveTab.IsScheduled ? "scheduled" : "entity",
                ["tabs"] = System.Text.Json.JsonSerializer.Serialize(Tabs.Select(CreateWorkspaceTabState).ToList()),
            },
        };
    }

    private ServiceBusWorkspaceTabState CreateWorkspaceTabState(ServiceBusTabViewModel tab)
    {
        return new ServiceBusWorkspaceTabState
        {
            NamespaceId = tab.NamespaceId,
            EntityPath = tab.EntityPath,
            Title = tab.Title,
            Mode = tab.IsScheduled ? "scheduled" : tab.IsDlq ? "dlq" : "active",
            TabType = tab.IsScheduled ? "scheduled" : "entity",
        };
    }

    private async Task RestoreWorkspaceAsync(WorkspaceSnapshot snapshot)
    {
        Tabs.Clear();
        ActiveTab = null;

        if (snapshot.RestoreState.TryGetValue("tabs", out var tabsJson))
        {
            var tabStates = System.Text.Json.JsonSerializer.Deserialize<List<ServiceBusWorkspaceTabState>>(tabsJson) ?? [];
            foreach (var tabState in tabStates)
            {
                if (!TryCreateRestoredTab(tabState, out var tab))
                {
                    continue;
                }

                Tabs.Add(tab);
                if (tab.IsScheduled)
                {
                    await RefreshScheduledMessagesAsync(tab);
                }
                else
                {
                    await LoadTabMessagesAsync(tab, _loadCts.Token);
                }
            }

            if (snapshot.RestoreState.TryGetValue("activeTabId", out var activeTabId))
            {
                ActiveTab = Tabs.FirstOrDefault(tab => string.Equals(tab.Id, activeTabId, StringComparison.Ordinal));
            }
        }

        ActiveTab ??= Tabs.FirstOrDefault();

        if (ActiveTab is null
            && snapshot.RestoreState.TryGetValue("namespaceId", out var namespaceText)
            && Guid.TryParse(namespaceText, out var namespaceId))
        {
            var entityPath = snapshot.RestoreState.TryGetValue("entityPath", out var restoredEntityPath)
                ? restoredEntityPath
                : string.Empty;
            var mode = snapshot.RestoreState.TryGetValue("mode", out var restoredMode)
                ? restoredMode
                : "active";
            var tabType = snapshot.RestoreState.TryGetValue("tabType", out var restoredTabType)
                ? restoredTabType
                : "entity";

            if (TryCreateRestoredTab(new ServiceBusWorkspaceTabState
                {
                    NamespaceId = namespaceId,
                    EntityPath = entityPath,
                    Title = snapshot.Resource.DisplayName,
                    Mode = mode,
                    TabType = tabType,
                }, out var restoredTab))
            {
                Tabs.Add(restoredTab);
                ActiveTab = restoredTab;
                if (restoredTab.IsScheduled)
                {
                    await RefreshScheduledMessagesAsync(restoredTab);
                }
                else
                {
                    await LoadTabMessagesAsync(restoredTab, _loadCts.Token);
                }
            }
        }

        await PublishWorkspaceSnapshotAsync(recordRecent: false);
    }

    private bool TryCreateRestoredTab(ServiceBusWorkspaceTabState state, out ServiceBusTabViewModel tab)
    {
        tab = null!;

        var namespaceItem = Namespaces.FirstOrDefault(candidate => candidate.Namespace.Id == state.NamespaceId);
        if (namespaceItem?.Client is null)
        {
            return false;
        }

        if (string.Equals(state.TabType, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.Mode, "scheduled", StringComparison.OrdinalIgnoreCase))
        {
            tab = ServiceBusTabViewModel.CreateScheduled(namespaceItem, _uiState);
            return true;
        }

        var title = string.IsNullOrWhiteSpace(state.Title)
            ? ExtractEntityTitle(state.EntityPath)
            : state.Title;
        var entity = new SbEntityInfo
        {
            Name = title,
            EntityPath = state.EntityPath,
        };

        tab = new ServiceBusTabViewModel(
            namespaceItem,
            entity,
            isDlq: string.Equals(state.Mode, "dlq", StringComparison.OrdinalIgnoreCase),
            _uiState,
            PeekCount);
        return true;
    }

    private static string ExtractEntityTitle(string entityPath)
    {
        if (string.IsNullOrWhiteSpace(entityPath))
        {
            return "Workspace";
        }

        return entityPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
               ?? entityPath;
    }

    private static string CreateTabId(Guid namespaceId, string entityPath, bool isDlq) =>
        $"{namespaceId:D}:{entityPath}:{(isDlq ? "dlq" : "active")}";

    private static string CreateScheduledTabId(Guid namespaceId) =>
        $"{namespaceId:D}:scheduled";

    private static bool TryResolveSubscription(SbEntityInfo entity, out string topicName, out string subscriptionName)
    {
        topicName = entity.TopicName ?? string.Empty;
        subscriptionName = entity.Name;

        if (!string.IsNullOrWhiteSpace(topicName) && !string.IsNullOrWhiteSpace(subscriptionName))
        {
            return true;
        }

        const string marker = "/subscriptions/";
        var markerIndex = entity.EntityPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0)
        {
            return false;
        }

        topicName = entity.EntityPath[..markerIndex];
        subscriptionName = entity.EntityPath[(markerIndex + marker.Length)..];
        return !string.IsNullOrWhiteSpace(topicName) && !string.IsNullOrWhiteSpace(subscriptionName);
    }

    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanLoadMoreActiveTab));
        OnPropertyChanged(nameof(CanSendActiveMessage));
        OnPropertyChanged(nameof(CanResubmitSelectedDeadLetter));
        OnPropertyChanged(nameof(CanCompleteSelectedDeadLetter));
        LoadMoreActiveTabCommand.NotifyCanExecuteChanged();
        ResubmitSelectedDeadLetterCommand.NotifyCanExecuteChanged();
        CompleteSelectedDeadLetterCommand.NotifyCanExecuteChanged();
    }

    private IServiceBusClient ResolveReplayTargetClient(Guid? targetNamespaceId)
    {
        if (ActiveTab is null)
        {
            throw new InvalidOperationException("An active workspace is required to resolve the replay target.");
        }

        if (targetNamespaceId is null || targetNamespaceId == ActiveTab.NamespaceId)
        {
            return ActiveTab.Client;
        }

        var namespaceClient = Namespaces.FirstOrDefault(namespaceItem =>
            namespaceItem.Namespace.Id == targetNamespaceId.Value
            && namespaceItem.Client is not null)?.Client;

        return namespaceClient
            ?? throw new InvalidOperationException("The selected replay target namespace is not connected.");
    }

    private static RemapRules? BuildReplayRemapRules(
        string? overrideSubject,
        string? overrideCorrelationId,
        string? propertyRenamesText,
        string? propertyRemovalsText)
    {
        var rules = new RemapRules
        {
            OverrideSubject = NormalizeOptional(overrideSubject),
            OverrideCorrelationId = NormalizeOptional(overrideCorrelationId),
            PropertyRenames = ParseReplayPropertyRenames(propertyRenamesText),
            PropertyRemoves = ParseReplayPropertyRemoves(propertyRemovalsText),
        };

        return rules.IsEmpty ? null : rules;
    }

    private static Dictionary<string, string> ParseReplayPropertyRenames(string? propertyRenamesText)
    {
        var propertyRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(propertyRenamesText))
        {
            return propertyRenames;
        }

        var lines = propertyRenamesText
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select((line, index) => (Line: line, Number: index + 1));

        foreach (var (line, lineNumber) in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                throw new InvalidOperationException($"Replay property renames must use 'oldKey=newKey' format. Check line {lineNumber}.");
            }

            var oldKey = line[..separatorIndex].Trim();
            var newKey = line[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(oldKey) || string.IsNullOrWhiteSpace(newKey))
            {
                throw new InvalidOperationException($"Replay property rename keys are required on line {lineNumber}.");
            }

            if (!propertyRenames.TryAdd(oldKey, newKey))
            {
                throw new InvalidOperationException($"Replay property rename key '{oldKey}' is duplicated.");
            }
        }

        return propertyRenames;
    }

    private static HashSet<string> ParseReplayPropertyRemoves(string? propertyRemovesText)
    {
        var propertyRemoves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(propertyRemovesText))
        {
            return propertyRemoves;
        }

        var lines = propertyRemovesText
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select((line, index) => (Line: line, Number: index + 1));

        foreach (var (line, lineNumber) in lines)
        {
            var key = line.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!propertyRemoves.Add(key))
            {
                throw new InvalidOperationException($"Replay property remove key '{key}' is duplicated on line {lineNumber}.");
            }
        }

        return propertyRemoves;
    }

    private static void ApplyReplayRemapRules(SbMessage message, RemapRules? rules)
    {
        if (rules is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(rules.OverrideSubject))
        {
            message.Subject = rules.OverrideSubject;
        }

        if (!string.IsNullOrWhiteSpace(rules.OverrideCorrelationId))
        {
            message.CorrelationId = rules.OverrideCorrelationId;
        }

        foreach (var propertyRename in rules.PropertyRenames)
        {
            if (!message.ApplicationProperties.TryGetValue(propertyRename.Key, out var propertyValue))
            {
                continue;
            }

            message.ApplicationProperties.Remove(propertyRename.Key);
            message.ApplicationProperties[propertyRename.Value] = propertyValue;
        }

        foreach (var propertyKey in rules.PropertyRemoves)
        {
            message.ApplicationProperties.Remove(propertyKey);
        }
    }

    private static Dictionary<string, string> ParseProperties(string? propertiesText)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(propertiesText))
        {
            return properties;
        }

        var lines = propertiesText
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select((line, index) => (Line: line, Number: index + 1));

        foreach (var (line, lineNumber) in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                throw new InvalidOperationException($"Application properties must use 'key=value' format. Check line {lineNumber}.");
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException($"Application property key is required on line {lineNumber}.");
            }

            if (!properties.TryAdd(key, value))
            {
                throw new InvalidOperationException($"Application property key '{key}' is duplicated.");
            }
        }

        return properties;
    }

    private static string SerializeProperties(IReadOnlyDictionary<string, string> properties)
    {
        if (properties.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, properties.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset BuildScheduledEnqueueTime(ServiceBusComposeDraft draft)
    {
        var localDate = draft.ScheduledDate.ToLocalTime();
        var localDateTime = new DateTime(
            localDate.Year,
            localDate.Month,
            localDate.Day,
            0,
            0,
            0,
            DateTimeKind.Unspecified).Add(draft.ScheduledTime);

        var offset = TimeZoneInfo.Local.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset);
    }
}

public sealed class ServiceBusComposeDraft
{
    public string MessageId { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string PropertiesText { get; set; } = string.Empty;

    public bool IsReplay { get; set; }

    public Guid? TargetNamespaceId { get; set; }

    public string TargetEntityPath { get; set; } = string.Empty;

    public string ReplayOverrideSubject { get; set; } = string.Empty;

    public string ReplayOverrideCorrelationId { get; set; } = string.Empty;

    public string ReplayPropertyRenamesText { get; set; } = string.Empty;

    public string ReplayPropertyRemovalsText { get; set; } = string.Empty;

    public bool IsScheduled { get; set; }

    public DateTimeOffset ScheduledDate { get; set; } = DateTimeOffset.Now;

    public TimeSpan ScheduledTime { get; set; } = DateTimeOffset.Now.TimeOfDay;
}

public sealed class ServiceBusComposeResult
{
    private ServiceBusComposeResult(bool wasScheduled, string messageId, long? scheduledSequenceNumber, ScheduledMessageEntry? scheduledEntry)
    {
        WasScheduled = wasScheduled;
        MessageId = messageId;
        ScheduledSequenceNumber = scheduledSequenceNumber;
        ScheduledEntry = scheduledEntry;
    }

    public bool WasScheduled { get; }

    public string MessageId { get; }

    public long? ScheduledSequenceNumber { get; }

    public ScheduledMessageEntry? ScheduledEntry { get; }

    public static ServiceBusComposeResult Sent(string messageId) =>
        new(false, messageId, null, null);

    public static ServiceBusComposeResult Scheduled(string messageId, long sequenceNumber, ScheduledMessageEntry scheduledEntry) =>
        new(true, messageId, sequenceNumber, scheduledEntry);
}

public sealed class ServiceBusBatchReplayRequest
{
    public string TargetEntityPath { get; set; } = string.Empty;

    public string OverrideSubject { get; set; } = string.Empty;

    public string OverrideCorrelationId { get; set; } = string.Empty;

    public string PropertyRenamesText { get; set; } = string.Empty;

    public string PropertyRemovalsText { get; set; } = string.Empty;
}

public sealed partial class ServiceBusNamespaceItemViewModel : ObservableObject
{
    public ServiceBusNamespace Namespace { get; }

    public IAsyncRelayCommand OpenScheduledCommand { get; }

    public IAsyncRelayCommand RemoveCommand { get; }

    public ObservableCollection<ServiceBusEntityItemViewModel> Queues { get; } = [];

    public ObservableCollection<ServiceBusEntityItemViewModel> Topics { get; } = [];

    [ObservableProperty]
    public partial IServiceBusClient? Client { get; set; }

    [ObservableProperty]
    public partial bool IsConnecting { get; set; }

    [ObservableProperty]
    public partial string? ConnectionError { get; set; }

    [ObservableProperty]
    public partial bool IsEntityLoading { get; set; }

    [ObservableProperty]
    public partial string? EntityLoadError { get; set; }

    public bool HasEntityLoadError => !string.IsNullOrWhiteSpace(EntityLoadError);

    public bool HasQueues => Queues.Count > 0;

    public bool HasTopics => Topics.Count > 0;

    public bool ShowEntityEmptyState => IsConnected && !IsEntityLoading && !HasQueues && !HasTopics && !HasEntityLoadError;

    public string Alias => Namespace.Alias;

    public string FullyQualifiedNamespace => Namespace.FullyQualifiedNamespace;

    public string StatusText => IsConnecting
        ? "Connecting..."
        : Client is not null
            ? "Connected"
            : string.IsNullOrWhiteSpace(ConnectionError)
                ? "Not connected"
                : ConnectionError;

    public bool IsConnected => Client is not null;

    public ServiceBusNamespaceItemViewModel(
        ServiceBusNamespace serviceBusNamespace,
        Func<ServiceBusNamespaceItemViewModel, Task> openScheduledAction,
        Func<ServiceBusNamespaceItemViewModel, Task> removeAction)
    {
        Namespace = serviceBusNamespace;
        OpenScheduledCommand = new AsyncRelayCommand(() => openScheduledAction(this));
        RemoveCommand = new AsyncRelayCommand(() => removeAction(this));

        Queues.CollectionChanged += (_, _) => RaiseEntityCollectionChanged();
        Topics.CollectionChanged += (_, _) => RaiseEntityCollectionChanged();
    }

    partial void OnClientChanged(IServiceBusClient? value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ShowEntityEmptyState));
    }

    partial void OnIsConnectingChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnConnectionErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsEntityLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEntityEmptyState));
    }

    partial void OnEntityLoadErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasEntityLoadError));
        OnPropertyChanged(nameof(ShowEntityEmptyState));
    }

    public void SetQueues(IEnumerable<ServiceBusEntityItemViewModel> queues)
    {
        Queues.Clear();
        foreach (var queue in queues)
        {
            Queues.Add(queue);
        }
    }

    public void SetTopics(IEnumerable<ServiceBusEntityItemViewModel> topics)
    {
        Topics.Clear();
        foreach (var topic in topics)
        {
            Topics.Add(topic);
        }
    }

    public void ClearEntities()
    {
        Queues.Clear();
        Topics.Clear();
    }

    private void RaiseEntityCollectionChanged()
    {
        OnPropertyChanged(nameof(HasQueues));
        OnPropertyChanged(nameof(HasTopics));
        OnPropertyChanged(nameof(ShowEntityEmptyState));
    }
}

public sealed partial class ServiceBusEntityItemViewModel : ObservableObject
{
    private readonly Func<ServiceBusEntityItemViewModel, Task> _openActiveAction;
    private readonly Func<ServiceBusEntityItemViewModel, Task> _openDlqAction;
    private readonly Func<ServiceBusEntityItemViewModel, Task> _toggleExpandedAction;
    private readonly Func<ServiceBusEntityItemViewModel, Task> _toggleEnabledAction;

    public ServiceBusNamespaceItemViewModel NamespaceItem { get; }

    public SbEntityInfo Entity { get; }

    public IAsyncRelayCommand OpenActiveCommand { get; }

    public IAsyncRelayCommand OpenDlqCommand { get; }

    public IAsyncRelayCommand ToggleExpandedCommand { get; }

    public IAsyncRelayCommand ToggleEnabledCommand { get; }

    public ObservableCollection<ServiceBusEntityItemViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingChildren { get; set; }

    [ObservableProperty]
    public partial string? ChildLoadError { get; set; }

    [ObservableProperty]
    public partial string? OperationMessage { get; set; }

    public string Name => Entity.Name;

    public string EntityPath => Entity.EntityPath;

    public string StatusText => Entity.IsDisabled ? "Disabled" : "Active";

    public string ToggleEnabledLabel => Entity.IsDisabled ? "Enable" : "Disable";

    public string ActiveCountText => $"Active: {Entity.Stats?.ActiveMessageCount ?? 0}";

    public string DeadLetterCountText => $"DLQ: {Entity.Stats?.DeadLetterMessageCount ?? 0}";

    public bool SupportsMessageModes => !Entity.IsTopic;

    public bool IsTopic => Entity.IsTopic;

    public bool HasChildLoadError => !string.IsNullOrWhiteSpace(ChildLoadError);

    public bool ShowChildren => IsExpanded;

    public bool ShowEmptyChildrenState => IsExpanded && !IsLoadingChildren && Children.Count == 0 && !HasChildLoadError;

    public string ExpandCollapseLabel => IsExpanded ? "Hide subscriptions" : "Show subscriptions";

    public ServiceBusEntityItemViewModel(
        ServiceBusNamespaceItemViewModel namespaceItem,
        SbEntityInfo entity,
        Func<ServiceBusEntityItemViewModel, Task> openActiveAction,
        Func<ServiceBusEntityItemViewModel, Task> openDlqAction,
        Func<ServiceBusEntityItemViewModel, Task> toggleExpandedAction,
        Func<ServiceBusEntityItemViewModel, Task> toggleEnabledAction)
    {
        NamespaceItem = namespaceItem;
        Entity = entity;
        _openActiveAction = openActiveAction;
        _openDlqAction = openDlqAction;
        _toggleExpandedAction = toggleExpandedAction;
        _toggleEnabledAction = toggleEnabledAction;

        OpenActiveCommand = new AsyncRelayCommand(() => _openActiveAction(this));
        OpenDlqCommand = new AsyncRelayCommand(() => _openDlqAction(this));
        ToggleExpandedCommand = new AsyncRelayCommand(() => _toggleExpandedAction(this));
        ToggleEnabledCommand = new AsyncRelayCommand(() => _toggleEnabledAction(this));

        Children.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowEmptyChildrenState));
        };
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowChildren));
        OnPropertyChanged(nameof(ExpandCollapseLabel));
        OnPropertyChanged(nameof(ShowEmptyChildrenState));
    }

    partial void OnIsLoadingChildrenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyChildrenState));
    }

    partial void OnChildLoadErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasChildLoadError));
        OnPropertyChanged(nameof(ShowEmptyChildrenState));
    }

    public void SetChildren(IEnumerable<ServiceBusEntityItemViewModel> children)
    {
        Children.Clear();
        foreach (var child in children)
        {
            Children.Add(child);
        }
    }

    public void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToggleEnabledLabel));
    }
}

public sealed partial class ServiceBusTabViewModel : ObservableObject
{
    private const string FieldApplicationProperty = "application-property";
    private const string FieldEnqueuedTime = "enqueued-time";
    private const string FieldDeliveryCount = "delivery-count";
    private const string FieldSequenceNumber = "sequence-number";

    private const string OperatorContains = "contains";
    private const string OperatorEquals = "equals";
    private const string OperatorNotEquals = "not-equals";
    private const string OperatorRegex = "regex";
    private const string OperatorBefore = "before";
    private const string OperatorOnOrBefore = "on-or-before";
    private const string OperatorAfter = "after";
    private const string OperatorOnOrAfter = "on-or-after";
    private const string OperatorGreaterThan = "gt";
    private const string OperatorGreaterThanOrEqual = "gte";
    private const string OperatorLessThan = "lt";
    private const string OperatorLessThanOrEqual = "lte";

    private const string ColumnEnqueued = "enqueued";
    private const string ColumnMessageId = "message-id";
    private const string ColumnCorrelationId = "correlation-id";
    private const string ColumnSubject = "subject";
    private const string ColumnDelivery = "delivery";
    private const string ColumnExpires = "expires";
    private const string ColumnContentType = "content-type";
    private const string ColumnSession = "session";
    private const string ColumnPartitionKey = "partition-key";
    private const string ColumnDeadLetterReason = "dead-letter-reason";

    private readonly UiStateRepository _uiState;
    private readonly int _pageSize;
    private bool _loadingPreferences;

    public string Id { get; }

    public Guid NamespaceId { get; }

    public string NamespaceAlias { get; }

    public string Title { get; }

    public string EntityPath { get; }

    public bool IsDlq { get; }

    public bool IsScheduled { get; }

    public bool IsMessageTab => !IsScheduled;

    public IServiceBusClient Client { get; }

    public ObservableCollection<SbMessage> Messages { get; } = [];

    public ObservableCollection<ServiceBusVisibleMessageItemViewModel> VisibleMessages { get; } = [];

    public ObservableCollection<SbMessage> SelectedMessages { get; } = [];

    public ObservableCollection<SavedFilter> SavedFilters { get; } = [];

    public ObservableCollection<AdvancedFilterRuleViewModel> AdvancedRules { get; } = [];

    public ObservableCollection<string> CustomPropertyColumns { get; } = [];

    public ObservableCollection<ScheduledMessageItemViewModel> ScheduledMessages { get; } = [];

    [ObservableProperty]
    public partial SbMessage? SelectedMessage { get; set; }

    [ObservableProperty]
    public partial ScheduledMessageItemViewModel? SelectedScheduledMessage { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? ScheduledActionError { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PendingFilterName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool FiltersEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool AdvancedFilterEnabled { get; set; }

    [ObservableProperty]
    public partial string NewCustomPropertyColumn { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RowDensity { get; set; } = "default";

    [ObservableProperty]
    public partial SavedFilter? SelectedSavedFilter { get; set; }

    [ObservableProperty]
    public partial bool ShowMessageIdField { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowSubjectField { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowCorrelationIdField { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowEnqueuedField { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowDeliveryField { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowExpiresField { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowContentTypeField { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowSessionField { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowPartitionKeyField { get; set; }

    [ObservableProperty]
    public partial bool ShowDeadLetterReasonField { get; set; } = true;

    [ObservableProperty]
    public partial long? TotalAvailableCount { get; set; }

    public string HeaderText => IsActive
        ? $"● {Title}{TabModeSuffix}"
        : $"{Title}{TabModeSuffix}";

    public string SummaryText => IsScheduled
        ? $"{NamespaceAlias} / scheduled messages"
        : $"{NamespaceAlias} / {EntityPath}";

    public string FilterScopeKey => IsMessageTab ? $"{NamespaceId:D}:{EntityPath}" : string.Empty;

    public string PreferenceScopeKey => string.IsNullOrWhiteSpace(FilterScopeKey)
        ? string.Empty
        : $"{FilterScopeKey}:{(IsDlq ? "dlq" : "active")}";

    public int RequestedWindowSize { get; private set; }

    public bool HasMessages => Messages.Count > 0;

    public bool HasScheduledMessages => ScheduledMessages.Count > 0;

    public bool HasSavedFilters => SavedFilters.Count > 0;

    public bool HasAdvancedRules => AdvancedRules.Count > 0;

    public bool CanAddCustomPropertyColumn => !string.IsNullOrWhiteSpace(NewCustomPropertyColumn);

    public bool HasTextFilter => !string.IsNullOrWhiteSpace(FilterText);

    public bool HasEnabledAdvancedRules => AdvancedRules.Any(rule => rule.Enabled && IsRuleConfigured(rule));

    public bool HasAnySavedCriteria => HasTextFilter || AdvancedRules.Any(IsRuleConfigured);

    public bool IsFilteringActive => FiltersEnabled && (HasTextFilter || (AdvancedFilterEnabled && HasEnabledAdvancedRules));

    public bool HasSelectedSavedFilter => SelectedSavedFilter is not null;

    public bool ShowEmptyState => !IsLoading && !HasItems && string.IsNullOrWhiteSpace(ErrorMessage);

    public bool ShowFilteredEmptyState => IsMessageTab
        && !IsLoading
        && string.IsNullOrWhiteSpace(ErrorMessage)
        && Messages.Count > 0
        && VisibleMessages.Count == 0;

    public bool HasItems => IsScheduled ? HasScheduledMessages : HasMessages;

    public bool HasSelectedMessage => SelectedMessage is not null;

    public int BatchSelectionCount => SelectedMessages.Count;

    public bool HasBatchSelection => BatchSelectionCount > 0;

    public bool CanBatchReplayDeadLetter => IsDlq && HasBatchSelection;

    public bool ShowNoSelectionState => !HasSelectedMessage;

    public bool HasSelectedScheduledMessage => SelectedScheduledMessage is not null;

    public bool ShowNoScheduledSelectionState => IsScheduled && !HasSelectedScheduledMessage;

    public bool CanSaveCurrentFilter => IsMessageTab
        && !string.IsNullOrWhiteSpace(PendingFilterName)
        && HasAnySavedCriteria;

    public bool CanLoadMore => IsMessageTab
        && !IsLoading
        && TotalAvailableCount is long total
        && Messages.Count < total;

    public Visibility MessageIdVisibility => ShowMessageIdField ? Visibility.Visible : Visibility.Collapsed;

    public ListViewSelectionMode MessageSelectionMode => IsDlq ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single;

    public Visibility SubjectVisibility => ShowSubjectField ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CorrelationIdVisibility => ShowCorrelationIdField ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EnqueuedVisibility => ShowEnqueuedField ? Visibility.Visible : Visibility.Collapsed;

    public Thickness MessageCardPadding => RowDensity switch
    {
        "compact" => new Thickness(10, 6, 10, 6),
        "comfort" => new Thickness(12, 14, 12, 14),
        _ => new Thickness(10)
    };

    public Visibility LoadMoreVisibility => CanLoadMore ? Visibility.Visible : Visibility.Collapsed;

    public string LoadMoreButtonLabel
    {
        get
        {
            if (!CanLoadMore)
            {
                return "Load more";
            }

            var remaining = TotalAvailableCount.GetValueOrDefault() - Messages.Count;
            var nextSize = Math.Min(remaining, _pageSize);
            return $"Load more (+{nextSize})";
        }
    }

    public string SelectedMessageId => SelectedMessage?.MessageId ?? string.Empty;

    public string SelectedMessageSubject => SelectedMessage?.Subject ?? string.Empty;

    public string SelectedMessageCorrelationId => SelectedMessage?.CorrelationId ?? string.Empty;

    public string SelectedMessageSessionId => SelectedMessage?.SessionId ?? string.Empty;

    public bool HasSelectedMessageSessionId => !string.IsNullOrWhiteSpace(SelectedMessage?.SessionId);

    public bool SupportsReplay => !IsDlq;

    public string SelectedMessageBody => SelectedMessage?.Body ?? string.Empty;

    public string SelectedMessageProperties => SelectedMessage is null || SelectedMessage.ApplicationProperties.Count == 0
        ? "None"
        : string.Join(Environment.NewLine, SelectedMessage.ApplicationProperties.Select(pair => $"{pair.Key}: {pair.Value}"));

    public string SelectedMessageSystemProperties => SelectedMessage is null
        ? "None"
        : string.Join(Environment.NewLine, BuildSystemPropertyLines(SelectedMessage));

    public string SelectedScheduledMessageId => SelectedScheduledMessage?.MessageId ?? string.Empty;

    public string SelectedScheduledSubject => SelectedScheduledMessage?.Subject ?? string.Empty;

    public string SelectedScheduledCorrelationId => SelectedScheduledMessage?.CorrelationId ?? string.Empty;

    public string SelectedScheduledEntityPath => SelectedScheduledMessage?.EntityPath ?? string.Empty;

    public string SelectedScheduledSequenceNumber => SelectedScheduledMessage?.SequenceNumber.ToString() ?? string.Empty;

    public string SelectedScheduledEnqueueTime => SelectedScheduledMessage?.ScheduledEnqueueTimeText ?? string.Empty;

    public string SelectedScheduledStatus => SelectedScheduledMessage?.StatusText ?? string.Empty;

    public string MessageCountSummary => IsScheduled
        ? (HasScheduledMessages ? $"Showing {ScheduledMessages.Count} scheduled message(s)" : "No scheduled messages loaded")
        : BuildMessageCountSummary();

    public string BatchSelectionSummary => HasBatchSelection
        ? $"{BatchSelectionCount} message(s) selected"
        : "No messages selected";

    public IReadOnlyList<string> SuggestedCustomPropertyColumns => Messages
        .SelectMany(message => message.ApplicationProperties.Keys)
        .Where(static key => !string.IsNullOrWhiteSpace(key))
        .Select(static key => key.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(key => CustomPropertyColumns.All(existing => !string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)))
        .Take(6)
        .ToList();

    public ServiceBusTabViewModel(
        ServiceBusNamespaceItemViewModel namespaceItem,
        SbEntityInfo entity,
        bool isDlq,
        UiStateRepository uiState,
        int pageSize)
        : this(namespaceItem, entity.Name, entity.EntityPath, isDlq, isScheduled: false, uiState, pageSize)
    {
    }

    private ServiceBusTabViewModel(
        ServiceBusNamespaceItemViewModel namespaceItem,
        string title,
        string entityPath,
        bool isDlq,
        bool isScheduled,
        UiStateRepository uiState,
        int pageSize)
    {
        _uiState = uiState;
        _pageSize = Math.Max(pageSize, 1);

        NamespaceId = namespaceItem.Namespace.Id;
        NamespaceAlias = namespaceItem.Alias;
        Title = title;
        EntityPath = entityPath;
        IsDlq = isDlq;
        IsScheduled = isScheduled;
        Client = namespaceItem.Client ?? throw new InvalidOperationException("Namespace client must exist before opening a tab.");
        Id = isScheduled
            ? $"{namespaceItem.Namespace.Id:D}:scheduled"
            : $"{namespaceItem.Namespace.Id:D}:{entityPath}:{(isDlq ? "dlq" : "active")}";
        RequestedWindowSize = isScheduled ? 0 : _pageSize;

        Messages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowFilteredEmptyState));
            OnPropertyChanged(nameof(MessageCountSummary));
            OnPropertyChanged(nameof(CanLoadMore));
            OnPropertyChanged(nameof(LoadMoreVisibility));
            OnPropertyChanged(nameof(LoadMoreButtonLabel));
        };

        VisibleMessages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowFilteredEmptyState));
            OnPropertyChanged(nameof(MessageCountSummary));
        };

        SavedFilters.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSavedFilters));
        };

        ScheduledMessages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasScheduledMessages));
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(MessageCountSummary));
        };

        SelectedMessages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(BatchSelectionCount));
            OnPropertyChanged(nameof(HasBatchSelection));
            OnPropertyChanged(nameof(CanBatchReplayDeadLetter));
            OnPropertyChanged(nameof(BatchSelectionSummary));
        };

        AdvancedRules.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAdvancedRules));
            OnPropertyChanged(nameof(HasEnabledAdvancedRules));
            OnPropertyChanged(nameof(HasAnySavedCriteria));
            OnPropertyChanged(nameof(IsFilteringActive));
            SaveCurrentFilterCommand.NotifyCanExecuteChanged();
        };

        CustomPropertyColumns.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SuggestedCustomPropertyColumns));
            RefreshVisibleMessages();
        };

        if (IsMessageTab)
        {
            ReloadSavedFilters();
            LoadMessageListPreferences();
        }
    }

    public static ServiceBusTabViewModel CreateScheduled(ServiceBusNamespaceItemViewModel namespaceItem, UiStateRepository uiState)
    {
        return new ServiceBusTabViewModel(
            namespaceItem,
            namespaceItem.Alias,
            namespaceItem.FullyQualifiedNamespace,
            isDlq: false,
            isScheduled: true,
            uiState,
            pageSize: 1);
    }

    partial void OnSelectedMessageChanged(SbMessage? value)
    {
        OnPropertyChanged(nameof(HasSelectedMessage));
        OnPropertyChanged(nameof(HasSelectedMessageSessionId));
        OnPropertyChanged(nameof(ShowNoSelectionState));
        OnPropertyChanged(nameof(SelectedMessageId));
        OnPropertyChanged(nameof(SelectedMessageSubject));
        OnPropertyChanged(nameof(SelectedMessageCorrelationId));
        OnPropertyChanged(nameof(SelectedMessageSessionId));
        OnPropertyChanged(nameof(SelectedMessageBody));
        OnPropertyChanged(nameof(SelectedMessageProperties));
        OnPropertyChanged(nameof(SelectedMessageSystemProperties));
    }

    partial void OnSelectedScheduledMessageChanged(ScheduledMessageItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedScheduledMessage));
        OnPropertyChanged(nameof(ShowNoScheduledSelectionState));
        OnPropertyChanged(nameof(SelectedScheduledMessageId));
        OnPropertyChanged(nameof(SelectedScheduledSubject));
        OnPropertyChanged(nameof(SelectedScheduledCorrelationId));
        OnPropertyChanged(nameof(SelectedScheduledEntityPath));
        OnPropertyChanged(nameof(SelectedScheduledSequenceNumber));
        OnPropertyChanged(nameof(SelectedScheduledEnqueueTime));
        OnPropertyChanged(nameof(SelectedScheduledStatus));
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowFilteredEmptyState));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(LoadMoreVisibility));
        OnPropertyChanged(nameof(LoadMoreButtonLabel));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowFilteredEmptyState));
    }

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(HeaderText));
    }

    partial void OnScheduledActionErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasScheduledActionError));
    }

    partial void OnFilterTextChanged(string value)
    {
        RefreshVisibleMessages();
        OnPropertyChanged(nameof(CanSaveCurrentFilter));
        OnPropertyChanged(nameof(HasTextFilter));
        OnPropertyChanged(nameof(HasAnySavedCriteria));
        OnPropertyChanged(nameof(IsFilteringActive));
        SaveCurrentFilterCommand.NotifyCanExecuteChanged();
    }

    partial void OnPendingFilterNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveCurrentFilter));
        SaveCurrentFilterCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSavedFilterChanged(SavedFilter? value)
    {
        OnPropertyChanged(nameof(HasSelectedSavedFilter));
        ApplySelectedSavedFilterCommand.NotifyCanExecuteChanged();
        DeleteSelectedSavedFilterCommand.NotifyCanExecuteChanged();
    }

    partial void OnFiltersEnabledChanged(bool value)
    {
        RefreshVisibleMessages();
        OnPropertyChanged(nameof(CanSaveCurrentFilter));
        OnPropertyChanged(nameof(IsFilteringActive));
        SaveCurrentFilterCommand.NotifyCanExecuteChanged();
    }

    partial void OnAdvancedFilterEnabledChanged(bool value)
    {
        if (value && AdvancedRules.Count == 0)
        {
            AddAdvancedRule();
        }

        RefreshVisibleMessages();
        OnPropertyChanged(nameof(CanSaveCurrentFilter));
        OnPropertyChanged(nameof(IsFilteringActive));
        SaveCurrentFilterCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewCustomPropertyColumnChanged(string value)
    {
        AddCustomPropertyColumnCommand.NotifyCanExecuteChanged();
    }

    partial void OnRowDensityChanged(string value)
    {
        OnPropertyChanged(nameof(MessageCardPadding));
        PersistManagedPreferences();
        RefreshVisibleMessages();
    }

    partial void OnShowMessageIdFieldChanged(bool value)
    {
        OnPropertyChanged(nameof(MessageIdVisibility));
        PersistManagedPreferences();
    }

    partial void OnShowSubjectFieldChanged(bool value)
    {
        OnPropertyChanged(nameof(SubjectVisibility));
        PersistManagedPreferences();
    }

    partial void OnShowCorrelationIdFieldChanged(bool value)
    {
        OnPropertyChanged(nameof(CorrelationIdVisibility));
        PersistManagedPreferences();
    }

    partial void OnShowEnqueuedFieldChanged(bool value)
    {
        OnPropertyChanged(nameof(EnqueuedVisibility));
        PersistManagedPreferences();
    }

    partial void OnShowDeliveryFieldChanged(bool value)
    {
        PersistManagedPreferences();
        RefreshVisibleMessages();
    }

    partial void OnShowExpiresFieldChanged(bool value)
    {
        PersistManagedPreferences();
        RefreshVisibleMessages();
    }

    partial void OnShowContentTypeFieldChanged(bool value)
    {
        PersistManagedPreferences();
        RefreshVisibleMessages();
    }

    partial void OnShowSessionFieldChanged(bool value)
    {
        PersistManagedPreferences();
        RefreshVisibleMessages();
    }

    partial void OnShowPartitionKeyFieldChanged(bool value)
    {
        PersistManagedPreferences();
        RefreshVisibleMessages();
    }

    partial void OnShowDeadLetterReasonFieldChanged(bool value)
    {
        PersistManagedPreferences();
        RefreshVisibleMessages();
    }

    partial void OnTotalAvailableCountChanged(long? value)
    {
        OnPropertyChanged(nameof(MessageCountSummary));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(LoadMoreVisibility));
        OnPropertyChanged(nameof(LoadMoreButtonLabel));
    }

    public bool HasScheduledActionError => !string.IsNullOrWhiteSpace(ScheduledActionError);

    public string TabModeSuffix => IsScheduled
        ? " (Scheduled)"
        : IsDlq
            ? " (DLQ)"
            : string.Empty;

    public void ExpandRequestedWindow()
    {
        if (!IsMessageTab)
        {
            return;
        }

        var expanded = (long)RequestedWindowSize + _pageSize;
        RequestedWindowSize = expanded >= int.MaxValue ? int.MaxValue : (int)expanded;
        OnPropertyChanged(nameof(LoadMoreButtonLabel));
    }

    public async Task SetBuiltInColumnVisibilityAsync(string columnKey, bool isVisible)
    {
        _loadingPreferences = true;
        try
        {
            switch (columnKey)
            {
                case ColumnMessageId:
                    ShowMessageIdField = isVisible;
                    break;
                case ColumnSubject:
                    ShowSubjectField = isVisible;
                    break;
                case ColumnCorrelationId:
                    ShowCorrelationIdField = isVisible;
                    break;
                case ColumnEnqueued:
                    ShowEnqueuedField = isVisible;
                    break;
                case ColumnDelivery:
                    ShowDeliveryField = isVisible;
                    break;
                case ColumnExpires:
                    ShowExpiresField = isVisible;
                    break;
                case ColumnContentType:
                    ShowContentTypeField = isVisible;
                    break;
                case ColumnSession:
                    ShowSessionField = isVisible;
                    break;
                case ColumnPartitionKey:
                    ShowPartitionKeyField = isVisible;
                    break;
                case ColumnDeadLetterReason:
                    ShowDeadLetterReasonField = isVisible;
                    break;
                default:
                    return;
            }
        }
        finally
        {
            _loadingPreferences = false;
        }

        await PersistMessageListPreferencesAsync();
    }

    public void SetMessages(IEnumerable<SbMessage> messages, long? totalAvailableCount = null)
    {
        Messages.Clear();
        foreach (var message in messages)
        {
            Messages.Add(message);
        }

        TotalAvailableCount = totalAvailableCount;
        RefreshVisibleMessages();
    }

    public void ClearMessages()
    {
        Messages.Clear();
        VisibleMessages.Clear();
        SelectedMessages.Clear();
        SelectedMessage = null;
        TotalAvailableCount = null;
    }

    public void SetSelectedMessages(IEnumerable<SbMessage> messages)
    {
        SelectedMessages.Clear();
        foreach (var message in messages)
        {
            SelectedMessages.Add(message);
        }
    }

    public void ClearSelectedMessages() => SelectedMessages.Clear();

    public IReadOnlyList<SbMessage> GetVisibleMessages() => VisibleMessages.Select(item => item.Message).ToList();

    public IReadOnlyList<long> GetVisibleMessageSequenceNumbers() => VisibleMessages
        .Select(item => item.Message.SequenceNumber)
        .Where(static sequenceNumber => sequenceNumber is not null)
        .Select(static sequenceNumber => sequenceNumber!.Value)
        .ToList();

    [RelayCommand]
    private void ToggleFiltersEnabled() => FiltersEnabled = !FiltersEnabled;

    [RelayCommand]
    private void ToggleAdvancedFilterEnabled()
    {
        if (!FiltersEnabled)
        {
            return;
        }

        AdvancedFilterEnabled = !AdvancedFilterEnabled;
    }

    [RelayCommand]
    private void AddAdvancedRule()
    {
        var rule = new AdvancedFilterRuleViewModel();
        AttachAdvancedRule(rule);
        AdvancedRules.Add(rule);
        RefreshVisibleMessages();
    }

    [RelayCommand]
    private void RemoveAdvancedRule(AdvancedFilterRuleViewModel? rule)
    {
        if (rule is null)
        {
            return;
        }

        DetachAdvancedRule(rule);
        AdvancedRules.Remove(rule);
        RefreshVisibleMessages();
    }

    [RelayCommand(CanExecute = nameof(CanAddCustomPropertyColumn))]
    private async Task AddCustomPropertyColumnAsync()
    {
        var normalized = NormalizeCustomPropertyColumnName(NewCustomPropertyColumn);
        if (normalized is null)
        {
            return;
        }

        if (CustomPropertyColumns.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            NewCustomPropertyColumn = string.Empty;
            return;
        }

        CustomPropertyColumns.Add(normalized);
        NewCustomPropertyColumn = string.Empty;
        await PersistMessageListPreferencesAsync();
    }

    [RelayCommand]
    private async Task RemoveCustomPropertyColumnAsync(string? columnName)
    {
        var normalized = NormalizeCustomPropertyColumnName(columnName);
        if (normalized is null)
        {
            return;
        }

        var removed = false;
        for (var index = CustomPropertyColumns.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(CustomPropertyColumns[index], normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CustomPropertyColumns.RemoveAt(index);
            removed = true;
        }

        if (removed)
        {
            await PersistMessageListPreferencesAsync();
        }
    }

    [RelayCommand]
    private void SetRowDensity(string? density)
    {
        if (density is not "compact" and not "default" and not "comfort")
        {
            return;
        }

        RowDensity = density;
    }

    public void SetScheduledMessages(IEnumerable<ScheduledMessageItemViewModel> messages)
    {
        ScheduledMessages.Clear();
        foreach (var message in messages)
        {
            ScheduledMessages.Add(message);
        }

        SelectedScheduledMessage = ScheduledMessages.FirstOrDefault();
    }

    public void ClearScheduledMessages()
    {
        ScheduledMessages.Clear();
        SelectedScheduledMessage = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveCurrentFilter))]
    private async Task SaveCurrentFilterAsync()
    {
        if (string.IsNullOrWhiteSpace(FilterScopeKey))
        {
            return;
        }

        var filter = new SavedFilter
        {
            Name = PendingFilterName.Trim(),
            Value = FilterText,
            FiltersEnabled = FiltersEnabled,
            AdvancedFilterEnabled = AdvancedFilterEnabled,
            AdvancedRules = AdvancedRules.Select(ToSavedRule).ToList(),
        };

        await _uiState.SaveFilterAsync(FilterScopeKey, filter);
        PendingFilterName = string.Empty;
        ReloadSavedFilters(filter.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSavedFilter))]
    private void ApplySelectedSavedFilter()
    {
        if (SelectedSavedFilter is null)
        {
            return;
        }

        FilterText = SelectedSavedFilter.Value;
        FiltersEnabled = SelectedSavedFilter.FiltersEnabled;
        AdvancedFilterEnabled = SelectedSavedFilter.AdvancedFilterEnabled;
        ReplaceAdvancedRules(SelectedSavedFilter.AdvancedRules.Select(FromSavedRule));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSavedFilter))]
    private async Task DeleteSelectedSavedFilterAsync()
    {
        if (SelectedSavedFilter is null || string.IsNullOrWhiteSpace(FilterScopeKey))
        {
            return;
        }

        var deletedFilterName = SelectedSavedFilter.Name;
        await _uiState.DeleteFilterAsync(FilterScopeKey, deletedFilterName);
        ReloadSavedFilters();
    }

    [RelayCommand]
    private async Task ResetMessageListPreferencesAsync()
    {
        if (string.IsNullOrWhiteSpace(PreferenceScopeKey))
        {
            return;
        }

        await _uiState.ResetMessageListPreferencesAsync(PreferenceScopeKey);
        LoadMessageListPreferences();
    }

    private string BuildMessageCountSummary()
    {
        if (!HasMessages)
        {
            return "No messages loaded";
        }

        if (IsFilteringActive)
        {
            return TotalAvailableCount is long total && total > Messages.Count
                ? $"Showing {VisibleMessages.Count} filtered of {Messages.Count} loaded ({total} total)"
                : $"Showing {VisibleMessages.Count} filtered of {Messages.Count} loaded";
        }

        return TotalAvailableCount is long knownTotal && knownTotal > Messages.Count
            ? $"Showing {VisibleMessages.Count} of {knownTotal} messages"
            : $"Showing {VisibleMessages.Count} messages";
    }

    private void RefreshVisibleMessages()
    {
        if (!IsMessageTab)
        {
            return;
        }

        var previousSelection = SelectedMessage;
        var filteredMessages = ApplyFilters(Messages);

        VisibleMessages.Clear();
        foreach (var message in filteredMessages)
        {
            VisibleMessages.Add(CreateVisibleMessageItem(message));
        }

        SelectedMessage = previousSelection is null
            ? VisibleMessages.FirstOrDefault()?.Message
            : VisibleMessages.FirstOrDefault(message => IsSameMessage(message.Message, previousSelection))?.Message ?? VisibleMessages.FirstOrDefault()?.Message;

        OnPropertyChanged(nameof(ShowFilteredEmptyState));
        OnPropertyChanged(nameof(MessageCountSummary));
    }

    public ServiceBusVisibleMessageItemViewModel CreateVisibleMessageItem(SbMessage message)
    {
        return new ServiceBusVisibleMessageItemViewModel(
            message,
            BuildAdditionalFieldText(message),
            BuildCustomPropertyText(message));
    }

    private List<SbMessage> ApplyFilters(IReadOnlyList<SbMessage> source)
    {
        if (!FiltersEnabled)
        {
            return source.ToList();
        }

        IEnumerable<SbMessage> query = source;

        if (HasTextFilter)
        {
            query = query.Where(MatchesTextFilter);
        }

        if (AdvancedFilterEnabled)
        {
            var enabledRules = AdvancedRules
                .Where(rule => rule.Enabled && IsRuleConfigured(rule))
                .ToList();

            if (enabledRules.Count > 0)
            {
                query = query.Where(message => enabledRules.All(rule => MatchesAdvancedRule(message, rule)));
            }
        }

        return query.ToList();
    }

    private bool MatchesTextFilter(SbMessage message)
    {
        return message.MessageId.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || message.CorrelationId?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) == true
            || message.Subject?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) == true
            || message.Body.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildAdditionalFieldText(SbMessage message)
    {
        var lines = new List<string>();

        if (ShowDeliveryField)
        {
            lines.Add($"Delivery: {message.DeliveryCount}");
        }

        if (ShowExpiresField && message.SystemProperties.ExpiresAt is not null)
        {
            lines.Add($"Expires: {message.SystemProperties.ExpiresAt:O}");
        }

        if (ShowContentTypeField && !string.IsNullOrWhiteSpace(message.ContentType))
        {
            lines.Add($"Content-Type: {message.ContentType}");
        }

        if (ShowSessionField && !string.IsNullOrWhiteSpace(message.SessionId))
        {
            lines.Add($"Session: {message.SessionId}");
        }

        if (ShowPartitionKeyField && !string.IsNullOrWhiteSpace(message.SystemProperties.PartitionKey))
        {
            lines.Add($"Partition key: {message.SystemProperties.PartitionKey}");
        }

        if (ShowDeadLetterReasonField && !string.IsNullOrWhiteSpace(message.DeadLetterReason))
        {
            lines.Add($"DLQ reason: {message.DeadLetterReason}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string BuildCustomPropertyText(SbMessage message)
    {
        if (CustomPropertyColumns.Count == 0)
        {
            return string.Empty;
        }

        var lines = CustomPropertyColumns
            .Select(column => $"{column}: {GetCustomPropertyValue(message, column)}")
            .ToList();
        return string.Join(Environment.NewLine, lines);
    }

    private static string? NormalizeCustomPropertyColumnName(string? columnName)
    {
        return string.IsNullOrWhiteSpace(columnName)
            ? null
            : columnName.Trim();
    }

    private static string GetCustomPropertyValue(SbMessage message, string propertyName)
    {
        if (message.ApplicationProperties.TryGetValue(propertyName, out var directValue))
        {
            return Convert.ToString(directValue, CultureInfo.InvariantCulture) ?? "-";
        }

        foreach (var property in message.ApplicationProperties)
        {
            if (!string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Convert.ToString(property.Value, CultureInfo.InvariantCulture) ?? "-";
        }

        return "-";
    }

    private static bool IsRuleConfigured(AdvancedFilterRuleViewModel rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Value))
        {
            return false;
        }

        return !RequiresPropertyName(rule.Field) || !string.IsNullOrWhiteSpace(rule.PropertyName);
    }

    private bool MatchesAdvancedRule(SbMessage message, AdvancedFilterRuleViewModel rule)
    {
        var rawValue = rule.Value.Trim();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        switch (rule.Field)
        {
            case FieldApplicationProperty:
                if (!TryGetApplicationPropertyValue(message, rule.PropertyName.Trim(), out var propertyValue))
                {
                    return false;
                }

                return EvaluateTextOperator(propertyValue, rawValue, rule.Operator);

            case FieldDeliveryCount:
                if (!TryParseLong(rawValue, out var expectedDeliveryCount))
                {
                    return false;
                }

                return EvaluateNumericOperator(message.DeliveryCount, expectedDeliveryCount, rule.Operator);

            case FieldSequenceNumber:
                if (message.SequenceNumber is not long sequenceNumber || !TryParseLong(rawValue, out var expectedSequenceNumber))
                {
                    return false;
                }

                return EvaluateNumericOperator(sequenceNumber, expectedSequenceNumber, rule.Operator);

            case FieldEnqueuedTime:
                if (!TryParseDate(rawValue, out var expectedTime))
                {
                    return false;
                }

                return EvaluateDateOperator(message.EnqueuedAt, expectedTime, rule.Operator);

            default:
                return true;
        }
    }

    private static bool TryGetApplicationPropertyValue(SbMessage message, string propertyName, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        if (message.ApplicationProperties.TryGetValue(propertyName, out var directValue))
        {
            value = Convert.ToString(directValue, CultureInfo.InvariantCulture) ?? string.Empty;
            return true;
        }

        foreach (var property in message.ApplicationProperties)
        {
            if (!string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = Convert.ToString(property.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            return true;
        }

        return false;
    }

    private static bool EvaluateTextOperator(string actual, string expected, string @operator)
    {
        return @operator switch
        {
            OperatorEquals => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            OperatorNotEquals => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            OperatorRegex => SafeRegexMatch(actual, expected),
            _ => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static bool SafeRegexMatch(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool EvaluateNumericOperator(long actual, long expected, string @operator)
    {
        return @operator switch
        {
            OperatorEquals => actual == expected,
            OperatorNotEquals => actual != expected,
            OperatorGreaterThan => actual > expected,
            OperatorGreaterThanOrEqual => actual >= expected,
            OperatorLessThan => actual < expected,
            OperatorLessThanOrEqual => actual <= expected,
            _ => false,
        };
    }

    private static bool TryParseLong(string value, out long parsed) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

    private static bool TryParseDate(string value, out DateTimeOffset parsed)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                   out parsed)
               || DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture,
                   DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                   out parsed);
    }

    private static bool EvaluateDateOperator(DateTimeOffset actual, DateTimeOffset expected, string @operator)
    {
        return @operator switch
        {
            OperatorEquals => actual.UtcDateTime == expected.UtcDateTime,
            OperatorBefore => actual.UtcDateTime < expected.UtcDateTime,
            OperatorOnOrBefore => actual.UtcDateTime <= expected.UtcDateTime,
            OperatorAfter => actual.UtcDateTime > expected.UtcDateTime,
            OperatorOnOrAfter => actual.UtcDateTime >= expected.UtcDateTime,
            _ => false,
        };
    }

    private void AttachAdvancedRule(AdvancedFilterRuleViewModel rule)
    {
        rule.PropertyChanged += OnAdvancedRulePropertyChanged;
    }

    private void DetachAdvancedRule(AdvancedFilterRuleViewModel rule)
    {
        rule.PropertyChanged -= OnAdvancedRulePropertyChanged;
    }

    private void ReplaceAdvancedRules(IEnumerable<AdvancedFilterRuleViewModel> rules)
    {
        foreach (var existingRule in AdvancedRules.ToList())
        {
            DetachAdvancedRule(existingRule);
        }

        AdvancedRules.Clear();
        foreach (var rule in rules)
        {
            AttachAdvancedRule(rule);
            AdvancedRules.Add(rule);
        }

        RefreshVisibleMessages();
        SaveCurrentFilterCommand.NotifyCanExecuteChanged();
    }

    private void OnAdvancedRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshVisibleMessages();
        OnPropertyChanged(nameof(HasEnabledAdvancedRules));
        OnPropertyChanged(nameof(HasAnySavedCriteria));
        OnPropertyChanged(nameof(IsFilteringActive));
        SaveCurrentFilterCommand.NotifyCanExecuteChanged();
    }

    private static bool RequiresPropertyName(string field) => field == FieldApplicationProperty;

    private static AdvancedFilterRuleViewModel FromSavedRule(SavedAdvancedFilterRule savedRule)
    {
        var field = NormalizeField(savedRule.Field);
        return new AdvancedFilterRuleViewModel
        {
            Enabled = savedRule.Enabled,
            Field = field,
            Operator = NormalizeOperator(field, savedRule.Operator),
            PropertyName = savedRule.PropertyName ?? string.Empty,
            Value = savedRule.Value ?? string.Empty,
        };
    }

    private static SavedAdvancedFilterRule ToSavedRule(AdvancedFilterRuleViewModel rule)
    {
        return new SavedAdvancedFilterRule
        {
            Enabled = rule.Enabled,
            Field = rule.Field,
            Operator = rule.Operator,
            PropertyName = string.IsNullOrWhiteSpace(rule.PropertyName) ? null : rule.PropertyName.Trim(),
            Value = string.IsNullOrWhiteSpace(rule.Value) ? null : rule.Value.Trim(),
        };
    }

    private static string NormalizeField(string? field) => field switch
    {
        FieldEnqueuedTime => FieldEnqueuedTime,
        FieldDeliveryCount => FieldDeliveryCount,
        FieldSequenceNumber => FieldSequenceNumber,
        _ => FieldApplicationProperty,
    };

    private static string NormalizeOperator(string field, string? @operator)
    {
        var allowedOperators = GetOperatorOptions(field).Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (@operator is not null && allowedOperators.Contains(@operator))
        {
            return @operator;
        }

        return field switch
        {
            FieldEnqueuedTime => OperatorAfter,
            FieldDeliveryCount => OperatorGreaterThanOrEqual,
            FieldSequenceNumber => OperatorGreaterThanOrEqual,
            _ => OperatorContains,
        };
    }

    public static IReadOnlyList<ServiceBusFilterOperatorOption> GetOperatorOptions(string field) => field switch
    {
        FieldEnqueuedTime =>
        [
            new ServiceBusFilterOperatorOption(OperatorEquals, "Equals"),
            new ServiceBusFilterOperatorOption(OperatorBefore, "Before"),
            new ServiceBusFilterOperatorOption(OperatorOnOrBefore, "On or before"),
            new ServiceBusFilterOperatorOption(OperatorAfter, "After"),
            new ServiceBusFilterOperatorOption(OperatorOnOrAfter, "On or after"),
        ],
        FieldDeliveryCount or FieldSequenceNumber =>
        [
            new ServiceBusFilterOperatorOption(OperatorEquals, "Equals"),
            new ServiceBusFilterOperatorOption(OperatorNotEquals, "Not equals"),
            new ServiceBusFilterOperatorOption(OperatorGreaterThan, ">"),
            new ServiceBusFilterOperatorOption(OperatorGreaterThanOrEqual, ">="),
            new ServiceBusFilterOperatorOption(OperatorLessThan, "<"),
            new ServiceBusFilterOperatorOption(OperatorLessThanOrEqual, "<="),
        ],
        _ =>
        [
            new ServiceBusFilterOperatorOption(OperatorContains, "Contains"),
            new ServiceBusFilterOperatorOption(OperatorEquals, "Equals"),
            new ServiceBusFilterOperatorOption(OperatorNotEquals, "Not equals"),
            new ServiceBusFilterOperatorOption(OperatorRegex, "Regex"),
        ],
    };

    private void ReloadSavedFilters(string? selectedFilterName = null)
    {
        var nameToRestore = selectedFilterName ?? SelectedSavedFilter?.Name;

        SavedFilters.Clear();
        if (string.IsNullOrWhiteSpace(FilterScopeKey))
        {
            SelectedSavedFilter = null;
            return;
        }

        foreach (var filter in _uiState.GetFilters(FilterScopeKey)
                     .OrderBy(filter => filter.Name, StringComparer.OrdinalIgnoreCase))
        {
            SavedFilters.Add(filter);
        }

        SelectedSavedFilter = SavedFilters.FirstOrDefault(filter =>
            !string.IsNullOrWhiteSpace(nameToRestore)
            && string.Equals(filter.Name, nameToRestore, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadMessageListPreferences()
    {
        _loadingPreferences = true;
        try
        {
            var visibleColumns = CreateDefaultManagedColumnVisibility();
            var customPropertyColumns = new List<string>();
            var rowDensity = "default";
            if (!string.IsNullOrWhiteSpace(PreferenceScopeKey))
            {
                var preference = _uiState.GetMessageListPreferences(PreferenceScopeKey);
                rowDensity = preference.RowDensity is "compact" or "default" or "comfort"
                    ? preference.RowDensity
                    : "default";
                foreach (var kvp in preference.BuiltInColumns)
                {
                    if (visibleColumns.ContainsKey(kvp.Key))
                    {
                        visibleColumns[kvp.Key] = kvp.Value;
                    }
                }

                customPropertyColumns = preference.CustomPropertyColumns
                    .Select(NormalizeCustomPropertyColumnName)
                    .Where(static column => column is not null)
                    .Select(static column => column!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            RowDensity = rowDensity;
            ShowMessageIdField = visibleColumns[ColumnMessageId];
            ShowSubjectField = visibleColumns[ColumnSubject];
            ShowCorrelationIdField = visibleColumns[ColumnCorrelationId];
            ShowEnqueuedField = visibleColumns[ColumnEnqueued];
            ShowDeliveryField = visibleColumns[ColumnDelivery];
            ShowExpiresField = visibleColumns[ColumnExpires];
            ShowContentTypeField = visibleColumns[ColumnContentType];
            ShowSessionField = visibleColumns[ColumnSession];
            ShowPartitionKeyField = visibleColumns[ColumnPartitionKey];
            ShowDeadLetterReasonField = visibleColumns[ColumnDeadLetterReason];

            CustomPropertyColumns.Clear();
            foreach (var column in customPropertyColumns)
            {
                CustomPropertyColumns.Add(column);
            }
        }
        finally
        {
            _loadingPreferences = false;
        }
    }

    private void PersistManagedPreferences()
    {
        if (_loadingPreferences || !IsMessageTab)
        {
            return;
        }

        _ = PersistMessageListPreferencesAsync();
    }

    private async Task PersistMessageListPreferencesAsync()
    {
        if (string.IsNullOrWhiteSpace(PreferenceScopeKey))
        {
            return;
        }

        var existingPreference = _uiState.GetMessageListPreferences(PreferenceScopeKey);
        var builtInColumns = existingPreference.BuiltInColumns is null
            ? new Dictionary<string, bool>(StringComparer.Ordinal)
            : new Dictionary<string, bool>(existingPreference.BuiltInColumns, StringComparer.Ordinal);

        foreach (var kvp in BuildManagedColumnVisibility())
        {
            builtInColumns[kvp.Key] = kvp.Value;
        }

        await _uiState.SaveMessageListPreferencesAsync(PreferenceScopeKey, new MessageListPreferences
        {
            RowDensity = RowDensity,
            BuiltInColumns = builtInColumns,
            CustomPropertyColumns = CustomPropertyColumns.ToList()
        });
    }

    private static Dictionary<string, bool> CreateDefaultManagedColumnVisibility() => new(StringComparer.Ordinal)
    {
        [ColumnEnqueued] = true,
        [ColumnMessageId] = true,
        [ColumnCorrelationId] = true,
        [ColumnSubject] = true,
        [ColumnDelivery] = true,
        [ColumnExpires] = true,
        [ColumnContentType] = true,
        [ColumnSession] = true,
        [ColumnPartitionKey] = false,
        [ColumnDeadLetterReason] = true,
    };

    private Dictionary<string, bool> BuildManagedColumnVisibility() => new(StringComparer.Ordinal)
    {
        [ColumnEnqueued] = ShowEnqueuedField,
        [ColumnMessageId] = ShowMessageIdField,
        [ColumnCorrelationId] = ShowCorrelationIdField,
        [ColumnSubject] = ShowSubjectField,
        [ColumnDelivery] = ShowDeliveryField,
        [ColumnExpires] = ShowExpiresField,
        [ColumnContentType] = ShowContentTypeField,
        [ColumnSession] = ShowSessionField,
        [ColumnPartitionKey] = ShowPartitionKeyField,
        [ColumnDeadLetterReason] = ShowDeadLetterReasonField,
    };

    private static bool IsSameMessage(SbMessage left, SbMessage right)
    {
        if (left.SequenceNumber is long leftSequence && right.SequenceNumber is long rightSequence)
        {
            return leftSequence == rightSequence;
        }

        return string.Equals(left.MessageId, right.MessageId, StringComparison.Ordinal);
    }

    private static IEnumerable<string> BuildSystemPropertyLines(SbMessage message)
    {
        yield return $"Enqueued: {message.EnqueuedAt:O}";
        yield return $"Delivery count: {message.DeliveryCount}";

        if (message.SequenceNumber is not null)
        {
            yield return $"Sequence number: {message.SequenceNumber}";
        }

        if (message.SystemProperties.ExpiresAt is not null)
        {
            yield return $"Expires: {message.SystemProperties.ExpiresAt:O}";
        }

        if (message.SystemProperties.LockedUntil is not null)
        {
            yield return $"Locked until: {message.SystemProperties.LockedUntil:O}";
        }

        if (!string.IsNullOrWhiteSpace(message.SystemProperties.PartitionKey))
        {
            yield return $"Partition key: {message.SystemProperties.PartitionKey}";
        }

        if (!string.IsNullOrWhiteSpace(message.DeadLetterReason))
        {
            yield return $"Dead-letter reason: {message.DeadLetterReason}";
        }

        if (!string.IsNullOrWhiteSpace(message.DeadLetterErrorDescription))
        {
            yield return $"Dead-letter description: {message.DeadLetterErrorDescription}";
        }
    }
}

public sealed class ScheduledMessageItemViewModel
{
    public ScheduledMessageItemViewModel(ScheduledMessageEntry entry)
    {
        Id = entry.Id;
        NamespaceId = entry.NamespaceId;
        EntityPath = entry.EntityPath;
        SequenceNumber = entry.SequenceNumber;
        ScheduledEnqueueTime = entry.ScheduledEnqueueTime;
        MessageId = entry.MessageId ?? string.Empty;
        Subject = entry.Subject ?? string.Empty;
        CorrelationId = entry.CorrelationId ?? string.Empty;
        CreatedAt = entry.CreatedAt;
    }

    public Guid Id { get; }

    public Guid NamespaceId { get; }

    public string EntityPath { get; }

    public long SequenceNumber { get; }

    public DateTimeOffset ScheduledEnqueueTime { get; }

    public string MessageId { get; }

    public string Subject { get; }

    public string CorrelationId { get; }

    public DateTimeOffset CreatedAt { get; }

    public bool CanCancel => ScheduledEnqueueTime > DateTimeOffset.Now;

    public string StatusText => CanCancel ? "Pending" : "Enqueued";

    public string ScheduledEnqueueTimeText => ScheduledEnqueueTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string CreatedAtText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string DisplayMessageId => string.IsNullOrWhiteSpace(MessageId) ? "—" : MessageId;

    public string DisplaySubject => string.IsNullOrWhiteSpace(Subject) ? "—" : Subject;

    public string DisplayCorrelationId => string.IsNullOrWhiteSpace(CorrelationId) ? "—" : CorrelationId;
}

public sealed class ServiceBusWorkspaceTabState
{
    public Guid NamespaceId { get; set; }

    public string EntityPath { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Mode { get; set; } = "active";

    public string TabType { get; set; } = "entity";
}

public sealed partial class AdvancedFilterRuleViewModel : ObservableObject
{
    private static readonly IReadOnlyList<ServiceBusFilterFieldOption> _availableFields =
    [
        new ServiceBusFilterFieldOption("application-property", "Application property"),
        new ServiceBusFilterFieldOption("enqueued-time", "Enqueued time"),
        new ServiceBusFilterFieldOption("delivery-count", "Delivery count"),
        new ServiceBusFilterFieldOption("sequence-number", "Sequence number"),
    ];

    [ObservableProperty]
    public partial Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    [ObservableProperty]
    public partial string Field { get; set; } = "application-property";

    [ObservableProperty]
    public partial string Operator { get; set; } = "contains";

    [ObservableProperty]
    public partial string PropertyName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    public IReadOnlyList<ServiceBusFilterFieldOption> AvailableFields => _availableFields;

    public IReadOnlyList<ServiceBusFilterOperatorOption> AvailableOperators => ServiceBusTabViewModel.GetOperatorOptions(Field);

    public bool RequiresPropertyName => string.Equals(Field, "application-property", StringComparison.Ordinal);

    public Visibility PropertyNameVisibility => RequiresPropertyName ? Visibility.Visible : Visibility.Collapsed;

    public string ValuePlaceholder => Field switch
    {
        "enqueued-time" => "2025-01-31T18:30:00Z",
        "delivery-count" => "3",
        "sequence-number" => "42",
        _ => "Match value",
    };

    partial void OnFieldChanged(string value)
    {
        Operator = value switch
        {
            "enqueued-time" => "after",
            "delivery-count" or "sequence-number" => "gte",
            _ => "contains",
        };

        if (!RequiresPropertyName)
        {
            PropertyName = string.Empty;
        }

        OnPropertyChanged(nameof(AvailableOperators));
        OnPropertyChanged(nameof(RequiresPropertyName));
        OnPropertyChanged(nameof(PropertyNameVisibility));
        OnPropertyChanged(nameof(ValuePlaceholder));
    }
}

public sealed record ServiceBusFilterOperatorOption(string Value, string Label);

public sealed record ServiceBusFilterFieldOption(string Value, string Label);

public sealed class ServiceBusVisibleMessageItemViewModel
{
    public ServiceBusVisibleMessageItemViewModel(SbMessage message, string additionalFieldsText, string customPropertyText)
    {
        Message = message;
        AdditionalFieldsText = additionalFieldsText;
        CustomPropertyText = customPropertyText;
    }

    public SbMessage Message { get; }

    public string MessageId => Message.MessageId;

    public string Subject => Message.Subject ?? string.Empty;

    public string CorrelationId => Message.CorrelationId ?? string.Empty;

    public DateTimeOffset EnqueuedAt => Message.EnqueuedAt;

    public string AdditionalFieldsText { get; }

    public bool HasAdditionalFieldsText => !string.IsNullOrWhiteSpace(AdditionalFieldsText);

    public string CustomPropertyText { get; }

    public bool HasCustomPropertyText => !string.IsNullOrWhiteSpace(CustomPropertyText);
}