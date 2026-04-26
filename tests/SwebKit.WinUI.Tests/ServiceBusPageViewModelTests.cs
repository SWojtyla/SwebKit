using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.ServiceBus;

namespace SwebKit.WinUI.Tests;

[Collection("AppDataSandbox")]
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
    public void CreateComposeDraftFromMessage_CopiesPayloadAndCanEnableScheduling()
    {
        var harness = CreateHarness();
        var message = new SbMessage
        {
            MessageId = "msg-123",
            Subject = "Accepted",
            CorrelationId = "corr-456",
            ContentType = "application/json",
            Body = "{\"tenant\":\"ops\"}",
            ApplicationProperties = new Dictionary<string, object>
            {
                ["tenant"] = "ops",
                ["priority"] = "high",
            },
        };

        var draft = harness.ViewModel.CreateComposeDraftFromMessage(message, scheduleForLater: true);

        Assert.Equal("msg-123", draft.MessageId);
        Assert.Equal("Accepted", draft.Subject);
        Assert.Equal("corr-456", draft.CorrelationId);
        Assert.Equal("application/json", draft.ContentType);
        Assert.Equal("{\"tenant\":\"ops\"}", draft.Body);
        Assert.Contains("tenant=ops", draft.PropertiesText, StringComparison.Ordinal);
        Assert.Contains("priority=high", draft.PropertiesText, StringComparison.Ordinal);
        Assert.True(draft.IsScheduled);
    }

    [Fact]
    public async Task ExecuteComposeDraftAsync_ReplaysToSelectedNamespaceAndAppliesRemapRules()
    {
        var harness = CreateHarness();
        var sourceClient = new TestServiceBusClient();
        var targetClient = new TestServiceBusClient();

        var sourceNamespace = new ServiceBusNamespaceItemViewModel(
            new ServiceBusNamespace
            {
                Alias = "orders",
                FullyQualifiedNamespace = "orders.servicebus.windows.net",
                CredentialKey = "cred-source",
            },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask)
        {
            Client = sourceClient,
        };

        var targetNamespace = new ServiceBusNamespaceItemViewModel(
            new ServiceBusNamespace
            {
                Alias = "archive",
                FullyQualifiedNamespace = "archive.servicebus.windows.net",
                CredentialKey = "cred-target",
            },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask)
        {
            Client = targetClient,
        };

        harness.ViewModel.Namespaces.Add(sourceNamespace);
        harness.ViewModel.Namespaces.Add(targetNamespace);

        var tab = new ServiceBusTabViewModel(
            sourceNamespace,
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

        var message = new SbMessage
        {
            MessageId = "msg-123",
            Subject = "Original subject",
            CorrelationId = "corr-original",
            ContentType = "application/json",
            Body = "{\"tenant\":\"ops\"}",
            ApplicationProperties = new Dictionary<string, object>
            {
                ["tenant"] = "ops",
                ["obsolete"] = "remove-me",
            },
        };

        var draft = harness.ViewModel.CreateReplayDraftFromMessage(message);
        draft.TargetNamespaceId = targetNamespace.Namespace.Id;
        draft.TargetEntityPath = "orders-archive";
        draft.ReplayOverrideSubject = "Replay subject";
        draft.ReplayOverrideCorrelationId = "corr-replay";
        draft.ReplayPropertyRenamesText = "tenant=tenantId";
        draft.ReplayPropertyRemovalsText = "obsolete";

        var result = await harness.ViewModel.ExecuteComposeDraftAsync(draft);

        Assert.False(result.WasScheduled);
        Assert.Equal("orders-archive", targetClient.LastSentEntityPath);
        Assert.NotNull(targetClient.LastSentMessage);
        Assert.Equal("Replay subject", targetClient.LastSentMessage!.Subject);
        Assert.Equal("corr-replay", targetClient.LastSentMessage.CorrelationId);
        Assert.Equal("ops", Assert.IsType<string>(targetClient.LastSentMessage.ApplicationProperties["tenantId"]));
        Assert.False(targetClient.LastSentMessage.ApplicationProperties.ContainsKey("tenant"));
        Assert.False(targetClient.LastSentMessage.ApplicationProperties.ContainsKey("obsolete"));
        Assert.Null(sourceClient.LastSentMessage);
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
    public async Task ReplaySelectedDeadLettersAsync_ProcessesSelectionInChunksAndAppliesRemapRules()
    {
        var harness = CreateHarness();
        var fakeClient = new TestServiceBusClient();

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
            isDlq: true,
            new UiStateRepository(),
            pageSize: 50);

        tab.SetSelectedMessages(
        [
            new SbMessage { MessageId = "msg-1", SequenceNumber = 1 },
            new SbMessage { MessageId = "msg-2", SequenceNumber = 2 },
            new SbMessage { MessageId = "msg-3", SequenceNumber = 3 },
            new SbMessage { MessageId = "msg-4", SequenceNumber = 4 },
            new SbMessage { MessageId = "msg-5", SequenceNumber = 5 },
            new SbMessage { MessageId = "msg-6", SequenceNumber = 6 },
            new SbMessage { MessageId = "msg-7", SequenceNumber = 7 },
            new SbMessage { MessageId = "msg-8", SequenceNumber = 8 },
            new SbMessage { MessageId = "msg-9", SequenceNumber = 9 },
            new SbMessage { MessageId = "msg-10", SequenceNumber = 10 },
            new SbMessage { MessageId = "msg-11", SequenceNumber = 11 },
            new SbMessage { MessageId = "msg-no-seq" },
        ]);

        harness.ViewModel.Tabs.Add(tab);
        harness.ViewModel.ActiveTab = tab;

        var result = await harness.ViewModel.ReplaySelectedDeadLettersAsync(new ServiceBusBatchReplayRequest
        {
            TargetEntityPath = "orders-replay",
            OverrideSubject = "Replay subject",
            OverrideCorrelationId = "corr-replay",
            PropertyRenamesText = "tenant=tenantId",
            PropertyRemovalsText = "obsolete",
        });

        Assert.Equal(11, result.Succeeded);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        Assert.Collection(
            fakeClient.ResubmitCalls,
            firstChunk =>
            {
                Assert.Equal("orders", firstChunk.EntityPath);
                Assert.Equal("orders-replay", firstChunk.TargetEntityPath);
                Assert.Equal(10, firstChunk.SequenceNumbers.Count);
                Assert.NotNull(firstChunk.RemapRules);
                Assert.Equal("Replay subject", firstChunk.RemapRules!.OverrideSubject);
                Assert.Equal("corr-replay", firstChunk.RemapRules.OverrideCorrelationId);
                Assert.Equal("tenantId", firstChunk.RemapRules.PropertyRenames["tenant"]);
                Assert.Contains("obsolete", firstChunk.RemapRules.PropertyRemoves);
            },
            secondChunk => Assert.Single(secondChunk.SequenceNumbers));
        Assert.Equal(1, fakeClient.PeekDeadLetterCallCount);
        Assert.Empty(tab.SelectedMessages);
        Assert.Null(tab.SelectedMessage);
    }

    [Fact]
    public async Task ReplaySelectedDeadLettersAsync_PreservesDistinctSelectionsWithSameMessageId()
    {
        var harness = CreateHarness();
        var fakeClient = new TestServiceBusClient();

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
            isDlq: true,
            new UiStateRepository(),
            pageSize: 50);

        tab.SetSelectedMessages(
        [
            new SbMessage { MessageId = "duplicate-id", SequenceNumber = 41 },
            new SbMessage { MessageId = "duplicate-id", SequenceNumber = 42 },
        ]);

        harness.ViewModel.Tabs.Add(tab);
        harness.ViewModel.ActiveTab = tab;

        var result = await harness.ViewModel.ReplaySelectedDeadLettersAsync(new ServiceBusBatchReplayRequest
        {
            TargetEntityPath = "orders-replay",
        });

        Assert.Equal(2, result.Succeeded);
        Assert.Collection(
            fakeClient.ResubmitCalls,
            chunk => Assert.Equal(["41", "42"], chunk.SequenceNumbers));
    }

    [Fact]
    public async Task ReplaySelectedDeadLettersAsync_PropagatesCancellation()
    {
        var harness = CreateHarness();
        var fakeClient = new TestServiceBusClient
        {
            ThrowOnResubmit = new OperationCanceledException("cancelled"),
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
            isDlq: true,
            new UiStateRepository(),
            pageSize: 50);

        tab.SetSelectedMessages([new SbMessage { MessageId = "msg-1", SequenceNumber = 1 }]);
        harness.ViewModel.Tabs.Add(tab);
        harness.ViewModel.ActiveTab = tab;

        await Assert.ThrowsAsync<OperationCanceledException>(() => harness.ViewModel.ReplaySelectedDeadLettersAsync(new ServiceBusBatchReplayRequest
        {
            TargetEntityPath = "orders-replay",
        }));
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

    [Fact]
    public async Task MessageTab_AdvancedRules_SaveAndRestoreFilterCriteria()
    {
        using var _ = new AppDataSandbox();
        var namespaceId = Guid.NewGuid();

        var initialUiState = new UiStateRepository();
        var initialTab = CreateMessageTab(initialUiState, namespaceId);
        initialTab.SetMessages(
        [
            new SbMessage
            {
                MessageId = "order-1",
                DeliveryCount = 4,
                EnqueuedAt = DateTimeOffset.UtcNow,
                ApplicationProperties = new Dictionary<string, object>
                {
                    ["tenant"] = "ops",
                },
            },
            new SbMessage
            {
                MessageId = "order-2",
                DeliveryCount = 1,
                EnqueuedAt = DateTimeOffset.UtcNow,
                ApplicationProperties = new Dictionary<string, object>
                {
                    ["tenant"] = "ops",
                },
            },
            new SbMessage
            {
                MessageId = "audit-3",
                DeliveryCount = 5,
                EnqueuedAt = DateTimeOffset.UtcNow,
                ApplicationProperties = new Dictionary<string, object>
                {
                    ["tenant"] = "audit",
                },
            },
        ]);

        initialTab.AddAdvancedRuleCommand.Execute(null);
        var tenantRule = Assert.Single(initialTab.AdvancedRules);
        tenantRule.Field = "application-property";
        tenantRule.PropertyName = "tenant";
        tenantRule.Value = "ops";

        initialTab.AddAdvancedRuleCommand.Execute(null);
        var deliveryRule = initialTab.AdvancedRules.Last();
        deliveryRule.Field = "delivery-count";
        deliveryRule.Operator = "gte";
        deliveryRule.Value = "3";

        initialTab.AdvancedFilterEnabled = true;
        initialTab.PendingFilterName = "Ops high delivery";

        Assert.Single(initialTab.VisibleMessages);
        Assert.Equal("order-1", initialTab.VisibleMessages[0].MessageId);

        await initialTab.SaveCurrentFilterCommand.ExecuteAsync(null);

        var reloadedUiState = new UiStateRepository();
        await reloadedUiState.LoadAsync();
        var reloadedTab = CreateMessageTab(reloadedUiState, namespaceId);
        reloadedTab.SetMessages(
        [
            new SbMessage
            {
                MessageId = "order-1",
                DeliveryCount = 4,
                EnqueuedAt = DateTimeOffset.UtcNow,
                ApplicationProperties = new Dictionary<string, object>
                {
                    ["tenant"] = "ops",
                },
            },
            new SbMessage
            {
                MessageId = "order-2",
                DeliveryCount = 1,
                EnqueuedAt = DateTimeOffset.UtcNow,
                ApplicationProperties = new Dictionary<string, object>
                {
                    ["tenant"] = "ops",
                },
            },
        ]);

        reloadedTab.SelectedSavedFilter = Assert.Single(reloadedTab.SavedFilters);
        reloadedTab.ApplySelectedSavedFilterCommand.Execute(null);

        Assert.True(reloadedTab.AdvancedFilterEnabled);
        Assert.Equal(2, reloadedTab.AdvancedRules.Count);
        Assert.Single(reloadedTab.VisibleMessages);
        Assert.Equal("order-1", reloadedTab.VisibleMessages[0].MessageId);
    }

    [Fact]
    public async Task MessageTab_MessageListPreferences_PersistRowDensityAndCustomColumns()
    {
        using var _ = new AppDataSandbox();
        var namespaceId = Guid.NewGuid();

        var initialUiState = new UiStateRepository();
        var initialTab = CreateMessageTab(initialUiState, namespaceId);
        initialTab.SetRowDensityCommand.Execute("comfort");
        initialTab.NewCustomPropertyColumn = "tenant";
        await initialTab.AddCustomPropertyColumnCommand.ExecuteAsync(null);
        await initialTab.SetBuiltInColumnVisibilityAsync("delivery", false);

        var reloadedUiState = new UiStateRepository();
        await reloadedUiState.LoadAsync();
        var reloadedTab = CreateMessageTab(reloadedUiState, namespaceId);

        Assert.Equal("comfort", reloadedTab.RowDensity);
        Assert.Contains("tenant", reloadedTab.CustomPropertyColumns);
        Assert.False(reloadedTab.ShowDeliveryField);
    }

    [Fact]
    public async Task DeleteFilteredMessagesAsync_DeletesOnlyVisibleMessageSequenceNumbers()
    {
        var harness = CreateHarness();
        var fakeClient = new TestServiceBusClient();

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

        tab.SetMessages(
        [
            new SbMessage { MessageId = "order-1", Subject = "Accepted", SequenceNumber = 1, EnqueuedAt = DateTimeOffset.UtcNow },
            new SbMessage { MessageId = "order-2", Subject = "Ignored", SequenceNumber = 2, EnqueuedAt = DateTimeOffset.UtcNow },
        ]);
        tab.FilterText = "Accepted";

        harness.ViewModel.Tabs.Add(tab);
        harness.ViewModel.ActiveTab = tab;

        var deleted = await harness.ViewModel.DeleteFilteredMessagesAsync();

        Assert.Equal(1, deleted);
        Assert.Equal("orders", fakeClient.LastCompletedEntityPath);
        Assert.Equal([1L], fakeClient.LastCompletedSequenceNumbers);
    }

    [Fact]
    public void SelectTabCommand_PublishesWorkspaceSnapshotWithOpenTabs()
    {
        var harness = CreateHarness();
        var firstTab = CreateMessageTab(new UiStateRepository(), Guid.NewGuid(), entityPath: "orders");
        var secondTab = CreateMessageTab(new UiStateRepository(), firstTab.NamespaceId, entityPath: "orders/dead");

        harness.ViewModel.Tabs.Add(firstTab);
        harness.ViewModel.Tabs.Add(secondTab);

        harness.ViewModel.SelectTabCommand.Execute(secondTab);

        var snapshot = harness.WorkspaceService.GetCurrentSnapshot("service-bus");

        Assert.NotNull(snapshot);
        Assert.Equal(secondTab.Id, snapshot!.RestoreState["activeTabId"]);
        var restoredTabs = System.Text.Json.JsonSerializer.Deserialize<List<ServiceBusWorkspaceTabState>>(snapshot.RestoreState["tabs"]);
        Assert.NotNull(restoredTabs);
        Assert.Equal(2, restoredTabs!.Count);
        Assert.Contains(restoredTabs, tab => tab.EntityPath == "orders");
        Assert.Contains(restoredTabs, tab => tab.EntityPath == "orders/dead");
    }

    [Fact]
    public async Task LoadAsync_AppliesPendingServiceBusWorkspaceRestore()
    {
        var namespaceId = Guid.NewGuid();
        var configuredNamespace = new ServiceBusNamespace
        {
            Id = namespaceId,
            Alias = "orders",
            FullyQualifiedNamespace = "orders.servicebus.windows.net",
            CredentialKey = "cred-1",
        };

        var fakeClient = new TestServiceBusClient();
        var bootstrapper = new StaticBootstrapper(configuredNamespace, fakeClient);
        var harness = CreateHarness(bootstrapper: bootstrapper);

        await harness.AppState.AddServiceBusNamespaceAsync(configuredNamespace);

        var pendingSnapshot = new WorkspaceSnapshot
        {
            Resource = new OperatorResourceReference
            {
                Key = $"service-bus:{namespaceId:D}:orders:active",
                Area = "service-bus",
                Kind = "entity",
                DisplayName = "orders",
                DisplayPath = "orders/orders",
                Summary = "orders",
                Icon = "📨",
            },
            RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["activeTabId"] = $"{namespaceId:D}:orders:active",
                ["namespaceId"] = namespaceId.ToString("D"),
                ["entityPath"] = "orders",
                ["mode"] = "active",
                ["tabType"] = "entity",
                ["tabs"] = System.Text.Json.JsonSerializer.Serialize(new List<ServiceBusWorkspaceTabState>
                {
                    new()
                    {
                        NamespaceId = namespaceId,
                        EntityPath = "orders",
                        Title = "orders",
                        Mode = "active",
                        TabType = "entity",
                    },
                }),
            },
        };

        harness.Navigation.CurrentArea = "dashboard";
        await harness.WorkspaceService.OpenSnapshotAsync(pendingSnapshot, recordRecent: false);

        Assert.Null(harness.ViewModel.ActiveTab);

        await harness.ViewModel.LoadAsync();

        Assert.NotNull(harness.ViewModel.ActiveTab);
        Assert.Equal("orders", harness.ViewModel.ActiveTab!.EntityPath);
        Assert.Single(harness.ViewModel.Tabs);
    }

    private static ServiceBusPageHarness CreateHarness(IServiceBusNamespaceBootstrapper? bootstrapper = null)
    {
        var profileRepository = new ProfileRepository();
        var uiStateRepository = new UiStateRepository();
        var appState = new AppStateService(
            profileRepository,
            uiStateRepository,
            new AppEventBus(NullLogger<AppEventBus>.Instance));
        var scheduledRepository = new ScheduledMessageRepository();
        var navigation = new TestShellNavigationService();
        var workspaceService = new OperatorWorkspaceService(appState, uiStateRepository, navigation, []);

        var viewModel = new ServiceBusPageViewModel(
            appState,
            new TestCredentialStore(),
            bootstrapper ?? new TestServiceBusNamespaceBootstrapper(),
            scheduledRepository,
            uiStateRepository,
            workspaceService);

        return new ServiceBusPageHarness(viewModel, workspaceService, navigation, appState);
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

    private sealed record ServiceBusPageHarness(
        ServiceBusPageViewModel ViewModel,
        OperatorWorkspaceService WorkspaceService,
        TestShellNavigationService Navigation,
        AppStateService AppState);

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

    private sealed class StaticBootstrapper : IServiceBusNamespaceBootstrapper
    {
        private readonly ServiceBusNamespace _serviceBusNamespace;
        private readonly IServiceBusClient _client;

        public StaticBootstrapper(ServiceBusNamespace serviceBusNamespace, IServiceBusClient client)
        {
            _serviceBusNamespace = serviceBusNamespace;
            _client = client;
        }

        public IReadOnlyList<ServiceBusNamespaceBootstrapState> BuildInitialStates(
            IReadOnlyList<ServiceBusNamespace> configuredNamespaces,
            IReadOnlyDictionary<Guid, ServiceBusNamespaceBootstrapSnapshot> cachedSnapshots,
            bool useDemoData)
        {
            return configuredNamespaces
                .Where(candidate => candidate.Id == _serviceBusNamespace.Id)
                .Select(candidate => new ServiceBusNamespaceBootstrapState(candidate, _client, ShouldConnect: false, ConnectionError: null, IsDemo: false))
                .ToList();
        }

        public Task<ServiceBusNamespaceConnectionResult> ConnectAsync(ServiceBusNamespace ns, CancellationToken ct = default) =>
            Task.FromResult(new ServiceBusNamespaceConnectionResult(_client, null));
    }

    private sealed class TestShellNavigationService : IShellNavigationService
    {
        public string? CurrentArea { get; set; } = "service-bus";

        public event Action? NavigationChanged;

        public void NavigateTo(string area, object? parameter = null)
        {
            CurrentArea = area;
            NavigationChanged?.Invoke();
        }
    }

    private sealed class TestServiceBusClient : IServiceBusClient
    {
        public List<ResubmitCall> ResubmitCalls { get; } = [];

        public int PeekDeadLetterCallCount { get; private set; }

        public long ScheduledSequenceNumber { get; set; } = 1;

        public string? LastSentEntityPath { get; private set; }

        public SbMessage? LastSentMessage { get; private set; }

        public string? LastScheduledEntityPath { get; private set; }

        public SbMessage? LastScheduledMessage { get; private set; }

        public DateTimeOffset? LastScheduledEnqueueTime { get; private set; }

        public Exception? ThrowOnResubmit { get; set; }

        public string? LastCompletedEntityPath { get; private set; }

        public IReadOnlyList<long> LastCompletedSequenceNumbers { get; private set; } = [];

        public string? LastPurgedEntityPath { get; private set; }

        public bool? LastPurgedDeadLetter { get; private set; }

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

        public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default)
        {
            PeekDeadLetterCallCount++;
            return Task.FromResult<IReadOnlyList<SbMessage>>([]);
        }

        public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default)
        {
            LastCompletedEntityPath = entityPath;
            LastCompletedSequenceNumbers = sequenceNumbers.ToList();
            return Task.FromResult(sequenceNumbers.Count);
        }

        public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default)
        {
            LastPurgedEntityPath = entityPath;
            LastPurgedDeadLetter = deadLetter;
            return Task.FromResult(0);
        }

        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default)
        {
            LastSentEntityPath = entityPath;
            LastSentMessage = CloneMessage(message);
            return Task.CompletedTask;
        }

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

        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default)
        {
            if (ThrowOnResubmit is not null)
            {
                throw ThrowOnResubmit;
            }

            ResubmitCalls.Add(new ResubmitCall(entityPath, sequenceNumbers.ToList(), targetEntityPath, remapRules is null ? null : CloneRemapRules(remapRules)));
            return Task.CompletedTask;
        }

        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> TestConnectionAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        private static RemapRules CloneRemapRules(RemapRules rules)
        {
            return new RemapRules
            {
                OverrideSubject = rules.OverrideSubject,
                OverrideCorrelationId = rules.OverrideCorrelationId,
                PropertyRenames = new Dictionary<string, string>(rules.PropertyRenames, StringComparer.OrdinalIgnoreCase),
                PropertyRemoves = new HashSet<string>(rules.PropertyRemoves, StringComparer.OrdinalIgnoreCase),
            };
        }

        private static SbMessage CloneMessage(SbMessage message)
        {
            return new SbMessage
            {
                MessageId = message.MessageId,
                CorrelationId = message.CorrelationId,
                Subject = message.Subject,
                ContentType = message.ContentType,
                Body = message.Body,
                ApplicationProperties = new Dictionary<string, object>(message.ApplicationProperties, StringComparer.OrdinalIgnoreCase),
                SystemProperties = new SbSystemProperties
                {
                    ExpiresAt = message.SystemProperties.ExpiresAt,
                    LockedUntil = message.SystemProperties.LockedUntil,
                    EnqueuedSequenceNumber = message.SystemProperties.EnqueuedSequenceNumber,
                    PartitionKey = message.SystemProperties.PartitionKey,
                },
                DeadLetterReason = message.DeadLetterReason,
                DeadLetterErrorDescription = message.DeadLetterErrorDescription,
                EnqueuedAt = message.EnqueuedAt,
                DeliveryCount = message.DeliveryCount,
                LockToken = message.LockToken,
                SequenceNumber = message.SequenceNumber,
                SessionId = message.SessionId,
            };
        }

        public sealed record ResubmitCall(string EntityPath, IReadOnlyList<string> SequenceNumbers, string? TargetEntityPath, RemapRules? RemapRules);
    }

}