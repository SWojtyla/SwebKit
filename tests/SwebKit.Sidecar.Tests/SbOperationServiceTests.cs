using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// State-machine coverage for <see cref="SbOperationService"/>: happy-path park→restore, cancel
/// mid-park, conflict refusal, and journal-driven crash recovery (a "running" journal entry
/// materializes as an interrupted, resumable op).
/// </summary>
public sealed class SbOperationServiceTests
{
    /// <summary>Delegate-driven client stub — the service's contract with the client is only the four power-op methods.</summary>
    private sealed class StubSbClient : IServiceBusClient
    {
        public Func<CancellationToken, Task<SbParkResult>>? OnPark { get; set; }
        public Func<CancellationToken, Task<SbRestoreResult>>? OnRestore { get; set; }
        public int ParkCalls { get; private set; }
        public int RestoreCalls { get; private set; }

        public Task<SbParkResult> ParkForReachAsync(string entityPath, long targetSequenceNumber, string operationId, SbReachTargetAction targetAction, int maxParked, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            ParkCalls++;
            return OnPark?.Invoke(ct) ?? Task.FromResult(new SbParkResult { TargetReached = true, ParkedCount = 0 });
        }

        public Task<SbRestoreResult> RestoreParkedCopiesAsync(string entityPath, string operationId, bool targetAfterPrefix, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            RestoreCalls++;
            return OnRestore?.Invoke(ct) ?? Task.FromResult(new SbRestoreResult());
        }

        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) => Task.FromResult(new SbNamespaceInfo { Name = "t", Endpoint = "t" });
        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SbEntityInfo>>([]);
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

