using System.Text.Json;
using Moq;
using SwebKit.Agents.Tools;
using SwebKit.Agents.Tools.Aks;
using SwebKit.Agents.Tools.Monitoring;
using SwebKit.Agents.Tools.Redis;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>
/// monitoring-closed-loop 1b/1c coverage: the remediation propose_* tools are
/// <see cref="ToolKind.Mutate"/> but <see cref="IAgentTool.BackgroundProposalEligible"/>, only ever
/// park a <see cref="PendingAgentAction"/> (never mutate), and carry the investigation provenance
/// stamp + extended expiry when a background run's ambient selection is pushed.
/// </summary>
public class AutofixProposalTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private static PendingAgentAction MakeAction(
        string id, AgentActionType type = AgentActionType.RestartAksDeployment,
        string target = "t", string? origin = null, string? sessionId = null,
        object? payload = null) => new()
    {
        Id = id,
        Type = type,
        Summary = "S",
        Target = target,
        Risk = AgentActionRisk.Low,
        Preview = "P",
        ExpectedFingerprint = null,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload),
        Origin = origin,
        OriginSessionId = sessionId,
    };

    private static IDisposable PushInvestigation(string sessionId = "proactive-r1-123") =>
        AgentExecutionContext.Push(new Dictionary<string, string>
        {
            [PendingActionProvenance.OriginKey] = PendingActionProvenance.InvestigationOrigin,
            [PendingActionProvenance.SessionIdKey] = sessionId,
        });

    private static AppStateService AksState() =>
        TestSupport.CreateAppState(c => c.AksConfig = new AksConfig { DefaultNamespace = "configured" });

    private static (AppStateService AppState, ProfileRepository Profiles) RedisState()
    {
        var config = new AppConfig
        {
            Name = "Test",
            RedisConfig = new RedisConfig
            {
                Caches = [new RedisCacheEntry { Id = "c1", DisplayName = "Cache One", ConnectionString = "x" }],
            },
        };
        var repo = new ProfileRepository();
        repo.ReplaceProfileData(new ProfileData { Config = config });
        return (new AppStateService(repo, new UiStateRepository(), new AppEventBus(Microsoft.Extensions.Logging.Abstractions.NullLogger<AppEventBus>.Instance)), repo);
    }

    // ── Metadata: eligible mutators, nothing else ────────────────────────────

    [Fact]
    public void RemediationTools_AreMutate_AndBackgroundProposalEligible()
    {
        var appState = AksState();
        var coordinator = new AgentActionCoordinator();
        var (redisState, profiles) = RedisState();
        IAgentTool[] tools =
        [
            new ProposeRestartAksDeploymentTool(appState, coordinator),
            new ProposeDeleteAksPodTool(appState, coordinator),
            new ProposePurgeDeadLettersTool(appState, coordinator),
            new ProposeResubmitDeadLettersTool(appState, coordinator),
            new ProposeFlushRedisDatabaseTool(redisState, profiles, coordinator),
        ];

        foreach (var tool in tools)
        {
            Assert.Equal(ToolKind.Mutate, tool.Kind);
            Assert.True(tool.BackgroundProposalEligible, $"{tool.Name} must be whitelisted");
        }
    }

    [Fact]
    public void ExistingProposeTools_AreNotBackgroundEligible()
    {
        // The whitelist is opt-in per tool — pre-existing propose_* tools (API client, blob copy,
        // SQL execute, alert-rule creation) stay out of background runs.
        Assert.False(((IAgentTool)new ProposeCreateAlertRuleTool(new AgentActionCoordinator())).BackgroundProposalEligible);
        Assert.False(((IAgentTool)new Tools.Sql.ProposeExecuteSqlTool(new AgentActionCoordinator())).BackgroundProposalEligible);
    }

    // ── Provenance stamping + extended expiry ────────────────────────────────

    [Fact]
    public async Task RestartDeployment_InsideInvestigation_StampsOrigin_AndLongExpiry()
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeRestartAksDeploymentTool(AksState(), coordinator);

        using var _ = PushInvestigation("proactive-rule9-777");
        var result = await tool.ExecuteAsync(
            Args("""{"deployment":"orders","namespace":"prod"}"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("pending_confirmation", doc.RootElement.GetProperty("status").GetString());

        var action = Assert.Single(coordinator.GetPendingActions());
        Assert.Equal(AgentActionType.RestartAksDeployment, action.Type);
        Assert.Equal("investigation", action.Origin);
        Assert.Equal("proactive-rule9-777", action.OriginSessionId);
        Assert.True(action.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23),
            "alert-context proposals must outlive the 5-minute interactive default");
        Assert.Equal("orders", action.Payload!.Value.GetProperty("deployment").GetString());
        Assert.Equal("prod", action.Payload.Value.GetProperty("namespace").GetString());
    }

    [Fact]
    public async Task RestartDeployment_InteractiveTurn_NoOrigin_AndShortExpiry()
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeRestartAksDeploymentTool(AksState(), coordinator);

        await tool.ExecuteAsync(Args("""{"deployment":"orders","namespace":"prod"}"""), CancellationToken.None);

        var action = Assert.Single(coordinator.GetPendingActions());
        Assert.Null(action.Origin);
        Assert.Null(action.OriginSessionId);
        Assert.True(action.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(10));
    }

    [Fact]
    public async Task Proposals_NeverMutate_OnlyParkPendingActions()
    {
        // Every remediation tool's whole observable effect is one parked action — no client
        // calls, no applied state. Confirm-before-execute stays structural.
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeDeleteAksPodTool(AksState(), coordinator);
        var result = await tool.ExecuteAsync(Args("""{"pod":"api-7c9f","namespace":"prod"}"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("pending_confirmation", doc.RootElement.GetProperty("status").GetString());
        var action = Assert.Single(coordinator.GetPendingActions());
        Assert.Equal(AgentActionType.DeleteAksPod, action.Type);
        Assert.False(action.IsApplied);
        Assert.False(action.IsConfirmed);
    }

    // ── Validation errors ────────────────────────────────────────────────────

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"deployment":"  "}""")]
    public async Task RestartDeployment_MissingDeployment_Errors(string args)
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeRestartAksDeploymentTool(AksState(), coordinator);
        var result = await tool.ExecuteAsync(Args(args), CancellationToken.None);
        Assert.Contains("deployment", result);
        Assert.Empty(coordinator.GetPendingActions());
    }

    [Fact]
    public async Task ResubmitDeadLetters_EmptySequenceNumbers_Errors()
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeResubmitDeadLettersTool(AksState(), coordinator);
        var result = await tool.ExecuteAsync(
            Args("""{"entity_path":"orders","sequence_numbers":[]}"""), CancellationToken.None);
        Assert.Contains("sequence_numbers", result);
        Assert.Empty(coordinator.GetPendingActions());
    }

    [Fact]
    public async Task ResubmitDeadLetters_ParksTypedAction_WithNamespaceResolution()
    {
        var appState = TestSupport.CreateAppState(serviceBusNamespaces:
            [new ServiceBusNamespace { Alias = "orders-dev", FullyQualifiedNamespace = "orders-dev.servicebus.windows.net" }]);
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeResubmitDeadLettersTool(appState, coordinator);

        var result = await tool.ExecuteAsync(
            Args("""{"entity_path":"orders","sequence_numbers":["42","43"],"namespace":"orders-dev"}"""),
            CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("pending_confirmation", doc.RootElement.GetProperty("status").GetString());
        var action = Assert.Single(coordinator.GetPendingActions());
        Assert.Equal(AgentActionType.ResubmitServiceBusDeadLetters, action.Type);
        var payload = action.Payload!.Value;
        Assert.Equal("orders", payload.GetProperty("entity_path").GetString());
        Assert.Equal("orders-dev", payload.GetProperty("namespace").GetString());
        Assert.Equal(2, payload.GetProperty("sequence_numbers").GetArrayLength());
    }

    [Fact]
    public async Task PurgeDeadLetters_UnknownNamespace_ErrorsInsteadOfParking()
    {
        var appState = TestSupport.CreateAppState(serviceBusNamespaces:
            [new ServiceBusNamespace { Alias = "orders-dev" }]);
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposePurgeDeadLettersTool(appState, coordinator);

        var result = await tool.ExecuteAsync(
            Args("""{"entity_path":"orders","namespace":"nope"}"""), CancellationToken.None);

        Assert.Contains("not found", result);
        Assert.Empty(coordinator.GetPendingActions());
    }

    [Fact]
    public async Task FlushRedisDatabase_ResolvesCache_IntoPayload()
    {
        var (appState, profiles) = RedisState();
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeFlushRedisDatabaseTool(appState, profiles, coordinator);

        var result = await tool.ExecuteAsync(Args("""{"cache_id":"c1"}"""), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("pending_confirmation", doc.RootElement.GetProperty("status").GetString());
        var action = Assert.Single(coordinator.GetPendingActions());
        Assert.Equal(AgentActionType.FlushRedisDatabase, action.Type);
        Assert.Equal(AgentActionRisk.High, action.Risk);
        Assert.Equal("c1", action.Payload!.Value.GetProperty("cache_id").GetString());
    }

    [Fact]
    public async Task FlushRedisDatabase_UnknownCache_Errors()
    {
        var (appState, profiles) = RedisState();
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeFlushRedisDatabaseTool(appState, profiles, coordinator);

        var result = await tool.ExecuteAsync(Args("""{"cache_id":"missing"}"""), CancellationToken.None);

        Assert.Contains("not found", result);
        Assert.Empty(coordinator.GetPendingActions());
    }

    // ── Per-run cap + dedupe + eviction ──────────────────────────────────────

    [Fact]
    public async Task InvestigationCap_FourthProposal_ReturnsError_AndParksNothing()
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeRestartAksDeploymentTool(AksState(), coordinator);

        using var _ = PushInvestigation("proactive-r1-1");
        for (var i = 0; i < 3; i++)
            await tool.ExecuteAsync(Args($$"""{"deployment":"dep-{{i}}","namespace":"prod"}"""), CancellationToken.None);

        var result = await tool.ExecuteAsync(Args("""{"deployment":"dep-4","namespace":"prod"}"""), CancellationToken.None);

        Assert.Contains("limit", result);
        Assert.Equal(3, coordinator.GetPendingActions().Count);
    }

    [Fact]
    public async Task InvestigationCap_IsPerRun_OtherSessionsUnaffected()
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeRestartAksDeploymentTool(AksState(), coordinator);

        using (PushInvestigation("proactive-r1-1"))
            for (var i = 0; i < 3; i++)
                await tool.ExecuteAsync(Args($$"""{"deployment":"dep-{{i}}"}"""), CancellationToken.None);

        using (PushInvestigation("proactive-r1-2"))
        {
            var result = await tool.ExecuteAsync(Args("""{"deployment":"dep-x"}"""), CancellationToken.None);
            Assert.Contains("pending_confirmation", result);
        }
        Assert.Equal(4, coordinator.GetPendingActions().Count);
    }

    [Fact]
    public void RegisterAction_SameOriginTypeTarget_Dedupes_AndFiresOnce()
    {
        var coordinator = new AgentActionCoordinator();
        var fired = new List<string>();
        coordinator.ActionRegistered += a => fired.Add(a.Id);

        var first = coordinator.RegisterAction(MakeAction("p1", origin: "investigation", sessionId: "s1", target: "prod/Deployment/x"));
        var second = coordinator.RegisterAction(MakeAction("p2", origin: "investigation", sessionId: "s1", target: "prod/Deployment/x"));

        Assert.Equal("p1", first);
        Assert.Equal("p1", second); // second registration returns the existing id
        Assert.Single(fired);
        Assert.Single(coordinator.GetPendingActions());
    }

    [Fact]
    public void RegisterAction_DifferentOriginSessions_DoNotDedupe()
    {
        var coordinator = new AgentActionCoordinator();
        var a = coordinator.RegisterAction(MakeAction("p1", origin: "investigation", sessionId: "s1", target: "t"));
        var b = coordinator.RegisterAction(MakeAction("p2", origin: "investigation", sessionId: "s2", target: "t"));
        Assert.NotEqual(a, b);
        Assert.Equal(2, coordinator.GetPendingActions().Count);
    }

    [Fact]
    public void RegisterAction_InteractiveActions_NeverDedupe()
    {
        var coordinator = new AgentActionCoordinator();
        coordinator.RegisterAction(MakeAction("p1", target: "t"));
        coordinator.RegisterAction(MakeAction("p2", target: "t"));
        Assert.Equal(2, coordinator.GetPendingActions().Count);
    }

    [Fact]
    public void RegisterAction_FullStore_EvictsInteractiveBeforeInvestigationProposals()
    {
        var coordinator = new AgentActionCoordinator();
        // Park the investigation proposal first — it's the oldest entry and would be the one
        // evicted under pure oldest-first ordering.
        coordinator.RegisterAction(MakeAction("inv-1", origin: "investigation", sessionId: "s1"));
        for (var i = 0; i < coordinator.MaxPendingActions - 1; i++)
            coordinator.RegisterAction(MakeAction($"chat-{i}"));

        coordinator.RegisterAction(MakeAction("chat-new"));

        Assert.NotNull(coordinator.GetAction("inv-1"));   // survived — alert proposal protected
        Assert.Null(coordinator.GetAction("chat-0"));     // oldest interactive evicted instead
        Assert.NotNull(coordinator.GetAction("chat-new"));
    }

    [Fact]
    public void CountPendingByOrigin_CountsOnlyLiveMatching()
    {
        var coordinator = new AgentActionCoordinator();
        coordinator.RegisterAction(MakeAction("a", origin: "investigation", sessionId: "s1"));
        coordinator.RegisterAction(MakeAction("b", origin: "investigation", sessionId: "s2"));
        coordinator.RegisterAction(MakeAction("c"));
        coordinator.RegisterAction(MakeAction("d", origin: "investigation", sessionId: "s1",
            type: AgentActionType.DeleteAksPod));
        coordinator.RejectAction("d");

        Assert.Equal(1, coordinator.CountPendingByOrigin("s1"));
        Assert.Equal(1, coordinator.CountPendingByOrigin("s2"));
        Assert.Equal(0, coordinator.CountPendingByOrigin("nope"));
    }

    // ── Executors: confirmed actions actually mutate ─────────────────────────

    [Fact]
    public void AksExecutor_CanHandle_NewTypes()
    {
        var executor = new AksActionExecutor(
            Mock.Of<IAksClientFactory>(), new DemoAksClient(), AksState());
        Assert.True(executor.CanHandle(AgentActionType.ApplyAksYaml));
        Assert.True(executor.CanHandle(AgentActionType.RestartAksDeployment));
        Assert.True(executor.CanHandle(AgentActionType.DeleteAksPod));
        Assert.False(executor.CanHandle(AgentActionType.FlushRedisDatabase));
    }

    [Fact]
    public async Task AksExecutor_RestartDeployment_CallsClient()
    {
        var client = new Mock<IAksClient>();
        var factory = new Mock<IAksClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string?>(), It.IsAny<string?>())).Returns(client.Object);
        var executor = new AksActionExecutor(factory.Object, new DemoAksClient(), AksState());

        var result = await executor.ApplyAsync(MakeAction("a1",
            payload: new { deployment = "orders", @namespace = "prod", context = "ctx-1" }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        factory.Verify(f => f.Create("ctx-1", It.IsAny<string?>()), Times.Once);
        client.Verify(c => c.RestartDeploymentAsync("prod", "orders", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AksExecutor_DeletePod_CallsClient()
    {
        var client = new Mock<IAksClient>();
        var factory = new Mock<IAksClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string?>(), It.IsAny<string?>())).Returns(client.Object);
        var executor = new AksActionExecutor(factory.Object, new DemoAksClient(), AksState());

        var result = await executor.ApplyAsync(MakeAction("a1", AgentActionType.DeleteAksPod,
            payload: new { pod = "api-7c9f", @namespace = "prod", context = (string?)null }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        client.Verify(c => c.DeletePodAsync("prod", "api-7c9f", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AksExecutor_IncompletePayload_Fails_WithoutCallingClient()
    {
        var client = new Mock<IAksClient>();
        var factory = new Mock<IAksClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string?>(), It.IsAny<string?>())).Returns(client.Object);
        var executor = new AksActionExecutor(factory.Object, new DemoAksClient(), AksState());

        var result = await executor.ApplyAsync(MakeAction("a1",
            payload: new { @namespace = "prod" }),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        client.Verify(c => c.RestartDeploymentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RedisExecutor_FlushDatabase_CallsClient()
    {
        var client = new Mock<IRedisClient>();
        var factory = new Mock<IRedisClientFactory>();
        var (appState, profiles) = RedisState();
        var cache = profiles.GetProfileData().Config.RedisConfig!.Caches![0];
        factory.Setup(f => f.CreateAsync(cache, It.IsAny<CancellationToken>())).ReturnsAsync(client.Object);
        var executor = new RedisActionExecutor(appState, profiles, factory.Object);

        var result = await executor.ApplyAsync(MakeAction("a1", AgentActionType.FlushRedisDatabase,
            payload: new { cache_id = "c1" }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        client.Verify(c => c.FlushDatabaseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
