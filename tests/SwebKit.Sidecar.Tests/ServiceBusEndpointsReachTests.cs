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
/// Handler-level coverage for the reach-message endpoints: the preview's honesty contract
/// (literal consequences, refusal reasons), the refusals (session entities, deferred targets,
/// cap breach, topic parent), and start→poll end-to-end against the demo client. The fake pool
/// returns a real <see cref="DemoServiceBusClient"/> so these run the full park/restore path,
/// not a stubbed one.
/// </summary>
public class ServiceBusEndpointsReachTests
{
    private const string EntityPath = "order-created";   // 5 active (4501..4505), 3 DLQ
    private const string SessionEntityPath = "order-sessions";

    private static (ProfileRepository Profile, DemoModeService Demo, FakeServiceBusClientFactory Pool, Guid NsId) Build()
    {
        var profile = new ProfileRepository();
        var nsId = Guid.NewGuid();
        profile.AddServiceBusNamespace(new ServiceBusNamespace
        {
            Id = nsId,
            Alias = "test-ns",
            FullyQualifiedNamespace = "test-ns.servicebus.windows.net",
            AuthMode = SbAuthMode.ConnectionString,
            CredentialKey = "test-ns-key",
        });
        var pool = new FakeServiceBusClientFactory
        {
            Client = new CountingServiceBusClient(DemoServiceBusClient.OrdersDev()),
        };
        return (profile, new DemoModeService(), pool, nsId);
    }

    private static SbOperationService NewOps() =>
        new(new SbOperationJournalRepository());

    private static ServiceBusEndpoints.ReachMessageRequest Req(
        long target,
        SbReachTargetAction action = SbReachTargetAction.Resubmit,
        int? maxParked = null) =>
        new()
        {
            TargetSequenceNumber = target,
            Action = action,
            RestoreBeforeTarget = true,
            MaxParked = maxParked,
        };

    private static ServiceBusEndpoints.ReachMessagePreview PreviewOf(IResult result) =>
        Assert.IsAssignableFrom<Ok<ServiceBusEndpoints.ReachMessagePreview>>(result).Value!;

