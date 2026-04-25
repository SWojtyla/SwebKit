using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.ViewModels.ServiceBus;

namespace SwebKit.WinUI.Tests;

public sealed class ServiceBusPageViewModelTests
{
    [Fact]
    public async Task SaveComposeTemplateAsync_PersistsTemplatePayload()
    {
        using var _ = new AppDataSandbox();
        var harness = CreateHarness();

        var draft = harness.ViewModel.CreateComposeDraft();
        draft.Subject = "Order accepted";
        draft.CorrelationId = "corr-123";
        draft.ContentType = "application/json";
        draft.Body = "{\"status\":\"accepted\"}";
        draft.PropertiesText = "tenant=ops\r\npriority=high";

        var savedTemplate = await harness.ViewModel.SaveComposeTemplateAsync("Order accepted", draft);

        Assert.Equal("Order accepted", savedTemplate.Name);

        var persistedProfiles = new ProfileRepository();
        await persistedProfiles.LoadAsync();
        var storedTemplate = Assert.Single(persistedProfiles.MessageTemplates);

        Assert.Equal(savedTemplate.Id, storedTemplate.Id);
        Assert.Equal("Order accepted", storedTemplate.Subject);
        Assert.Equal("corr-123", storedTemplate.CorrelationId);
        Assert.Equal("application/json", storedTemplate.ContentType);
        Assert.Equal("{\"status\":\"accepted\"}", storedTemplate.Body);
        Assert.Equal("ops", storedTemplate.Properties["tenant"]);
        Assert.Equal("high", storedTemplate.Properties["priority"]);
    }

    [Fact]
    public async Task SendOrScheduleActiveMessageAsync_SchedulesBrokerMessageAndStoresLocalEntry()
    {
        using var _ = new AppDataSandbox();
        var harness = CreateHarness();
        var fakeClient = new TestServiceBusClient
        {
            ScheduledSequenceNumber = 42,
        };

        var namespaceItem = new ServiceBusNamespaceItemViewModel(
            new ServiceBusNamespace
            {
                Alias = "orders",
                FullyQualifiedNamespace = "orders.servicebus.windows.net",
                CredentialKey = "cred-1",
            },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask)
        {
            Client = fakeClient,
        };

        var tab = new ServiceBusTabViewModel(
            namespaceItem,
            new SbEntityInfo
            {
                Name = "orders",
                EntityPath = "orders",
            },
            isDlq: false,
            new UiStateRepository(),
            pageSize: 50);

        harness.ViewModel.Tabs.Add(tab);
        harness.ViewModel.ActiveTab = tab;

        var draft = harness.ViewModel.CreateComposeDraft();
        var future = DateTimeOffset.Now.AddMinutes(30);
        draft.MessageId = "msg-123";
        draft.Subject = "Scheduled order";
        draft.CorrelationId = "corr-456";
        draft.ContentType = "application/json";
        draft.Body = "{\"kind\":\"scheduled\"}";
        draft.PropertiesText = "tenant=ops";
        draft.IsScheduled = true;
        draft.ScheduledDate = new DateTimeOffset(future.Year, future.Month, future.Day, 0, 0, 0, future.Offset);
        draft.ScheduledTime = future.TimeOfDay;

        var result = await harness.ViewModel.SendOrScheduleActiveMessageAsync(draft);

        Assert.True(result.WasScheduled);
        Assert.Equal(42, result.ScheduledSequenceNumber);
        Assert.Equal("orders", fakeClient.LastScheduledEntityPath);
        Assert.NotNull(fakeClient.LastScheduledMessage);
        Assert.Equal("msg-123", fakeClient.LastScheduledMessage!.MessageId);
        Assert.Equal("Scheduled order", fakeClient.LastScheduledMessage.Subject);
        Assert.Equal("corr-456", fakeClient.LastScheduledMessage.CorrelationId);
        Assert.Equal("ops", Assert.IsType<string>(fakeClient.LastScheduledMessage.ApplicationProperties["tenant"]));

        var persistedRepository = new ScheduledMessageRepository();
        await persistedRepository.LoadAsync();
        var storedEntry = Assert.Single(persistedRepository.All);

        Assert.Equal(42, storedEntry.SequenceNumber);
        Assert.Equal(namespaceItem.Namespace.Id, storedEntry.NamespaceId);
        Assert.Equal("orders", storedEntry.EntityPath);
        Assert.Equal("msg-123", storedEntry.MessageId);
        Assert.Equal("Scheduled order", storedEntry.Subject);
        Assert.Equal("corr-456", storedEntry.CorrelationId);
        Assert.NotNull(result.ScheduledEntry);
        Assert.Equal(storedEntry.Id, result.ScheduledEntry!.Id);
    }

