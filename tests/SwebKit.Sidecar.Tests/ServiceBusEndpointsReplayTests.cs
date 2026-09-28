using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Handler-level coverage for the cross-environment replay endpoints and the read-only entity
/// properties surface. The fake pool resolves a DIFFERENT client per namespace — an honest
/// two-store test: source runs against an orders-dev-shaped demo client, the target against a
/// payments-dev-shaped one, and "cross-environment" really means cross-client.
/// </summary>
public class ServiceBusEndpointsReplayTests
{
    private const string SourcePath = "order-created";      // orders-dev: 5 active (4501..4505), 3 DLQ (4350,4388,4410)
    private const string SessionPath = "order-sessions";    // orders-dev session-required queue (4601..4605)
    private const string TargetPath = "order-failed";       // payments-dev: empty — clean landing zone

    private static (
        ProfileRepository Profile,
        DemoModeService Demo,
        FakeServiceBusClientFactory Pool,
        CountingServiceBusClient Source,
        CountingServiceBusClient Target,
        Guid SourceNsId,
        Guid TargetNsId) Build()
    {
        var profile = new ProfileRepository();
        var sourceNsId = Guid.NewGuid();
        var targetNsId = Guid.NewGuid();
        profile.AddServiceBusNamespace(new ServiceBusNamespace
        {
            Id = sourceNsId,
            Alias = "orders-dev",
            FullyQualifiedNamespace = "orders-dev.servicebus.windows.net",
            AuthMode = SbAuthMode.ConnectionString,
            CredentialKey = "orders-key",
        });
        profile.AddServiceBusNamespace(new ServiceBusNamespace
        {
            Id = targetNsId,
            Alias = "payments-dev",
            FullyQualifiedNamespace = "payments-dev.servicebus.windows.net",
            AuthMode = SbAuthMode.ConnectionString,
            CredentialKey = "payments-key",
        });

        var source = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev());
        var target = new CountingServiceBusClient(DemoServiceBusClient.PaymentsDev());
        var pool = new FakeServiceBusClientFactory { Client = source };
        pool.ClientsByNamespace[targetNsId] = target;
        return (profile, new DemoModeService(), pool, source, target, sourceNsId, targetNsId);
    }

    private static SbOperationService NewOps() => new(new SbOperationJournalRepository());

    private static ServiceBusEndpoints.ReplayToRequest Req(
        Guid targetNsId,
        string targetPath = TargetPath,
        bool deadLetter = false,
        bool scrub = false,
        bool stripSession = false,
        bool removeSource = false,
        params long[] seqs) =>
        new()
        {
            SequenceNumbers = seqs.Length > 0 ? seqs : [4501, 4502],
            DeadLetter = deadLetter,
            TargetNsId = targetNsId.ToString(),
            TargetEntityPath = targetPath,
            ScrubProperties = scrub,
            StripSessionId = stripSession,
            RemoveSource = removeSource,
        };

    private static ServiceBusEndpoints.ReplayToPreview PreviewOf(IResult result) =>
        Assert.IsAssignableFrom<Ok<ServiceBusEndpoints.ReplayToPreview>>(result).Value!;

    private static async Task<SbOperationStatus> PollToTerminal(
        ProfileRepository profile, DemoModeService demo, SbOperationService ops,
        Guid nsId, Guid opId, params SbOperationState[] terminal)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        SbOperationStatus? status = null;
        while (DateTime.UtcNow < deadline)
        {
            var get = await ServiceBusEndpoints.GetOperationAsync(
                nsId.ToString(), opId.ToString(), profile, demo, ops, CancellationToken.None);
            status = Assert.IsAssignableFrom<Ok<SbOperationStatus>>(get).Value!;
            if (terminal.Contains(status.State)) return status;
            await Task.Delay(25);
        }
        Assert.Fail($"op never reached a terminal state — last: {status?.State} ({status?.Error})");
        return null!;
    }

    // ── Preview ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_HappyPath_CanStart_WithNewCopySemantics()
    {
        var (profile, demo, pool, _, _, nsId, targetNsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReplayToPreviewAsync(
            nsId.ToString(), SourcePath, Req(targetNsId), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.True(preview.CanStart, preview.RefusalReason);
        Assert.Equal(2, preview.MatchedCount);
        Assert.Equal(0, preview.MissingCount);
        // The honest contract — consequences must say NEW message, tail, provenance.
        Assert.Contains(preview.Consequences, c => c.Contains("NEW message") && c.Contains("tail"));
        Assert.Contains(preview.Consequences, c => c.Contains(SbReplayStamp.ReplayedFrom) && c.Contains("orders-dev.servicebus.windows.net/order-created"));
        Assert.Contains(preview.Consequences, c => c.Contains("Cannot preserve"));
        // Cross-namespace non-atomicity is a warning, never hidden.
        Assert.Contains(preview.Warnings, w => w.Contains("No transaction can span two namespaces"));
    }

    [Fact]
    public async Task Preview_SessionSource_Refused()
    {
        var (profile, demo, pool, _, _, nsId, targetNsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReplayToPreviewAsync(
            nsId.ToString(), SessionPath, Req(targetNsId, seqs: [4601]), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.False(preview.CanStart);
        Assert.Contains("session", preview.RefusalReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_SessionTarget_WithStripSessionId_Refused()
    {
        var (profile, demo, pool, _, _, nsId, _) = Build();
        using var ops = NewOps();

        // Target is the session-required queue on the SAME namespace — cross-namespace is
        // supported but not required; the session incompatibility is what's being refused here.
        var result = await ServiceBusEndpoints.ReplayToPreviewAsync(
            nsId.ToString(), SourcePath, Req(nsId, SessionPath, stripSession: true), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.False(preview.CanStart);
        Assert.Contains("requires sessions", preview.RefusalReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_SessionTarget_SessionlessSources_WarnsNotRefuses()
    {
        var (profile, demo, pool, _, _, nsId, _) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReplayToPreviewAsync(
            nsId.ToString(), SourcePath, Req(nsId, SessionPath), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.True(preview.CanStart, preview.RefusalReason);
        Assert.True(preview.TargetRequiresSession);
        Assert.Contains(preview.Warnings, w => w.Contains("no session id"));
    }

    [Fact]
    public async Task Preview_TargetEqualsSource_Refused()
    {
        var (profile, demo, pool, _, _, nsId, _) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReplayToPreviewAsync(
            nsId.ToString(), SourcePath, Req(nsId, SourcePath), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.False(preview.CanStart);
        Assert.Contains("equals source", preview.RefusalReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_UnknownTargetNamespace_Refused()
    {
        var (profile, demo, pool, _, _, nsId, _) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReplayToPreviewAsync(
            nsId.ToString(), SourcePath, Req(Guid.NewGuid()), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.False(preview.CanStart);
        Assert.Contains("doesn't exist", preview.RefusalReason!);
    }

    [Fact]
    public async Task Preview_SubscriptionTarget_Refused()
    {
        var (profile, demo, pool, _, _, nsId, targetNsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReplayToPreviewAsync(
            nsId.ToString(), SourcePath, Req(targetNsId, "user-events/subscriptions/consumer-a"),
            profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.False(preview.CanStart);
        Assert.Contains("receive-only", preview.RefusalReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_EmptySelection_Returns400()
    {
        var (profile, demo, pool, _, _, nsId, targetNsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReplayToPreviewAsync(
            nsId.ToString(), SourcePath, new ServiceBusEndpoints.ReplayToRequest { TargetNsId = targetNsId.ToString(), TargetEntityPath = TargetPath },
            profile, pool, demo, ops, CancellationToken.None);

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    // ── Start → run → verify store truth ────────────────────────────────────

    [Fact]
    public async Task Start_CopySemantics_TargetGainsStampedCopies_SourceIntact()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, demo, pool, source, target, nsId, targetNsId) = Build();
        using var ops = NewOps();

        var startResult = await ServiceBusEndpoints.ReplayToStartAsync(
            nsId.ToString(), SourcePath, Req(targetNsId), profile, pool, demo, ops, CancellationToken.None);

        var started = Assert.IsAssignableFrom<Ok<SbOperationStatus>>(startResult).Value!;
        Assert.Equal(SbOperationService.ReplayToKind, started.Kind);
        Assert.Equal(targetNsId, started.TargetNamespaceId);
        Assert.Equal(TargetPath, started.TargetEntityPath);

        var status = await PollToTerminal(profile, demo, ops, nsId, started.Id, SbOperationState.Completed, SbOperationState.Failed);
        Assert.Equal(SbOperationState.Completed, status.State);
        Assert.Equal(2, status.ReplayedCount);
        Assert.Equal(0, status.FailedCount);
        Assert.Equal(0, status.MissingCount);

        // Target truth: two NEW messages — fresh sequence numbers beyond the payments seed range,
        // provenance stamp pointing back at the source namespace/entity.
        var targetActive = await target.PeekMessagesAsync(TargetPath, 50);
        Assert.Equal(2, targetActive.Count);
        Assert.All(targetActive, m =>
        {
            Assert.True(m.SequenceNumber > 1002, $"expected a fresh sequence, got {m.SequenceNumber}");
            Assert.Equal("orders-dev.servicebus.windows.net/order-created",
                m.ApplicationProperties[SbReplayStamp.ReplayedFrom]);
            Assert.Equal(started.Id.ToString("N"), m.ApplicationProperties[SbReplayStamp.OperationId]);
            Assert.Equal(0, m.DeliveryCount);   // reset — new message
        });
        // Source untouched — copy semantics.
        Assert.Equal(5, (await source.PeekMessagesAsync(SourcePath, 50)).Count);
    }

    [Fact]
    public async Task Start_RemoveSource_MoveSemantics_SourceLosesCopies()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, demo, pool, source, target, nsId, targetNsId) = Build();
        using var ops = NewOps();

        var startResult = await ServiceBusEndpoints.ReplayToStartAsync(
            nsId.ToString(), SourcePath, Req(targetNsId, removeSource: true), profile, pool, demo, ops, CancellationToken.None);

        var started = Assert.IsAssignableFrom<Ok<SbOperationStatus>>(startResult).Value!;
        var status = await PollToTerminal(profile, demo, ops, nsId, started.Id, SbOperationState.Completed, SbOperationState.Failed);
        Assert.Equal(SbOperationState.Completed, status.State);

        Assert.Equal(2, (await target.PeekMessagesAsync(TargetPath, 50)).Count);
        var sourceActive = await source.PeekMessagesAsync(SourcePath, 50);
        Assert.Equal(3, sourceActive.Count);
        Assert.DoesNotContain(sourceActive, m => m.SequenceNumber is 4501 or 4502);
    }

    [Fact]
    public async Task Start_DlqSource_CopiesLeaveDlqAlone_UnlessRemoved()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, demo, pool, source, target, nsId, targetNsId) = Build();
        using var ops = NewOps();

        var startResult = await ServiceBusEndpoints.ReplayToStartAsync(
            nsId.ToString(), SourcePath, Req(targetNsId, deadLetter: true, seqs: [4410]), profile, pool, demo, ops, CancellationToken.None);

        var started = Assert.IsAssignableFrom<Ok<SbOperationStatus>>(startResult).Value!;
        var status = await PollToTerminal(profile, demo, ops, nsId, started.Id, SbOperationState.Completed, SbOperationState.Failed);
        Assert.Equal(SbOperationState.Completed, status.State);
        Assert.Equal(1, status.ReplayedCount);

        var copy = Assert.Single(await target.PeekMessagesAsync(TargetPath, 50));
        Assert.Equal("orders-dev.servicebus.windows.net/order-created/$DeadLetterQueue",
            copy.ApplicationProperties[SbReplayStamp.ReplayedFrom]);
        // DLQ metadata does not ride the copy.
        Assert.False(copy.ApplicationProperties.ContainsKey("DeadLetterReason"));
        // Copy semantics — the DLQ original stays.
        Assert.Equal(3, (await source.PeekDeadLetterAsync(SourcePath, 50)).Count);
    }

    [Fact]
    public async Task Start_SequenceGone_Completes_WithMissingReport()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, demo, pool, _, target, nsId, targetNsId) = Build();
        using var ops = NewOps();

        var startResult = await ServiceBusEndpoints.ReplayToStartAsync(
            nsId.ToString(), SourcePath, Req(targetNsId, seqs: [4501, 99999]), profile, pool, demo, ops, CancellationToken.None);

        var started = Assert.IsAssignableFrom<Ok<SbOperationStatus>>(startResult).Value!;
        var status = await PollToTerminal(profile, demo, ops, nsId, started.Id, SbOperationState.Completed, SbOperationState.Failed);
        Assert.Equal(SbOperationState.Completed, status.State);
        Assert.Equal(1, status.ReplayedCount);
        Assert.Equal(1, status.MissingCount);
        Assert.Contains("never found", status.Error!);
        Assert.Single(await target.PeekMessagesAsync(TargetPath, 50));
    }

    [Fact]
    public async Task Start_SessionSource_Returns409()
    {
        var (profile, demo, pool, _, _, nsId, targetNsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReplayToStartAsync(
            nsId.ToString(), SessionPath, Req(targetNsId, seqs: [4601]), profile, pool, demo, ops, CancellationToken.None);

        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    // ── Cancel ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_MidTransfer_StopsAndLeavesSourceUntouched()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, demo, pool, source, target, nsId, targetNsId) = Build();
        using var ops = NewOps();

        // Cancel as soon as the first confirmed send reports — the runner thread blocks in the
        // hook until the op id arrives, so the cancel lands between messages deterministically.
        var opIdReady = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.OnReplayProgress = p =>
        {
            if (p.Succeeded)
            {
                ops.CancelAsync(opIdReady.Task.GetAwaiter().GetResult()).GetAwaiter().GetResult();
            }
        };

        var startResult = await ServiceBusEndpoints.ReplayToStartAsync(
            nsId.ToString(), SourcePath, Req(targetNsId, seqs: [4501, 4502, 4503]), profile, pool, demo, ops, CancellationToken.None);
        var started = Assert.IsAssignableFrom<Ok<SbOperationStatus>>(startResult).Value!;
        opIdReady.SetResult(started.Id);

        var status = await PollToTerminal(profile, demo, ops, nsId, started.Id,
            SbOperationState.Cancelled, SbOperationState.Completed, SbOperationState.Failed);
        Assert.Equal(SbOperationState.Cancelled, status.State);
        Assert.Equal(1, status.ReplayedCount);          // only the first send completed before the cancel
        Assert.Single(await target.PeekMessagesAsync(TargetPath, 50));
        Assert.Equal(5, (await source.PeekMessagesAsync(SourcePath, 50)).Count);  // copy semantics — intact
        Assert.Contains("Cancelled", status.Error);
        Assert.Contains("Resume continues", status.Error);
    }

    // ── Crash → resume through the journal ──────────────────────────────────

    [Fact]
    public async Task Resume_InterruptedReplay_SkipsAlreadyProcessed()
    {
        using var sandbox = new AppDataSandbox();
        var journal = new SbOperationJournalRepository();
        var (profile, demo, pool, source, target, nsId, targetNsId) = Build();

        // Simulate the crashed run: 4501 confirmed sent before the sidecar died — the journal's
        // processed-set is the resume skip-set.
        var opId = Guid.NewGuid();
        await source.ReplayMessagesAsync(
            SourcePath, [4501], false, target, TargetPath,
            new SbReplayOptions { ReplayedFrom = "orders-dev.servicebus.windows.net/order-created", OperationId = opId.ToString("N") });
        await journal.UpsertAsync(new SbOperationJournalEntry
        {
            Id = opId,
            NamespaceId = nsId,
            EntityPath = SourcePath,
            Kind = SbOperationService.ReplayToKind,
            Status = SbOperationJournalStatus.Running,
            TargetNamespaceId = targetNsId,
            TargetEntityPath = TargetPath,
            ReplayedFrom = "orders-dev.servicebus.windows.net/order-created",
            RequestedSequences = [4501, 4502],
            ProcessedSequences = [4501],
            ReplayedCount = 1,
        });

        using var ops = new SbOperationService(journal);
        var result = await ServiceBusEndpoints.ResumeOperationAsync(
            nsId.ToString(), opId.ToString(), profile, pool, demo, ops, CancellationToken.None);

        var resumed = Assert.IsAssignableFrom<Ok<SbOperationStatus>>(result).Value!;
        var status = await PollToTerminal(profile, demo, ops, nsId, opId, SbOperationState.Completed, SbOperationState.Failed);
        Assert.Equal(SbOperationState.Completed, status.State);
        Assert.Equal(2, status.ReplayedCount);

        // The honest bit: 4501's pre-crash copy is NOT re-sent — the target holds exactly two.
        Assert.Equal(2, (await target.PeekMessagesAsync(TargetPath, 50)).Count);
    }

    [Fact]
    public async Task Resume_ReplayOp_UnresolvableTarget_FailsHonestly()
    {
        using var sandbox = new AppDataSandbox();
        var journal = new SbOperationJournalRepository();
        var (profile, demo, pool, _, _, nsId, _) = Build();

        var opId = Guid.NewGuid();
        await journal.UpsertAsync(new SbOperationJournalEntry
        {
            Id = opId,
            NamespaceId = nsId,
            EntityPath = SourcePath,
            Kind = SbOperationService.ReplayToKind,
            Status = SbOperationJournalStatus.Running,
            TargetNamespaceId = Guid.NewGuid(),   // not in the profile
            TargetEntityPath = TargetPath,
            ReplayedFrom = "orders-dev.servicebus.windows.net/order-created",
            RequestedSequences = [4501],
        });

        using var ops = new SbOperationService(journal);
        var result = await ServiceBusEndpoints.ResumeOperationAsync(
            nsId.ToString(), opId.ToString(), profile, pool, demo, ops, CancellationToken.None);

        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task ListOperations_ReplayKind_DoesNotScanDlq()
    {
        using var sandbox = new AppDataSandbox();
        var journal = new SbOperationJournalRepository();
        var (profile, demo, pool, source, _, nsId, targetNsId) = Build();

        await journal.UpsertAsync(new SbOperationJournalEntry
        {
            Id = Guid.NewGuid(),
            NamespaceId = nsId,
            EntityPath = SourcePath,
            Kind = SbOperationService.ReplayToKind,
            Status = SbOperationJournalStatus.Running,
            TargetNamespaceId = targetNsId,
            TargetEntityPath = TargetPath,
            ReplayedFrom = "orders-dev.servicebus.windows.net/order-created",
            RequestedSequences = [4501],
        });

        using var ops = new SbOperationService(journal);
        var result = await ServiceBusEndpoints.ListOperationsAsync(
            nsId.ToString(), SourcePath, profile, pool, demo, ops, CancellationToken.None);

        var list = Assert.IsAssignableFrom<Ok<IReadOnlyList<SbOperationStatus>>>(result).Value!;
        var op = Assert.Single(list);
        Assert.Equal(SbOperationState.Interrupted, op.State);
        Assert.Equal(SbOperationService.ReplayToKind, op.Kind);
        // Kind-gated: the parked-DLQ stamp scan is reach-only — replay ops never trigger it.
        Assert.Equal(0, source.ScanParkedCallCount);
        Assert.Null(op.ParkedInDlq);
    }

    // ── Entity properties ───────────────────────────────────────────────────

    [Fact]
    public async Task EntityProperties_Queue_ReturnsReadOnlyRows()
    {
        var (profile, demo, pool, _, _, nsId, _) = Build();

        var result = await ServiceBusEndpoints.GetEntityPropertiesAsync(
            nsId.ToString(), SourcePath, profile, pool, demo, CancellationToken.None);

        var props = Assert.IsAssignableFrom<Ok<SbEntityProperties>>(result).Value!;
        Assert.Equal("queue", props.EntityKind);
        Assert.Equal(SourcePath, props.EntityPath);
        Assert.False(props.RequiresSession);
        Assert.Contains(props.Properties, p => p.Name == "Max size");
        Assert.Contains(props.Properties, p => p.Name == "Max delivery count");
        Assert.Contains(props.Properties, p => p.Name == "Lock duration");
        Assert.Contains(props.Properties, p => p.Name == "Default TTL");
    }

    [Fact]
    public async Task EntityProperties_SessionQueue_ExposesSessionFlag()
    {
        var (profile, demo, pool, _, _, nsId, _) = Build();

        var result = await ServiceBusEndpoints.GetEntityPropertiesAsync(
            nsId.ToString(), SessionPath, profile, pool, demo, CancellationToken.None);

        var props = Assert.IsAssignableFrom<Ok<SbEntityProperties>>(result).Value!;
        Assert.True(props.RequiresSession);
    }

    [Fact]
    public async Task EntityProperties_Subscription_ResolvesTopicAndName()
    {
        var (profile, demo, pool, _, _, nsId, _) = Build();

        var result = await ServiceBusEndpoints.GetEntityPropertiesAsync(
            nsId.ToString(), "user-events/subscriptions/consumer-a", profile, pool, demo, CancellationToken.None);

        var props = Assert.IsAssignableFrom<Ok<SbEntityProperties>>(result).Value!;
        Assert.Equal("subscription", props.EntityKind);
        Assert.Equal("user-events", props.TopicName);
    }

    [Fact]
    public async Task EntityProperties_Topic_ReturnsTopicRows()
    {
        var (profile, demo, pool, _, _, nsId, _) = Build();

        var result = await ServiceBusEndpoints.GetEntityPropertiesAsync(
            nsId.ToString(), "user-events", profile, pool, demo, CancellationToken.None);

        var props = Assert.IsAssignableFrom<Ok<SbEntityProperties>>(result).Value!;
        Assert.Equal("topic", props.EntityKind);
        Assert.Contains(props.Properties, p => p.Name == "Max size");
    }

    [Fact]
    public async Task EntityProperties_UnknownEntity_Returns404()
    {
        var (profile, demo, pool, _, _, nsId, _) = Build();

        var result = await ServiceBusEndpoints.GetEntityPropertiesAsync(
            nsId.ToString(), "no-such-entity", profile, pool, demo, CancellationToken.None);

        Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }
}