    private static async Task<SbOperationStatus> WaitForStateAsync(
        SbOperationService ops, Guid id, SbOperationState wanted, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var status = await ops.GetAsync(id);
            Assert.NotNull(status);
            if (status!.State == wanted) return status;
            if (status.State is SbOperationState.Failed && wanted != SbOperationState.Failed)
            {
                Assert.Fail($"Operation failed instead of reaching {wanted}: {status.Error}");
            }
            await Task.Delay(25);
        }
        var last = await ops.GetAsync(id);
        Assert.Fail($"Timed out waiting for {wanted}; last state: {last?.State} ({last?.Error})");
        throw new InvalidOperationException();
    }

    [Fact]
    public async Task Start_ParkThenRestore_Completes_WithCounts()
    {
        using var sandbox = new AppDataSandbox();
        using var ops = new SbOperationService(new SbOperationJournalRepository());
        var nsId = Guid.NewGuid();
        var client = new StubSbClient
        {
            OnPark = _ => Task.FromResult(new SbParkResult { TargetReached = true, ParkedCount = 3 }),
            OnRestore = _ => Task.FromResult(new SbRestoreResult { RestoredCount = 3 }),
        };

        var started = await ops.TryStartReachAsync(nsId, "q", 4504, SbReachTargetAction.Complete, true, 1000, client);

        Assert.NotNull(started);
        var done = await WaitForStateAsync(ops, started!.Id, SbOperationState.Completed);
        Assert.Equal(3, done.ParkedCount);
        Assert.Equal(3, done.RestoredCount);
        Assert.Equal(1, client.ParkCalls);
        Assert.Equal(1, client.RestoreCalls);
    }

    [Fact]
    public async Task Start_WhenOpAlreadyRunningOnEntity_ReturnsNull()
    {
        using var sandbox = new AppDataSandbox();
        using var ops = new SbOperationService(new SbOperationJournalRepository());
        var nsId = Guid.NewGuid();
        var parkGate = new TaskCompletionSource<SbParkResult>();
        var client = new StubSbClient { OnPark = _ => parkGate.Task };

        var first = await ops.TryStartReachAsync(nsId, "q", 10, SbReachTargetAction.Complete, true, 1000, client);
        var second = await ops.TryStartReachAsync(nsId, "q", 10, SbReachTargetAction.Complete, true, 1000, client);

        Assert.NotNull(first);
        Assert.Null(second);

        parkGate.SetResult(new SbParkResult { TargetReached = true });
        await WaitForStateAsync(ops, first!.Id, SbOperationState.Completed);
    }

    [Fact]
    public async Task Cancel_MidPark_LeavesOpCancelled_AndResumeRestores()
    {
        using var sandbox = new AppDataSandbox();
        using var ops = new SbOperationService(new SbOperationJournalRepository());
        var nsId = Guid.NewGuid();
        var client = new StubSbClient
        {
            // Park hangs until the cancellation token fires — the real client's ct.ThrowIfCancellationRequested loop.
            OnPark = async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new SbParkResult();
            },
            OnRestore = _ => Task.FromResult(new SbRestoreResult { RestoredCount = 2 }),
        };

        var started = await ops.TryStartReachAsync(nsId, "q", 10, SbReachTargetAction.Resubmit, true, 1000, client);
        Assert.NotNull(started);

        var cancelled = await ops.CancelAsync(started!.Id);
        Assert.NotNull(cancelled);

        var done = await WaitForStateAsync(ops, started.Id, SbOperationState.Cancelled);
        Assert.Contains("parked", done.Error, StringComparison.OrdinalIgnoreCase);

        var resumed = await ops.ResumeAsync(started.Id, client);
        Assert.NotNull(resumed);

        var completed = await WaitForStateAsync(ops, started.Id, SbOperationState.Completed);
        Assert.Equal(2, completed.RestoredCount);
    }

    [Fact]
    public async Task Park_TargetUnreached_Fails_WithResumableParkedSet()
    {
        using var sandbox = new AppDataSandbox();
        using var ops = new SbOperationService(new SbOperationJournalRepository());
        var nsId = Guid.NewGuid();
        var client = new StubSbClient
        {
            OnPark = _ => Task.FromResult(new SbParkResult
            {
                TargetReached = false,
                ParkedCount = 4,
                FirstSequenceBeyondTarget = 99,
            }),
        };

        var started = await ops.TryStartReachAsync(nsId, "q", 50, SbReachTargetAction.Complete, true, 1000, client);

        var done = await WaitForStateAsync(ops, started!.Id, SbOperationState.Failed);
        Assert.Contains("deferred", done.Error);
        Assert.Contains("99", done.Error);
        Assert.Equal(0, client.RestoreCalls); // restore must NOT run when the target was never reached
    }

    [Fact]
    public async Task JournalRunningEntry_MaterializesAsInterrupted_ResumableAfterRestart()
    {
        using var sandbox = new AppDataSandbox();
        var journal = new SbOperationJournalRepository();
        var nsId = Guid.NewGuid();
        var opId = Guid.NewGuid();

        // Simulate the crashed run: a journal entry left "running" — exactly what the service
        // wrote before its first park batch.
        await journal.UpsertAsync(new SbOperationJournalEntry
        {
            Id = opId,
            NamespaceId = nsId,
            EntityPath = "q",
            Kind = SbOperationService.ReachMessageKind,
            TargetSequenceNumber = 42,
            TargetAction = SbReachTargetAction.Complete,
            Status = SbOperationJournalStatus.Running,
            ParkedCount = 0,
        });

        // A fresh service instance on the same journal file = the restarted sidecar.
        using var ops = new SbOperationService(journal);
        var opsList = await ops.ListAsync(nsId, "q");

        var interrupted = Assert.Single(opsList);
        Assert.Equal(SbOperationState.Interrupted, interrupted.State);
        Assert.Equal(opId, interrupted.Id);
        Assert.Contains("parked", interrupted.Error, StringComparison.OrdinalIgnoreCase);

        var client = new StubSbClient
        {
            OnRestore = _ => Task.FromResult(new SbRestoreResult { RestoredCount = 7 }),
        };
        var resumed = await ops.ResumeAsync(opId, client);
        Assert.NotNull(resumed);

        var completed = await WaitForStateAsync(ops, opId, SbOperationState.Completed);
        Assert.Equal(7, completed.RestoredCount);
    }

    [Fact]
    public async Task Dismiss_TerminalizesInterruptedOp()
    {
        using var sandbox = new AppDataSandbox();
        var journal = new SbOperationJournalRepository();
        var nsId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        await journal.UpsertAsync(new SbOperationJournalEntry
        {
            Id = opId,
            NamespaceId = nsId,
            EntityPath = "q",
            Kind = SbOperationService.ReachMessageKind,
            Status = SbOperationJournalStatus.Running,
        });

        using var ops = new SbOperationService(journal);
        var dismissed = await ops.DismissAsync(opId);

        Assert.NotNull(dismissed);
        Assert.Equal(SbOperationState.Dismissed, dismissed!.State);
        // And it must not come back — dismiss is terminal.
        Assert.Null(await ops.ResumeAsync(opId, new StubSbClient()));
    }

    [Fact]
    public async Task Cancel_UnknownOrNonRunning_ReturnsNull()
    {
        using var sandbox = new AppDataSandbox();
        using var ops = new SbOperationService(new SbOperationJournalRepository());
        Assert.Null(await ops.CancelAsync(Guid.NewGuid()));
    }
}