    [Fact]
    public void MessageTab_FilterText_UpdatesVisibleMessagesAndSelection()
    {
        var tab = CreateMessageTab(new UiStateRepository());

        tab.SetMessages(
        [
            new SbMessage
            {
                MessageId = "order-1",
                Subject = "Accepted",
                Body = "tenant=ops",
                EnqueuedAt = DateTimeOffset.UtcNow
            },
            new SbMessage
            {
                MessageId = "audit-2",
                Subject = "Ignored",
                Body = "tenant=audit",
                EnqueuedAt = DateTimeOffset.UtcNow
            }
        ]);

        tab.FilterText = "accepted";

        var match = Assert.Single(tab.VisibleMessages);
        Assert.Equal("order-1", match.MessageId);
        Assert.Equal("order-1", tab.SelectedMessage?.MessageId);
    }

    [Fact]
    public async Task MessageTab_SavedFilters_PersistApplyAndDeleteAcrossTabs()
    {
        using var _ = new AppDataSandbox();
        var namespaceId = Guid.NewGuid();

        var initialUiState = new UiStateRepository();
        var initialTab = CreateMessageTab(initialUiState, namespaceId);
        initialTab.FilterText = "tenant=ops";
        initialTab.PendingFilterName = "Ops";

        await initialTab.SaveCurrentFilterCommand.ExecuteAsync(null);

        var reloadedUiState = new UiStateRepository();
        await reloadedUiState.LoadAsync();
        var reloadedTab = CreateMessageTab(reloadedUiState, namespaceId);
        reloadedTab.SelectedSavedFilter = Assert.Single(reloadedTab.SavedFilters);

        reloadedTab.ApplySelectedSavedFilterCommand.Execute(null);

        Assert.Equal("tenant=ops", reloadedTab.FilterText);

        await reloadedTab.DeleteSelectedSavedFilterCommand.ExecuteAsync(null);

        var finalUiState = new UiStateRepository();
        await finalUiState.LoadAsync();
        var finalTab = CreateMessageTab(finalUiState, namespaceId);

        Assert.Empty(finalTab.SavedFilters);
    }

    [Fact]
    public async Task MessageTab_BuiltInFieldPreferences_PersistPerModeScope()
    {
        using var _ = new AppDataSandbox();
        var namespaceId = Guid.NewGuid();

        var initialUiState = new UiStateRepository();
        var activeTab = CreateMessageTab(initialUiState, namespaceId, isDlq: false);

        await activeTab.SetBuiltInColumnVisibilityAsync("subject", false);

        var reloadedUiState = new UiStateRepository();
        await reloadedUiState.LoadAsync();
        var reloadedActiveTab = CreateMessageTab(reloadedUiState, namespaceId, isDlq: false);
        var reloadedDlqTab = CreateMessageTab(reloadedUiState, namespaceId, isDlq: true);

        Assert.False(reloadedActiveTab.ShowSubjectField);
        Assert.True(reloadedDlqTab.ShowSubjectField);

        await reloadedActiveTab.ResetMessageListPreferencesCommand.ExecuteAsync(null);

        var resetUiState = new UiStateRepository();
        await resetUiState.LoadAsync();
        var resetTab = CreateMessageTab(resetUiState, namespaceId, isDlq: false);

        Assert.True(resetTab.ShowSubjectField);
    }

