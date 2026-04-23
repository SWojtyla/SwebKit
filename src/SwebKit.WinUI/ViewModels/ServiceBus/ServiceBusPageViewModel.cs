using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwebKit.Core.Abstractions;
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

    public bool CanSendActiveMessage => ActiveTab is not null && !ActiveTab.IsDlq && !IsBusy;

    public bool CanResubmitSelectedDeadLetter =>
        ActiveTab?.IsDlq == true &&
        ActiveTab.SelectedMessage?.SequenceNumber is not null &&
        !IsBusy;

    public bool CanCompleteSelectedDeadLetter =>
        ActiveTab?.IsDlq == true &&
        ActiveTab.SelectedMessage?.SequenceNumber is not null &&
        !IsBusy;

    public ServiceBusPageViewModel(
        AppStateService appState,
        ICredentialStore credentialStore,
        IServiceBusClientFactory serviceBusClientFactory,
        IServiceBusNamespaceBootstrapper bootstrapper)
    {
        _appState = appState;
        _credentialStore = credentialStore;
        _serviceBusClientFactory = serviceBusClientFactory;
        _bootstrapper = bootstrapper;

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
        };
    }

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

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

            var namespaceItem = new ServiceBusNamespaceItemViewModel(serviceBusNamespace, RemoveNamespaceAsync)
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
        OnPropertyChanged(nameof(CanSendActiveMessage));
        OnPropertyChanged(nameof(CanResubmitSelectedDeadLetter));
        OnPropertyChanged(nameof(CanCompleteSelectedDeadLetter));
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
        OnPropertyChanged(nameof(CanSendActiveMessage));
        OnPropertyChanged(nameof(CanResubmitSelectedDeadLetter));
        OnPropertyChanged(nameof(CanCompleteSelectedDeadLetter));
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

        await LoadTabMessagesAsync(ActiveTab, _loadCts.Token);
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

        var tab = new ServiceBusTabViewModel(entityItem.NamespaceItem, entityItem.Entity, isDlq);
        Tabs.Add(tab);
        ActiveTab = tab;
        await LoadTabMessagesAsync(tab, _loadCts.Token);
    }

    private async Task LoadTabMessagesAsync(ServiceBusTabViewModel tab, CancellationToken cancellationToken)
    {
        tab.IsLoading = true;
        tab.ErrorMessage = null;

        try
        {
            var messages = tab.IsDlq
                ? await tab.Client.PeekDeadLetterAsync(tab.EntityPath, PeekCount, cancellationToken)
                : await tab.Client.PeekMessagesAsync(tab.EntityPath, PeekCount, cancellationToken);

            tab.SetMessages(messages);
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
        OnPropertyChanged(nameof(CanSendActiveMessage));
        OnPropertyChanged(nameof(CanResubmitSelectedDeadLetter));
        OnPropertyChanged(nameof(CanCompleteSelectedDeadLetter));
        ResubmitSelectedDeadLetterCommand.NotifyCanExecuteChanged();
        CompleteSelectedDeadLetterCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class ServiceBusNamespaceItemViewModel : ObservableObject
{
    public ServiceBusNamespace Namespace { get; }

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
        Func<ServiceBusNamespaceItemViewModel, Task> removeAction)
    {
        Namespace = serviceBusNamespace;
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
    public string Id { get; }

    public Guid NamespaceId { get; }

    public string NamespaceAlias { get; }

    public string Title { get; }

    public string EntityPath { get; }

    public bool IsDlq { get; }

    public IServiceBusClient Client { get; }

    public ObservableCollection<SbMessage> Messages { get; } = [];

    [ObservableProperty]
    public partial SbMessage? SelectedMessage { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    public string HeaderText => IsActive
        ? $"● {Title}{(IsDlq ? " (DLQ)" : string.Empty)}"
        : $"{Title}{(IsDlq ? " (DLQ)" : string.Empty)}";

    public string SummaryText => $"{NamespaceAlias} / {EntityPath}";

    public bool HasMessages => Messages.Count > 0;

    public bool ShowEmptyState => !IsLoading && !HasMessages && string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasSelectedMessage => SelectedMessage is not null;

    public bool ShowNoSelectionState => !HasSelectedMessage;

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

    public string MessageCountSummary => HasMessages
        ? $"Showing {Messages.Count} messages"
        : "No messages loaded";

    public ServiceBusTabViewModel(ServiceBusNamespaceItemViewModel namespaceItem, SbEntityInfo entity, bool isDlq)
    {
        NamespaceId = namespaceItem.Namespace.Id;
        NamespaceAlias = namespaceItem.Alias;
        Title = entity.Name;
        EntityPath = entity.EntityPath;
        IsDlq = isDlq;
        Client = namespaceItem.Client ?? throw new InvalidOperationException("Namespace client must exist before opening a tab.");
        Id = $"{namespaceItem.Namespace.Id:D}:{entity.EntityPath}:{(isDlq ? "dlq" : "active")}";

        Messages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(MessageCountSummary));
        };
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

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(HeaderText));
    }

    public void SetMessages(IEnumerable<SbMessage> messages)
    {
        Messages.Clear();
        foreach (var message in messages)
        {
            Messages.Add(message);
        }

        SelectedMessage = Messages.FirstOrDefault();
    }

    public void ClearMessages()
    {
        Messages.Clear();
        SelectedMessage = null;
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