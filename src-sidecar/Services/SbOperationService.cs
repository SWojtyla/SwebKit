using System.Collections.Concurrent;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Runs Service Bus reach-message operations: an in-memory op record per run, a background task
/// driving park → act → restore through <see cref="IServiceBusClient"/>, and a crash journal
/// (<see cref="SbOperationJournalRepository"/>) written before the first park batch so a restarted
/// sidecar can detect an interrupted op. The journal is an accelerator — the DLQ stamp scan
/// (<see cref="IServiceBusClient.ScanParkedAsync"/>) is what rediscovers the parked set.
/// </summary>
/// <remarks>
/// Honesty contract (docs/features/active/service-bus-power-ops.md): cancel or crash mid-op leaves
/// parked messages in the DLQ — the op reports that, never hides it; resume re-runs the restore
/// phase; dismiss leaves the parked set dead-lettered on purpose. Restore is never positional —
/// parked messages come back as new copies appended at the tail.
/// </remarks>
public sealed class SbOperationService : IDisposable
{
    /// <summary>Default cap on prefix parks — the plan's hard cap before opt-in.</summary>
    public const int DefaultMaxParked = 1000;
    /// <summary>Absolute ceiling even with opt-in — past this the preview refuses.</summary>
    public const int AbsoluteMaxParked = 5000;
    /// <summary>Only reach-message exists today; kept on the record so other op kinds can join later.</summary>
    public const string ReachMessageKind = "reach-message";

