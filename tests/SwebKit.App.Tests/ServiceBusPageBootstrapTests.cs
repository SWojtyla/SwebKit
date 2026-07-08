using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.App.Components.Pages;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

public sealed class ServiceBusPageBootstrapTests : TestContext
{
    private readonly AppStateService _appState;
    private readonly FakeServiceBusNamespaceBootstrapper _bootstrapper;

    public ServiceBusPageBootstrapTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var uiState = new UiStateRepository();
        var eventBus = new AppEventBus(NullLogger<AppEventBus>.Instance);
        _appState = new AppStateService(new ProfileRepository(), uiState, eventBus);
        _bootstrapper = new FakeServiceBusNamespaceBootstrapper();

        Services.AddSingleton<IAppEventBus>(eventBus);
        Services.AddSingleton(_appState);
        Services.AddSingleton(uiState);
        Services.AddSingleton<ICredentialStore>(new FakeCredentialStore());
        Services.AddSingleton(new ScheduledMessageRepository());
        Services.AddSingleton(new PageDataCache());
        Services.AddSingleton(new CommandRegistry(uiState));
        Services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        Services.AddSingleton<ISelectionContext>(new FakeSelectionContext());
        Services.AddSingleton<IServiceBusNamespaceBootstrapper>(_bootstrapper);
        Services.AddSingleton<IServiceBusClientFactory>(new FakeServiceBusClientFactory());
        Services.AddSingleton<IServiceBusWarmupCache>(new ServiceBusWarmupCache());
        Services.AddScoped<OperatorWorkspaceService>();
        Services.AddSingleton<IncidentInvestigationLauncher>();
    }

    [Fact]
    public async Task LoadNamespaces_UpdatesPerNamespace_AsBackgroundConnectionsFinish()
    {
        var orders = new ServiceBusNamespace
        {
            Id = Guid.NewGuid(),
            Alias = "orders-live",
            FullyQualifiedNamespace = "orders-live.servicebus.windows.net",
            CredentialKey = "orders-live"
        };
        var payments = new ServiceBusNamespace
        {
            Id = Guid.NewGuid(),
            Alias = "payments-live",
            FullyQualifiedNamespace = "payments-live.servicebus.windows.net",
            CredentialKey = "payments-live"
        };

        await _appState.AddServiceBusNamespaceAsync(orders);
        await _appState.AddServiceBusNamespaceAsync(payments);

        _bootstrapper.ConfigureInitialStates(orders, payments);
        var ordersConnection = _bootstrapper.EnqueuePendingConnection(orders.Id);
        var paymentsConnection = _bootstrapper.EnqueuePendingConnection(payments.Id);

        var cut = RenderComponent<ServiceBusPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("orders-live", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("payments-live", cut.Markup, StringComparison.Ordinal);
            // Active namespace (orders) is connecting — grid shows skeleton
            Assert.Single(cut.FindAll(".sbg-loading"));
        });

        ordersConnection.SetResult(new ServiceBusNamespaceConnectionResult(new FakeServiceBusClient(), null));

        cut.WaitForAssertion(() =>
        {
            // Orders is now connected; skeleton should be gone and no error on active ns
            Assert.Empty(cut.FindAll(".sbg-loading"));
            Assert.Empty(cut.FindAll(".sbg-error"));
        });

        paymentsConnection.SetResult(new ServiceBusNamespaceConnectionResult(null, "Namespace unavailable"));

        // Switch active namespace to payments to observe its error state
        await cut.InvokeAsync(() =>
            cut.Find("select.sbg-ns-select").Change(payments.Id.ToString()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll(".sbg-error"));
            Assert.Contains("Namespace unavailable", cut.Markup, StringComparison.Ordinal);
        });
    }

    private sealed class FakeServiceBusNamespaceBootstrapper : IServiceBusNamespaceBootstrapper
    {
        private IReadOnlyList<ServiceBusNamespaceBootstrapState> _initialStates = [];
        private readonly Dictionary<Guid, Queue<TaskCompletionSource<ServiceBusNamespaceConnectionResult>>> _pendingConnections = new();

        public void ConfigureInitialStates(params ServiceBusNamespace[] namespaces)
        {
            _initialStates = namespaces.Select(ns => new ServiceBusNamespaceBootstrapState(
                ns,
                Client: null,
                ShouldConnect: true,
                ConnectionError: null,
                IsDemo: false)).ToList();
        }

        public TaskCompletionSource<ServiceBusNamespaceConnectionResult> EnqueuePendingConnection(Guid namespaceId)
        {
            var pending = new TaskCompletionSource<ServiceBusNamespaceConnectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingConnections.TryGetValue(namespaceId, out var queue))
            {
                queue = new Queue<TaskCompletionSource<ServiceBusNamespaceConnectionResult>>();
                _pendingConnections[namespaceId] = queue;
            }

            queue.Enqueue(pending);
            return pending;
        }

        public IReadOnlyList<ServiceBusNamespaceBootstrapState> BuildInitialStates(
            IReadOnlyList<ServiceBusNamespace> configuredNamespaces,
            IReadOnlyDictionary<Guid, ServiceBusNamespaceBootstrapSnapshot> cachedSnapshots,
            bool useDemoData) => _initialStates;

        public Task<ServiceBusNamespaceConnectionResult> ConnectAsync(ServiceBusNamespace ns, CancellationToken ct = default)
        {
            var pending = _pendingConnections[ns.Id].Dequeue();
            ct.Register(() => pending.TrySetCanceled(ct));
            return pending.Task;
        }
    }

    private sealed class FakeServiceBusClient : IServiceBusClient
    {
        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new SbNamespaceInfo { Name = "demo", Endpoint = "demo.servicebus.windows.net" });

        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);

        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);

        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);

        public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) => Task.FromResult(new SbEntityStats());
        public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null) => Task.FromResult<IReadOnlyList<SbMessage>>([]);
        public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default, long? fromSequenceNumber = null) => Task.FromResult<IReadOnlyList<SbMessage>>([]);
        public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default) => Task.FromResult(0);
        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) => Task.FromResult(0L);
        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FakeServiceBusClientFactory : IServiceBusClientFactory
    {
        public IServiceBusClient Create(string connectionString) => new FakeServiceBusClient();

        public IServiceBusClient CreateWithEntra(string fullyQualifiedNamespace) => new FakeServiceBusClient();

        public string ParseFullyQualifiedNamespace(string connectionString) => "test.servicebus.windows.net";
    }

    private sealed class FakeCredentialStore : ICredentialStore
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

    private sealed class FakeSelectionContext : ISelectionContext
    {
        public event Action? SelectionChanged;

        public void SetSelection(string area, object? selected)
        {
            SelectionChanged?.Invoke();
        }

        public T? GetSelection<T>(string area) where T : class => null;
    }
}