    private static ServiceBusPageHarness CreateHarness()
    {
        var profileRepository = new ProfileRepository();
        var uiStateRepository = new UiStateRepository();
        var appState = new AppStateService(
            profileRepository,
            uiStateRepository,
            new AppEventBus(NullLogger<AppEventBus>.Instance));
        var scheduledRepository = new ScheduledMessageRepository();

        var viewModel = new ServiceBusPageViewModel(
            appState,
            new TestCredentialStore(),
            new TestServiceBusClientFactory(),
            new TestServiceBusNamespaceBootstrapper(),
            scheduledRepository,
            uiStateRepository);

        return new ServiceBusPageHarness(viewModel);
    }

    private static ServiceBusTabViewModel CreateMessageTab(
        UiStateRepository uiStateRepository,
        Guid? namespaceId = null,
        bool isDlq = false,
        string entityPath = "orders")
    {
        var namespaceItem = new ServiceBusNamespaceItemViewModel(
            new ServiceBusNamespace
            {
                Id = namespaceId ?? Guid.NewGuid(),
                Alias = "orders",
                FullyQualifiedNamespace = "orders.servicebus.windows.net",
                CredentialKey = "cred-1",
            },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask)
        {
            Client = new TestServiceBusClient(),
        };

        return new ServiceBusTabViewModel(
            namespaceItem,
            new SbEntityInfo
            {
                Name = entityPath,
                EntityPath = entityPath,
            },
            isDlq,
            uiStateRepository,
            pageSize: 50);
    }

    private sealed record ServiceBusPageHarness(ServiceBusPageViewModel ViewModel);

    private sealed class TestCredentialStore : ICredentialStore
    {
        public void Save(string key, string secret)
        {
        }

        public string? Get(string key) => null;

        public void Delete(string key)
        {
        }

        public IReadOnlyList<string> ListKeys(string prefix = "") => [];
    }

    private sealed class TestServiceBusClientFactory : IServiceBusClientFactory
    {
        public IServiceBusClient Create(string connectionString) => new TestServiceBusClient();

        public string ParseFullyQualifiedNamespace(string connectionString) => connectionString;
    }

    private sealed class TestServiceBusNamespaceBootstrapper : IServiceBusNamespaceBootstrapper
    {
        public IReadOnlyList<ServiceBusNamespaceBootstrapState> BuildInitialStates(
            IReadOnlyList<ServiceBusNamespace> configuredNamespaces,
            IReadOnlyDictionary<Guid, ServiceBusNamespaceBootstrapSnapshot> cachedSnapshots,
            bool useDemoData) => [];

        public Task<ServiceBusNamespaceConnectionResult> ConnectAsync(ServiceBusNamespace ns, CancellationToken ct = default) =>
            Task.FromResult(new ServiceBusNamespaceConnectionResult(null, null));
    }

    private sealed class TestServiceBusClient : IServiceBusClient
    {
        public long ScheduledSequenceNumber { get; set; } = 1;

        public string? LastScheduledEntityPath { get; private set; }

        public SbMessage? LastScheduledMessage { get; private set; }

        public DateTimeOffset? LastScheduledEnqueueTime { get; private set; }

        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbMessage>>([]);

        public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbMessage>>([]);

        public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default)
        {
            LastScheduledEntityPath = entityPath;
            LastScheduledMessage = message;
            LastScheduledEnqueueTime = scheduledEnqueueTime;
            return Task.FromResult(ScheduledSequenceNumber);
        }

        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> TestConnectionAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class AppDataSandbox : IDisposable
    {
        private readonly string? _originalOverrideRoot;
        private readonly string? _originalAppData;
        private readonly string _tempRoot;

        public AppDataSandbox()
        {
            _originalOverrideRoot = Environment.GetEnvironmentVariable("SWEBKIT_APPDATA_ROOT");
            _originalAppData = Environment.GetEnvironmentVariable("APPDATA");
            _tempRoot = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _tempRoot);
            Environment.SetEnvironmentVariable("APPDATA", _tempRoot);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _originalOverrideRoot);
            Environment.SetEnvironmentVariable("APPDATA", _originalAppData);

            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
    }
}