    private readonly ConcurrentDictionary<Guid, Operation> _ops = new();
    private readonly SbOperationJournalRepository _journal;
    private readonly ILogger<SbOperationService>? _logger;
    /// <summary>Serializes op creation, journal reads and state transitions — ops are rare, so one gate is plenty.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public SbOperationService(SbOperationJournalRepository journal, ILogger<SbOperationService>? logger = null)
    {
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Mutable op record — the single writer is the op's runner task (plus Cancel/Dismiss under the gate).</summary>
    private sealed class Operation : IDisposable
    {
        public required Guid Id { get; init; }
        public required Guid NamespaceId { get; init; }
        public required string EntityPath { get; init; }
        public required string Kind { get; init; }
        public required long TargetSequenceNumber { get; init; }
        public required SbReachTargetAction TargetAction { get; init; }
        public required bool RestoreBeforeTarget { get; init; }
        public required int MaxParked { get; init; }
        public SbOperationState State { get; set; } = SbOperationState.Running;
        public SbOperationPhase? Phase { get; set; } = SbOperationPhase.Parking;
        public int ParkedCount { get; set; }
        public int RestoredCount { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Fresh source per run — a resumed op gets a new one so an earlier cancel can't leak in.</summary>
        public CancellationTokenSource Cancellation { get; set; } = new();
        public void Dispose() => Cancellation.Dispose();
    }

    /// <summary>Releases the gate and every op's cancellation source — called on host shutdown.</summary>
    public void Dispose()
    {
        foreach (var op in _ops.Values)
        {
            op.Dispose();
        }
        _gate.Dispose();
    }

    /// <summary>
    /// Loads the journal once and materializes ops for every entry the journal still tracks.
    /// An entry left <c>running</c> means the process died mid-op — it surfaces as
    /// <see cref="SbOperationState.Interrupted"/> (resumable/dismissable). Cancelled/failed entries
    /// stay resumable across restarts; terminal entries load as history.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            IReadOnlyList<SbOperationJournalEntry> entries;
            try
            {
                entries = await _journal.GetEntriesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Journal failure must never block operations — the DLQ stamp is the truth anyway.
                _logger?.LogWarning(ex, "Operation journal could not be read; starting with an empty operation list.");
                entries = [];
            }

            foreach (var entry in entries)
            {
                if (_ops.ContainsKey(entry.Id)) continue;
                var state = entry.Status switch
                {
                    SbOperationJournalStatus.Running => SbOperationState.Interrupted,
                    SbOperationJournalStatus.Cancelled => SbOperationState.Cancelled,
                    SbOperationJournalStatus.Failed => SbOperationState.Failed,
                    SbOperationJournalStatus.Dismissed => SbOperationState.Dismissed,
                    _ => SbOperationState.Completed,
                };
                _ops[entry.Id] = new Operation
                {
                    Id = entry.Id,
                    NamespaceId = entry.NamespaceId,
                    EntityPath = entry.EntityPath,
                    Kind = entry.Kind,
                    TargetSequenceNumber = entry.TargetSequenceNumber,
                    TargetAction = entry.TargetAction,
                    RestoreBeforeTarget = entry.RestoreBeforeTarget,
                    MaxParked = DefaultMaxParked,
                    State = state,
                    Phase = null,
                    ParkedCount = entry.ParkedCount,
                    RestoredCount = entry.RestoredCount,
                    Error = state == SbOperationState.Interrupted
                        ? "The sidecar was stopped or restarted mid-operation. Messages parked before the interruption remain in the dead-letter queue, stamped with this operation's id — Resume restores them as copies at the tail, or dismiss to leave them dead-lettered."
                        : entry.Error,
                    CreatedAt = entry.CreatedAt,
                    UpdatedAt = entry.UpdatedAt,
                };
            }

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>True while a reach-message op is still running against this entity — the second one is refused.</summary>
    public async Task<bool> HasRunningAsync(Guid namespaceId, string entityPath)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return _ops.Values.Any(o =>
            o.State == SbOperationState.Running &&
            o.NamespaceId == namespaceId &&
            string.Equals(o.EntityPath, entityPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Starts a reach-message op: journal entry first (the pre-park "running" write is the crash
    /// marker — without it a kill during parking is undetectable), then the runner task.
    /// Returns null when an op is already running on the entity.
    /// </summary>
    public async Task<SbOperationStatus?> TryStartReachAsync(
        Guid namespaceId,
        string entityPath,
        long targetSequenceNumber,
        SbReachTargetAction action,
        bool restoreBeforeTarget,
        int maxParked,
        IServiceBusClient client)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        Operation op;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_ops.Values.Any(o =>
                    o.State == SbOperationState.Running &&
                    o.NamespaceId == namespaceId &&
                    string.Equals(o.EntityPath, entityPath, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            op = new Operation
            {
                Id = Guid.NewGuid(),
                NamespaceId = namespaceId,
                EntityPath = entityPath,
                Kind = ReachMessageKind,
                TargetSequenceNumber = targetSequenceNumber,
                TargetAction = action,
                RestoreBeforeTarget = restoreBeforeTarget,
                MaxParked = maxParked,
            };
            _ops[op.Id] = op;
            await PersistAsync(op).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        _ = Task.Run(() => RunReachAsync(op, client));
        return Snapshot(op);
    }

    /// <summary>
    /// Poll — returns a snapshot, or null for an unknown id.
    /// </summary>
    public async Task<SbOperationStatus?> GetAsync(Guid operationId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return _ops.TryGetValue(operationId, out var op) ? Snapshot(op) : null;
    }

    /// <summary>Lists ops for an entity (or a whole namespace when <paramref name="entityPath"/> is null), newest first.</summary>
    public async Task<IReadOnlyList<SbOperationStatus>> ListAsync(Guid namespaceId, string? entityPath)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return _ops.Values
            .Where(o => o.NamespaceId == namespaceId &&
                        (entityPath is null || string.Equals(o.EntityPath, entityPath, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(o => o.CreatedAt)
            .Select(Snapshot)
            .ToList();
    }

    /// <summary>
    /// Cancellation is observed between receive batches — in-flight peek-locks are left to expire
    /// (~5 minutes) rather than force-settled. Parked messages stay in the DLQ; the op lands in
    /// <see cref="SbOperationState.Cancelled"/>, resumable.
    /// </summary>
    public async Task<SbOperationStatus?> CancelAsync(Guid operationId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        if (!_ops.TryGetValue(operationId, out var op) || op.State != SbOperationState.Running)
        {
            return null;
        }

        op.Cancellation.Cancel();
        return Snapshot(op);
    }

    /// <summary>
    /// Resume = run the restore phase again. Valid for interrupted (post-crash), failed and
    /// cancelled ops — anything with parked messages still stamped in the DLQ. Returns null when
    /// the op doesn't exist or is in a state where resume is meaningless (running, completed,
    /// dismissed).
    /// </summary>
    public async Task<SbOperationStatus?> ResumeAsync(Guid operationId, IServiceBusClient client)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_ops.TryGetValue(operationId, out var op) ||
                op.State is not (SbOperationState.Interrupted or SbOperationState.Failed or SbOperationState.Cancelled))
            {
                return null;
            }

            op.State = SbOperationState.Running;
            op.Phase = SbOperationPhase.Restoring;
            op.Error = null;
            op.UpdatedAt = DateTimeOffset.UtcNow;
            // A cancelled op's source is already tripped — replace it so the resumed run isn't
            // born cancelled.
            op.Cancellation.Dispose();
            op.Cancellation = new CancellationTokenSource();
            await PersistAsync(op).ConfigureAwait(false);
            _ = Task.Run(() => RunRestoreAsync(op, client));
            return Snapshot(op);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// "Leave in DLQ" — terminal. The parked copies keep their stamps so the DLQ record still says
    /// which op parked them.
    /// </summary>
    public async Task<SbOperationStatus?> DismissAsync(Guid operationId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_ops.TryGetValue(operationId, out var op) ||
                op.State is not (SbOperationState.Interrupted or SbOperationState.Failed or SbOperationState.Cancelled))
            {
                return null;
            }

            op.State = SbOperationState.Dismissed;
            op.Phase = null;
            op.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistAsync(op).ConfigureAwait(false);
            return Snapshot(op);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Runner ────────────────────────────────────────────────────────────────

    private async Task RunReachAsync(Operation op, IServiceBusClient client)
    {
        try
        {
            var parkProgress = new Progress<int>(count => { op.ParkedCount = count; op.UpdatedAt = DateTimeOffset.UtcNow; });
            var park = await client.ParkForReachAsync(
                op.EntityPath, op.TargetSequenceNumber, op.Id.ToString("N"),
                op.TargetAction, op.MaxParked, parkProgress, op.Cancellation.Token).ConfigureAwait(false);
            op.ParkedCount = park.ParkedCount;

            if (!park.TargetReached)
            {
                Fail(op, park.CapHit
                    ? $"Reached the {op.MaxParked}-message park cap before the target sequence {op.TargetSequenceNumber}. {park.ParkedCount} message(s) remain parked in the dead-letter queue — Resume restores them."
                    : $"The target sequence {op.TargetSequenceNumber} was never reached — a message at sequence {park.FirstSequenceBeyondTarget} arrived first, so the target is deferred or was consumed by another receiver. {park.ParkedCount} message(s) remain parked in the dead-letter queue — Resume restores them.");
                await PersistAsync(op).ConfigureAwait(false);
                return;
            }

            await TransitionAsync(op, SbOperationPhase.Restoring).ConfigureAwait(false);
            await RunRestoreAsync(op, client).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (op.Cancellation.IsCancellationRequested)
        {
            MarkCancelled(op);
            await PersistAsync(op).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Reach-message operation {OperationId} failed during {Phase}", op.Id, op.Phase);
            Fail(op, $"{SafeError(ex)} {ParkedRemainderNote(op)}");
            await PersistAsync(op).ConfigureAwait(false);
        }
    }

    private async Task RunRestoreAsync(Operation op, IServiceBusClient client)
    {
        try
        {
            var restoreProgress = new Progress<int>(count => { op.UpdatedAt = DateTimeOffset.UtcNow; });
            var restore = await client.RestoreParkedCopiesAsync(
                op.EntityPath, op.Id.ToString("N"), op.RestoreBeforeTarget, restoreProgress, op.Cancellation.Token).ConfigureAwait(false);
            op.RestoredCount += restore.RestoredCount;
            op.UpdatedAt = DateTimeOffset.UtcNow;

            if (restore.FailedCount > 0)
            {
                Fail(op, $"{restore.FailedCount} parked message(s) could not be restored — they remain stamped in the dead-letter queue. Resume retries them.");
            }
            else
            {
                op.State = SbOperationState.Completed;
                op.Phase = null;
                op.Error = null;
            }
            await PersistAsync(op).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (op.Cancellation.IsCancellationRequested)
        {
            MarkCancelled(op);
            await PersistAsync(op).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Reach-message operation {OperationId} failed during restore", op.Id);
            Fail(op, $"{SafeError(ex)} {ParkedRemainderNote(op)}");
            await PersistAsync(op).ConfigureAwait(false);
        }
    }

    private async Task TransitionAsync(Operation op, SbOperationPhase phase)
    {
        op.Phase = phase;
        op.UpdatedAt = DateTimeOffset.UtcNow;
        await PersistAsync(op).ConfigureAwait(false);
    }

    private void Fail(Operation op, string error)
    {
        op.State = SbOperationState.Failed;
        op.Phase = null;
        op.Error = error;
        op.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void MarkCancelled(Operation op)
    {
        op.State = SbOperationState.Cancelled;
        op.Phase = null;
        op.Error = $"Cancelled — {ParkedRemainderNote(op)}";
        op.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string ParkedRemainderNote(Operation op) =>
        $"{op.ParkedCount} message(s) remain parked in the dead-letter queue — Resume restores them as copies, or dismiss to leave them.";

    /// <summary>
    /// Our own validation exceptions carry safe text; anything else (SDK, IO) collapses to a
    /// generic message — exception text can carry connection details the UI must never show.
    /// </summary>
    private static string SafeError(Exception ex) =>
        ex is InvalidOperationException or ArgumentException
            ? ex.Message
            : "The operation failed against the broker — see the sidecar log for details.";

    private async Task PersistAsync(Operation op)
    {
        try
        {
            var status = op.State switch
            {
                SbOperationState.Completed => SbOperationJournalStatus.Completed,
                SbOperationState.Cancelled => SbOperationJournalStatus.Cancelled,
                SbOperationState.Failed => SbOperationJournalStatus.Failed,
                SbOperationState.Dismissed => SbOperationJournalStatus.Dismissed,
                // Running AND Interrupted both persist as "running" — a surviving running entry is
                // exactly what interrupted means after a restart.
                _ => SbOperationJournalStatus.Running,
            };

            await _journal.UpsertAsync(new SbOperationJournalEntry
            {
                Id = op.Id,
                NamespaceId = op.NamespaceId,
                EntityPath = op.EntityPath,
                Kind = op.Kind,
                TargetSequenceNumber = op.TargetSequenceNumber,
                TargetAction = op.TargetAction,
                RestoreBeforeTarget = op.RestoreBeforeTarget,
                Status = status,
                ParkedCount = op.ParkedCount,
                RestoredCount = op.RestoredCount,
                Error = op.Error,
                CreatedAt = op.CreatedAt,
                UpdatedAt = op.UpdatedAt,
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The journal is an accelerator — losing a write must never kill the op itself; the
            // DLQ stamp still marks parked messages for a post-restart scan.
            _logger?.LogWarning(ex, "Could not persist operation journal entry for {OperationId}", op.Id);
        }
    }

    private static SbOperationStatus Snapshot(Operation op) => new()
    {
        Id = op.Id,
        NamespaceId = op.NamespaceId,
        EntityPath = op.EntityPath,
        Kind = op.Kind,
        TargetSequenceNumber = op.TargetSequenceNumber,
        TargetAction = op.TargetAction,
        RestoreBeforeTarget = op.RestoreBeforeTarget,
        State = op.State,
        Phase = op.State == SbOperationState.Running ? op.Phase : null,
        ParkedCount = op.ParkedCount,
        RestoredCount = op.RestoredCount,
        Error = op.Error,
        CreatedAt = op.CreatedAt,
        UpdatedAt = op.UpdatedAt,
    };
}