    [Fact]
    public async Task Preview_HappyPath_CanStart_WithLiteralHonestConsequences()
    {
        var (profile, demo, pool, nsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReachMessagePreviewAsync(
            nsId.ToString(), EntityPath, Req(4504), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.True(preview.CanStart);
        Assert.Equal(3, preview.PrefixCount);
        Assert.False(preview.TargetBeyondWindow);
        // The honest contract — consequences must say tail copies, not restoration-in-place.
        Assert.Contains(preview.Consequences, c => c.Contains("tail") && c.Contains("NOT restored"));
        Assert.NotEmpty(preview.Warnings);
    }

    [Fact]
    public async Task Preview_SessionEntity_RefusedWithReason()
    {
        var (profile, demo, pool, nsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReachMessagePreviewAsync(
            nsId.ToString(), SessionEntityPath, Req(1), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.False(preview.CanStart);
        Assert.Contains("session", preview.RefusalReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_TopicEntity_RefusedAsNotReceivable()
    {
        var (profile, demo, pool, nsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReachMessagePreviewAsync(
            nsId.ToString(), "user-events", Req(1), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.False(preview.CanStart);
        Assert.Contains("subscription", preview.RefusalReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_DeferredTarget_InsidePeekedRangeButAbsent_Refused()
    {
        var (profile, demo, pool, nsId) = Build();
        using var ops = NewOps();
        // Remove 4503 from the window — a seq inside the peeked range that no longer peeks
        // behaves exactly like a deferred message to this check.
        await pool.Client.CompleteMessagesAsync(EntityPath, [4503]);

        var result = await ServiceBusEndpoints.ReachMessagePreviewAsync(
            nsId.ToString(), EntityPath, Req(4503), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.False(preview.CanStart);
        Assert.Contains("deferred", preview.RefusalReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_TargetBeyondWindow_StartableWithWarning_UnknownPrefix()
    {
        var (profile, demo, pool, nsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReachMessagePreviewAsync(
            nsId.ToString(), EntityPath, Req(99999), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.True(preview.CanStart);
        Assert.True(preview.TargetBeyondWindow);
        Assert.Null(preview.PrefixCount);
        Assert.Contains(preview.Warnings, w => w.Contains("beyond the current peek window"));
    }

    [Fact]
    public async Task Preview_PrefixOverCap_Refused()
    {
        var (profile, demo, pool, nsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReachMessagePreviewAsync(
            nsId.ToString(), EntityPath, Req(4505, maxParked: 2), profile, pool, demo, ops, CancellationToken.None);

        var preview = PreviewOf(result);
        Assert.False(preview.CanStart);
        Assert.Contains("park cap", preview.RefusalReason!);
    }

    [Fact]
    public async Task Preview_BadTarget_Returns400()
    {
        var (profile, demo, pool, nsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReachMessagePreviewAsync(
            nsId.ToString(), EntityPath, Req(0), profile, pool, demo, ops, CancellationToken.None);

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task Start_OnDemoClient_RunsParkRestoreEndToEnd()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, demo, pool, nsId) = Build();
        using var ops = NewOps();

        var startResult = await ServiceBusEndpoints.ReachMessageStartAsync(
            nsId.ToString(), EntityPath, Req(4503, SbReachTargetAction.Complete),
            profile, pool, demo, ops, CancellationToken.None);

        var started = Assert.IsAssignableFrom<Ok<SbOperationStatus>>(startResult).Value!;
        Assert.Equal(SbOperationState.Running, started.State);
        Assert.Equal(EntityPath, started.EntityPath);

        // The demo client runs synchronously — poll until terminal.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        SbOperationStatus? status = null;
        while (DateTime.UtcNow < deadline)
        {
            var get = await ServiceBusEndpoints.GetOperationAsync(
                nsId.ToString(), started.Id.ToString(), profile, demo, ops, CancellationToken.None);
            status = Assert.IsAssignableFrom<Ok<SbOperationStatus>>(get).Value!;
            if (status.State == SbOperationState.Completed) break;
            await Task.Delay(25);
        }

        Assert.True(status!.State == SbOperationState.Completed, $"op ended {status.State}: {status.Error}");
        Assert.Equal(2, status.ParkedCount);
        Assert.Equal(2, status.RestoredCount);

        // Store truth: seqs 4504/4505 remain, plus 2 restored copies at the tail.
        var active = await pool.Client.PeekMessagesAsync(EntityPath, 50);
        Assert.Equal(4, active.Count);
        Assert.Equal([4504L, 4505L], active.Take(2).Select(m => m.SequenceNumber).ToArray());
    }

    [Fact]
    public async Task Start_SessionEntity_Returns409()
    {
        var (profile, demo, pool, nsId) = Build();
        using var ops = NewOps();

        var result = await ServiceBusEndpoints.ReachMessageStartAsync(
            nsId.ToString(), SessionEntityPath, Req(1), profile, pool, demo, ops, CancellationToken.None);

        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task ListOperations_ReportsInterruptedOp_WithDlqStampCount()
    {
        using var sandbox = new AppDataSandbox();
        var journal = new SbOperationJournalRepository();
        var (profile, demo, pool, nsId) = Build();

        // Simulate the crashed run: op stamped 2 messages into the DLQ, then the
        // sidecar died with the journal entry still "running".
        var opId = Guid.NewGuid();
        var client = DemoServiceBusClient.OrdersDev();
        await client.ParkForReachAsync(EntityPath, 4503, opId.ToString("N"), SbReachTargetAction.Complete, 1000);
        await journal.UpsertAsync(new SbOperationJournalEntry
        {
            Id = opId,
            NamespaceId = nsId,
            EntityPath = EntityPath,
            Kind = SbOperationService.ReachMessageKind,
            TargetSequenceNumber = 4503,
            TargetAction = SbReachTargetAction.Complete,
            Status = SbOperationJournalStatus.Running,
        });
        pool.Client = client;

        using var ops = new SbOperationService(journal);
        var result = await ServiceBusEndpoints.ListOperationsAsync(
            nsId.ToString(), EntityPath, profile, pool, demo, ops, CancellationToken.None);

        var list = Assert.IsAssignableFrom<Ok<IReadOnlyList<SbOperationStatus>>>(result).Value!;
        var op = Assert.Single(list);
        Assert.Equal(SbOperationState.Interrupted, op.State);
        // ParkedInDlq comes from the live DLQ stamp scan — the broker's truth, not the journal.
        Assert.Equal(2, op.ParkedInDlq);
    }

    // ── DLQ requeue-by-filter ────────────────────────────────────────────────

    [Fact]
    public async Task RequeueByFilter_HappyPath_ResubmitsMatchingGroup()
    {
        var (profile, demo, pool, nsId) = Build();

        var result = await ServiceBusEndpoints.ResubmitDeadLetterByFilterAsync(
            nsId.ToString(), EntityPath,
            new ServiceBusEndpoints.DlqRequeueByFilterRequest { Reason = "MaxDeliveryCountExceeded" },
            profile, pool, demo, CancellationToken.None);

        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        var resubmitted = (int)value!.GetType().GetProperty("resubmitted")!.GetValue(value)!;
        Assert.Equal(2, resubmitted);
    }

    [Fact]
    public async Task RequeueByFilter_MissingReason_Returns400()
    {
        var (profile, demo, pool, nsId) = Build();

        var result = await ServiceBusEndpoints.ResubmitDeadLetterByFilterAsync(
            nsId.ToString(), EntityPath,
            new ServiceBusEndpoints.DlqRequeueByFilterRequest { Reason = "  " },
            profile, pool, demo, CancellationToken.None);

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task RequeueByFilter_SessionEntity_Returns409()
    {
        var (profile, demo, pool, nsId) = Build();

        var result = await ServiceBusEndpoints.ResubmitDeadLetterByFilterAsync(
            nsId.ToString(), SessionEntityPath,
            new ServiceBusEndpoints.DlqRequeueByFilterRequest { Reason = "Anything" },
            profile, pool, demo, CancellationToken.None);

        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }
}
