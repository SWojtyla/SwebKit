using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

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
        UiStateRepository uiState)
    {
        _appState = appState;
        _credentialStore = credentialStore;
        _serviceBusClientFactory = serviceBusClientFactory;
        _bootstrapper = bootstrapper;
        _scheduledMessageRepository = scheduledMessageRepository;
        _uiState = uiState;

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
            return;
        }

        var tab = new ServiceBusTabViewModel(entityItem.NamespaceItem, entityItem.Entity, isDlq, _uiState, PeekCount);
        Tabs.Add(tab);
        ActiveTab = tab;
        await LoadTabMessagesAsync(tab, _loadCts.Token);
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
            return;
        }

        var tab = ServiceBusTabViewModel.CreateScheduled(namespaceItem, _uiState);
        Tabs.Add(tab);
        ActiveTab = tab;
        await RefreshScheduledMessagesAsync(tab);
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
    private const string ColumnEnqueued = "enqueued";
    private const string ColumnMessageId = "message-id";
    private const string ColumnCorrelationId = "correlation-id";
    private const string ColumnSubject = "subject";

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

    public ObservableCollection<SbMessage> VisibleMessages { get; } = [];

    public ObservableCollection<SavedFilter> SavedFilters { get; } = [];

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

    public bool HasSelectedSavedFilter => SelectedSavedFilter is not null;

    public bool ShowEmptyState => !IsLoading && !HasItems && string.IsNullOrWhiteSpace(ErrorMessage);

    public bool ShowFilteredEmptyState => IsMessageTab
        && !IsLoading
        && string.IsNullOrWhiteSpace(ErrorMessage)
        && Messages.Count > 0
        && VisibleMessages.Count == 0;

    public bool HasItems => IsScheduled ? HasScheduledMessages : HasMessages;

    public bool HasSelectedMessage => SelectedMessage is not null;

    public bool ShowNoSelectionState => !HasSelectedMessage;

    public bool HasSelectedScheduledMessage => SelectedScheduledMessage is not null;

    public bool ShowNoScheduledSelectionState => IsScheduled && !HasSelectedScheduledMessage;

    public bool CanSaveCurrentFilter => IsMessageTab
        && !string.IsNullOrWhiteSpace(FilterText)
        && !string.IsNullOrWhiteSpace(PendingFilterName);

    public bool CanLoadMore => IsMessageTab
        && !IsLoading
        && TotalAvailableCount is long total
        && Messages.Count < total;

    public Visibility MessageIdVisibility => ShowMessageIdField ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SubjectVisibility => ShowSubjectField ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CorrelationIdVisibility => ShowCorrelationIdField ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EnqueuedVisibility => ShowEnqueuedField ? Visibility.Visible : Visibility.Collapsed;

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
        SaveCurrentFilterCommand.NotifyCanExecuteChanged();
    }

    partial void OnPendingFilterNameChanged(string value)
    {
        SaveCurrentFilterCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSavedFilterChanged(SavedFilter? value)
    {
        OnPropertyChanged(nameof(HasSelectedSavedFilter));
        ApplySelectedSavedFilterCommand.NotifyCanExecuteChanged();
        DeleteSelectedSavedFilterCommand.NotifyCanExecuteChanged();
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
        SelectedMessage = null;
        TotalAvailableCount = null;
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
            FiltersEnabled = true,
            AdvancedFilterEnabled = false,
            AdvancedRules = []
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

        if (!string.IsNullOrWhiteSpace(FilterText))
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
        var filteredMessages = string.IsNullOrWhiteSpace(FilterText)
            ? Messages.ToList()
            : Messages.Where(MatchesTextFilter).ToList();

        VisibleMessages.Clear();
        foreach (var message in filteredMessages)
        {
            VisibleMessages.Add(message);
        }

        SelectedMessage = previousSelection is null
            ? VisibleMessages.FirstOrDefault()
            : VisibleMessages.FirstOrDefault(message => IsSameMessage(message, previousSelection)) ?? VisibleMessages.FirstOrDefault();

        OnPropertyChanged(nameof(ShowFilteredEmptyState));
        OnPropertyChanged(nameof(MessageCountSummary));
    }

    private bool MatchesTextFilter(SbMessage message)
    {
        return message.MessageId.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || message.CorrelationId?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) == true
            || message.Subject?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) == true
            || message.Body.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

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
            if (!string.IsNullOrWhiteSpace(PreferenceScopeKey))
            {
                var preference = _uiState.GetMessageListPreferences(PreferenceScopeKey);
                foreach (var kvp in preference.BuiltInColumns)
                {
                    if (visibleColumns.ContainsKey(kvp.Key))
                    {
                        visibleColumns[kvp.Key] = kvp.Value;
                    }
                }
            }

            ShowMessageIdField = visibleColumns[ColumnMessageId];
            ShowSubjectField = visibleColumns[ColumnSubject];
            ShowCorrelationIdField = visibleColumns[ColumnCorrelationId];
            ShowEnqueuedField = visibleColumns[ColumnEnqueued];
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
            RowDensity = existingPreference.RowDensity,
            BuiltInColumns = builtInColumns,
            CustomPropertyColumns = existingPreference.CustomPropertyColumns?.ToList() ?? []
        });
    }

    private static Dictionary<string, bool> CreateDefaultManagedColumnVisibility() => new(StringComparer.Ordinal)
    {
        [ColumnEnqueued] = true,
        [ColumnMessageId] = true,
        [ColumnCorrelationId] = true,
        [ColumnSubject] = true
    };

    private Dictionary<string, bool> BuildManagedColumnVisibility() => new(StringComparer.Ordinal)
    {
        [ColumnEnqueued] = ShowEnqueuedField,
        [ColumnMessageId] = ShowMessageIdField,
        [ColumnCorrelationId] = ShowCorrelationIdField,
        [ColumnSubject] = ShowSubjectField
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