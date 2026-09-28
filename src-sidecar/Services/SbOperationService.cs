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
    /// <summary>Reach-message: park → act → restore against one entity.</summary>
    public const string ReachMessageKind = "reach-message";
    /// <summary>Cross-environment replay: source receive → clone → send to another namespace's entity.</summary>
    public const string ReplayToKind = "replay-to";

    private readonly ConcurrentDictionary<Guid, Operation> _ops = new();
    private readonly SbOperationJournalRepository _journal;
    private readonly ILogger<SbOperationService>? _logger;
    /// <summary>Serializes op creation, journal reads and state transitions — ops are rare, so one gate is plenty.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>Serializes journal writes — the throttled mid-run persist must not overlap the terminal one.</summary>
    private readonly SemaphoreSlim _persistGate = new(1, 1);
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

        // ── replay-to fields (Kind == ReplayToKind) ─────────────────────────
        public Guid? TargetNamespaceId { get; init; }
        public string? TargetEntityPath { get; init; }
        public bool SourceIsDeadLetter { get; init; }
        public bool ScrubProperties { get; init; }
        public bool StripSessionId { get; init; }
        public bool RemoveSource { get; init; }
        /// <summary>Provenance value stamped on every copy — "{sourceNamespace}/{entityPath}".</summary>
        public string? ReplayedFrom { get; init; }
        public IReadOnlyList<long> RequestedSequences { get; init; } = [];
        /// <summary>Confirmed-processed sequences — the resume skip-set, journaled periodically.</summary>
        public HashSet<long> ProcessedSequences { get; } = [];
        /// <summary>Serializes processed-set reads/writes — progress callbacks arrive off-thread.</summary>
        public object ProcessedLock { get; } = new();
        public int ReplayedCount { get; set; }
        public int ReplayFailedCount { get; set; }
        public int ReplayMissingCount { get; set; }
        /// <summary>Throttle guard: prevents overlapping journal writes from per-message progress.</summary>
        public int PersistInFlight;

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
                    TargetNamespaceId = entry.TargetNamespaceId,
                    TargetEntityPath = entry.TargetEntityPath,
                    SourceIsDeadLetter = entry.SourceIsDeadLetter,
                    ScrubProperties = entry.ScrubProperties,
                    StripSessionId = entry.StripSessionId,
                    RemoveSource = entry.RemoveSource,
                    ReplayedFrom = entry.ReplayedFrom,
                    RequestedSequences = entry.RequestedSequences,
                    ReplayedCount = entry.ReplayedCount,
                    ReplayFailedCount = entry.ReplayFailedCount,
                    ReplayMissingCount = entry.ReplayMissingCount,
                    Error = state == SbOperationState.Interrupted
                        ? InterruptedNote(entry.Kind)
                        : entry.Error,
                    CreatedAt = entry.CreatedAt,
                    UpdatedAt = entry.UpdatedAt,
                };
                if (entry.ProcessedSequences.Count > 0)
                {
                    _ops[entry.Id].ProcessedSequences.UnionWith(entry.ProcessedSequences);
                }
            }

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// "The sidecar died mid-op" note — kind-aware: reach-message strands parked copies in the DLQ;
    /// replay strands nothing but may have partially transferred.
    /// </summary>
    private static string InterruptedNote(string kind) =>
        kind == ReplayToKind
            ? "The sidecar was stopped or restarted mid-replay. Copies already sent remain on the target; unprocessed source messages are untouched. Resume continues from the confirmed set — a crash between send and source-settle can duplicate a copy on the target."
            : "The sidecar was stopped or restarted mid-operation. Messages parked before the interruption remain in the dead-letter queue, stamped with this operation's id — Resume restores them as copies at the tail, or dismiss to leave them dead-lettered.";

    /// <summary>True while ANY op is still running against this entity — the second one is refused regardless of kind.</summary>
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
    /// Starts a cross-environment replay op: journal entry first (the "running" write is the crash
    /// marker), then the runner task driving receive → clone → send through the source and target
    /// clients. Returns null when an op is already running on the source entity — two ops racing the
    /// same receive path could settle each other's messages.
    /// </summary>
    public async Task<SbOperationStatus?> TryStartReplayAsync(
        Guid namespaceId,
        string entityPath,
        Guid targetNamespaceId,
        string targetEntityPath,
        IReadOnlyList<long> sequenceNumbers,
        bool deadLetter,
        bool scrubProperties,
        bool stripSessionId,
        bool removeSource,
        string replayedFrom,
        IServiceBusClient sourceClient,
        IServiceBusClient targetClient)
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
                Kind = ReplayToKind,
                // Reach-only fields carry inert defaults on a replay op.
                TargetSequenceNumber = 0,
                TargetAction = SbReachTargetAction.Complete,
                RestoreBeforeTarget = false,
                MaxParked = 0,
                Phase = SbOperationPhase.Transferring,
                TargetNamespaceId = targetNamespaceId,
                TargetEntityPath = targetEntityPath,
                SourceIsDeadLetter = deadLetter,
                ScrubProperties = scrubProperties,
                StripSessionId = stripSessionId,
                RemoveSource = removeSource,
                ReplayedFrom = replayedFrom,
                RequestedSequences = sequenceNumbers,
            };
            _ops[op.Id] = op;
            await PersistAsync(op).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        _ = Task.Run(() => RunReplayAsync(op, sourceClient, targetClient));
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
    /// Resume = re-run the resumable phase: restore for reach-message, the transfer for replay —
    /// skipping sequences the journal already confirmed. Valid for interrupted (post-crash),
    /// failed and cancelled ops. Returns null when the op doesn't exist or is in a state where
    /// resume is meaningless (running, completed, dismissed).
    /// </summary>
    /// <param name="targetClient">Replay ops only — the client for the journaled target namespace.
    /// The endpoint resolves it; a null here means the target no longer resolves and the op is
    /// failed honestly instead of silently dropped.</param>
    public async Task<SbOperationStatus?> ResumeAsync(Guid operationId, IServiceBusClient client, IServiceBusClient? targetClient = null)
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

            if (op.Kind == ReplayToKind && targetClient is null)
            {
                // Honest failure, not a silent drop: the op stays resumable-shaped in the journal
                // but reports why resume cannot proceed.
                Fail(op, "Cannot resume — the target namespace this replay was sending to no longer resolves. The confirmed copies are already on it; unprocessed source messages are untouched.");
                await PersistAsync(op).ConfigureAwait(false);
                return Snapshot(op);
            }

            op.State = SbOperationState.Running;
            op.Phase = op.Kind == ReplayToKind ? SbOperationPhase.Transferring : SbOperationPhase.Restoring;
            op.Error = null;
            op.UpdatedAt = DateTimeOffset.UtcNow;
            // A cancelled op's source is already tripped — replace it so the resumed run isn't
            // born cancelled.
            op.Cancellation.Dispose();
            op.Cancellation = new CancellationTokenSource();
            await PersistAsync(op).ConfigureAwait(false);
            _ = Task.Run(() => op.Kind == ReplayToKind
                ? RunReplayAsync(op, client, targetClient!)
                : RunRestoreAsync(op, client));
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

    /// <summary>
    /// Replay runner: one pass of receive → clone → send → (optionally) settle through the source
    /// client. Progress callbacks journal each confirmed sequence — throttled, because the crash
    /// that matters mid-run is the one that would otherwise force resume to re-send confirmed
    /// copies (the at-least-once duplicate window stays honest: send-then-crash can still dupe).
    /// </summary>
    private async Task RunReplayAsync(Operation op, IServiceBusClient sourceClient, IServiceBusClient targetClient)
    {
        try
        {
            // Inline (synchronous) progress — Progress<T> would post to the thread pool and lag
            // behind the loop, so a cancel could persist a stale processed-set and make resume
            // re-send confirmed copies.
            var progress = new InlineProgress<SbReplayProgress>(p =>
            {
                if (!p.Succeeded)
                {
                    return;
                }
                var flush = false;
                lock (op.ProcessedLock)
                {
                    op.ProcessedSequences.Add(p.SequenceNumber);
                    op.ReplayedCount++;
                    // Persist the skip-set every 25 confirmed sends — bounds how much a crash
                    // makes resume re-do without turning every send into a file write.
                    flush = op.ProcessedSequences.Count % 25 == 0;
                }
                op.UpdatedAt = DateTimeOffset.UtcNow;
                if (flush)
                {
                    PersistThrottled(op);
                }
            });

            HashSet<long> alreadyProcessed;
            lock (op.ProcessedLock)
            {
                alreadyProcessed = [.. op.ProcessedSequences];
            }

            var result = await sourceClient.ReplayMessagesAsync(
                op.EntityPath,
                op.RequestedSequences,
                op.SourceIsDeadLetter,
                targetClient,
                op.TargetEntityPath!,
                new SbReplayOptions
                {
                    ScrubApplicationProperties = op.ScrubProperties,
                    StripSessionId = op.StripSessionId,
                    RemoveSource = op.RemoveSource,
                    ReplayedFrom = op.ReplayedFrom!,
                    OperationId = op.Id.ToString("N"),
                },
                alreadyProcessed,
                progress,
                op.Cancellation.Token).ConfigureAwait(false);

            // Merge in case the client confirmed work without reporting progress per message.
            lock (op.ProcessedLock)
            {
                op.ProcessedSequences.UnionWith(result.ProcessedSequenceNumbers);
            }
            op.ReplayFailedCount += result.FailedCount;
            op.ReplayMissingCount = result.MissingSequenceNumbers.Count;
            op.UpdatedAt = DateTimeOffset.UtcNow;

            if (result.FailedCount > 0)
            {
                Fail(op, $"{result.FailedCount} message(s) could not be replayed — they remain in the source. Resume retries them. {MissingNote(op)}");
            }
            else
            {
                op.State = SbOperationState.Completed;
                op.Phase = null;
                op.Error = op.ReplayMissingCount > 0 ? MissingNote(op) : null;
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
            _logger?.LogError(ex, "Replay operation {OperationId} failed during transfer", op.Id);
            Fail(op, $"{SafeError(ex)} {MissingNote(op)}");
            await PersistAsync(op).ConfigureAwait(false);
        }
    }

    /// <summary>Invokes <see cref="IProgress{T}.Report"/> on the caller thread — no sync-context posting.</summary>
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    /// <summary>Fire-and-forget journal write guarded against overlap — progress callbacks can stack up.</summary>
    private void PersistThrottled(Operation op)
    {
        if (Interlocked.Exchange(ref op.PersistInFlight, 1) != 0)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await PersistAsync(op).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref op.PersistInFlight, 0);
            }
        });
    }

    /// <summary>Replay-only tail of failure/completion text — how much of the request never matched.</summary>
    private static string MissingNote(Operation op) =>
        op.ReplayMissingCount > 0
            ? $"{op.ReplayMissingCount} requested message(s) were never found in the source (consumed, expired or already moved)."
            : "";

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
        op.Error = $"Cancelled — {RemainderNote(op)}";
        op.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string ParkedRemainderNote(Operation op) =>
        $"{op.ParkedCount} message(s) remain parked in the dead-letter queue — Resume restores them as copies, or dismiss to leave them.";

    /// <summary>The "what's left behind" sentence — kind-aware: parking strands DLQ copies; replay just stops mid-set.</summary>
    private static string RemainderNote(Operation op) =>
        op.Kind == ReplayToKind
            ? $"{op.ReplayedCount} of {op.RequestedSequences.Count} message(s) replayed so far; unprocessed source messages are untouched{(op.RemoveSource ? " (unsent copies stay in the source)" : " — nothing was removed from the source")}. Resume continues from the confirmed set."
            : ParkedRemainderNote(op);

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

            // Serialized against the throttled mid-run writes PersistThrottled kicks off — two
            // overlapping upserts would collide on the repository's shared tmp file, and a
            // stale snapshot could land AFTER the terminal one.
            await _persistGate.WaitAsync().ConfigureAwait(false);
            try
            {
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
                TargetNamespaceId = op.TargetNamespaceId,
                TargetEntityPath = op.TargetEntityPath,
                SourceIsDeadLetter = op.SourceIsDeadLetter,
                ScrubProperties = op.ScrubProperties,
                StripSessionId = op.StripSessionId,
                RemoveSource = op.RemoveSource,
                ReplayedFrom = op.ReplayedFrom,
                RequestedSequences = [.. op.RequestedSequences],
                ProcessedSequences = SnapshotProcessed(op),
                ReplayedCount = op.ReplayedCount,
                ReplayFailedCount = op.ReplayFailedCount,
                ReplayMissingCount = op.ReplayMissingCount,
            }).ConfigureAwait(false);
            }
            finally
            {
                _persistGate.Release();
            }
        }
        catch (Exception ex)
        {
            // The journal is an accelerator — losing a write must never kill the op itself; the
            // DLQ stamp still marks parked messages for a post-restart scan.
            _logger?.LogWarning(ex, "Could not persist operation journal entry for {OperationId}", op.Id);
        }
    }

    private static List<long> SnapshotProcessed(Operation op)
    {
        lock (op.ProcessedLock)
        {
            return [.. op.ProcessedSequences];
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
        TargetNamespaceId = op.TargetNamespaceId,
        TargetEntityPath = op.TargetEntityPath,
        SourceIsDeadLetter = op.SourceIsDeadLetter,
        RemoveSource = op.RemoveSource,
        RequestedCount = op.RequestedSequences.Count,
        ReplayedCount = op.ReplayedCount,
        FailedCount = op.ReplayFailedCount,
        MissingCount = op.ReplayMissingCount,
    };
}
