using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Agents;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Services;
using Xunit;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// monitoring-closed-loop executor coverage: the confirmed back half of
/// <c>propose_purge_dead_letters</c> / <c>propose_resubmit_dead_letters</c>. The executor only
/// runs after the parked action is confirmed — these tests apply the action directly, the way
/// <see cref="AgentActionApplier"/> does once <see cref="PendingAgentAction.IsConfirmed"/> is set.
/// </summary>
public class ServiceBusActionExecutorTests
{
    private static (ServiceBusActionExecutor Executor, CountingServiceBusClient Client) Build()
    {
        var profiles = new ProfileRepository();
        profiles.AddServiceBusNamespace(new ServiceBusNamespace
        {
            Alias = "test-ns",
            FullyQualifiedNamespace = "test-ns.servicebus.windows.net",
            AuthMode = SbAuthMode.ConnectionString,
            CredentialKey = "test-ns-key",
        });
        var appState = new AppStateService(profiles, new UiStateRepository(),
            new AppEventBus(NullLogger<AppEventBus>.Instance));
        var client = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev());
        var factory = new FakeServiceBusClientFactory { Client = client };
        return (new ServiceBusActionExecutor(factory, appState), client);
    }

    private static PendingAgentAction Action(AgentActionType type, object payload) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = type,
        Summary = "s",
        Target = "t",
        Risk = AgentActionRisk.High,
        Preview = "p",
        ExpectedFingerprint = null,
        Payload = JsonSerializer.SerializeToElement(payload),
    };

    [Fact]
    public void CanHandle_ServiceBusTypes_Only()
    {
        var (executor, _) = Build();
        Assert.True(executor.CanHandle(AgentActionType.PurgeServiceBusDeadLetters));
        Assert.True(executor.CanHandle(AgentActionType.ResubmitServiceBusDeadLetters));
        Assert.False(executor.CanHandle(AgentActionType.RestartAksDeployment));
        Assert.False(executor.CanHandle(AgentActionType.FlushRedisDatabase));
    }

    [Fact]
    public async Task PurgeDeadLetters_PurgesDeadLetterQueue_AndReportsCount()
    {
        var (executor, client) = Build();

        var result = await executor.ApplyAsync(
            Action(AgentActionType.PurgeServiceBusDeadLetters,
                new { entity_path = "order-created", @namespace = "test-ns" }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, client.PurgeMessagesCallCount);
        Assert.Contains("Purged 3", result.ResultSummary); // demo 'order-created' seeds 3 DLQ messages
        Assert.Contains("test-ns", result.ResultSummary);
    }

    [Fact]
    public async Task ResubmitDeadLetters_MovesOnlyTheListedMessages()
    {
        var (executor, client) = Build();

        var result = await executor.ApplyAsync(
            Action(AgentActionType.ResubmitServiceBusDeadLetters,
                new { entity_path = "order-created", @namespace = "test-ns", sequence_numbers = new[] { "4410" } }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, client.ResubmitDeadLetterCallCount);
        Assert.Contains("Resubmitted 1", result.ResultSummary);
    }

    [Fact]
    public async Task ResubmitDeadLetters_EmptySequenceNumbers_Fails_WithoutCallingClient()
    {
        var (executor, client) = Build();

        var result = await executor.ApplyAsync(
            Action(AgentActionType.ResubmitServiceBusDeadLetters,
                new { entity_path = "order-created", sequence_numbers = Array.Empty<string>() }),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, client.ResubmitDeadLetterCallCount);
    }

    [Fact]
    public async Task MissingEntityPath_Fails_WithoutResolvingAClient()
    {
        var (executor, client) = Build();

        var result = await executor.ApplyAsync(
            Action(AgentActionType.PurgeServiceBusDeadLetters, new { @namespace = "test-ns" }),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, client.PurgeMessagesCallCount);
    }

    [Fact]
    public async Task UnknownNamespace_Fails_BeforeAnyMutation()
    {
        var (executor, client) = Build();

        var result = await executor.ApplyAsync(
            Action(AgentActionType.PurgeServiceBusDeadLetters,
                new { entity_path = "order-created", @namespace = "nope" }),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.ErrorMessage);
        Assert.Equal(0, client.PurgeMessagesCallCount);
    }

    [Fact]
    public async Task ClientThrow_SurfacesError_InsteadOfPropagating()
    {
        var (executor, _) = Build();
        // Client failures must come back as a failed AgentActionResult — the applier records
        // the failure on the action rather than crashing the confirm flow.
        executor = new ServiceBusActionExecutor(
            new FakeServiceBusClientFactory
            {
                Client = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev())
                {
                    ThrowOnPurge = new InvalidOperationException("service bus unavailable"),
                },
            },
            NewAppState());

        var result = await executor.ApplyAsync(
            Action(AgentActionType.PurgeServiceBusDeadLetters,
                new { entity_path = "order-created", @namespace = "test-ns" }),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("service bus unavailable", result.ErrorMessage);
    }

    private static AppStateService NewAppState()
    {
        var profiles = new ProfileRepository();
        profiles.AddServiceBusNamespace(new ServiceBusNamespace
        {
            Alias = "test-ns",
            FullyQualifiedNamespace = "test-ns.servicebus.windows.net",
            AuthMode = SbAuthMode.ConnectionString,
            CredentialKey = "test-ns-key",
        });
        return new AppStateService(profiles, new UiStateRepository(),
            new AppEventBus(NullLogger<AppEventBus>.Instance));
    }
}
