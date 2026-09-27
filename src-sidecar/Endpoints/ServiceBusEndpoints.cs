using SwebKit.Sidecar.Services;
using System;
using System.Linq;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Sidecar.Endpoints;

public static class ServiceBusEndpoints
{
    public static void MapServiceBusEndpoints(this WebApplication app)
    {
        // ── Namespace selection ────────────────────────────────────────────────
        // The frontend selects a namespace by ID from the profile, then all
        // subsequent calls pass the namespace ID as a route parameter.
        // The sidecar resolves the ID → connection string → IServiceBusClient.

        app.MapGet("/api/servicebus/{nsId}/test", async (
            string nsId,
            ProfileRepository profile,
            IServiceBusConnectionPool pool,
            DemoModeService demo,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return ApiErrors.NotFound("Namespace not found");

            try
            {
                var client = pool.GetOrCreate(ns);
                var ok = await client.TestConnectionAsync(ct);
                return Results.Ok(new { connected = ok });
            }
            catch (Exception ex)
            {
                // The Service Bus connection string/Entra details can appear in the underlying SDK
                // exception's message — never return ex.Message here.
                logger.LogWarning(ex, "Service Bus connection test failed for namespace {NamespaceId}", nsId);
                return Results.Ok(new { connected = false, error = ConnectionTestError.Describe(ex) });
            }
        });

        app.MapGet("/api/servicebus/{nsId}/info", async (
            string nsId,
            ProfileRepository profile,
            IServiceBusConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return ApiErrors.NotFound("Namespace not found");

            var client = pool.GetOrCreate(ns);
            var info = await client.GetNamespaceInfoAsync(ct);
            return Results.Ok(info);
        });

        app.MapGet("/api/servicebus/{nsId}/queues", async (
            string nsId,
            ProfileRepository profile,
            IServiceBusConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return ApiErrors.NotFound("Namespace not found");

            var client = pool.GetOrCreate(ns);
            var queues = await client.ListQueuesAsync(ct);
            return Results.Ok(queues);
        });

        app.MapGet("/api/servicebus/{nsId}/topics", async (
            string nsId,
            ProfileRepository profile,
            IServiceBusConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return ApiErrors.NotFound("Namespace not found");

            var client = pool.GetOrCreate(ns);
            var topics = await client.ListTopicsAsync(ct);
            return Results.Ok(topics);
        });

        app.MapGet("/api/servicebus/{nsId}/topics/{topic}/subscriptions", async (
            string nsId,
            string topic,
            ProfileRepository profile,
            IServiceBusConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return ApiErrors.NotFound("Namespace not found");

            var client = pool.GetOrCreate(ns);
            var subs = await client.ListSubscriptionsAsync(topic, ct);
            return Results.Ok(subs);
        });

        app.MapGet("/api/servicebus/{nsId}/entities/{entityPath}/stats", async (
            string nsId,
            string entityPath,
            ProfileRepository profile,
            IServiceBusConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return ApiErrors.NotFound("Namespace not found");

            var client = pool.GetOrCreate(ns);
            var stats = await client.GetEntityStatsAsync(entityPath, ct);
            return Results.Ok(stats);
        });

        app.MapGet("/api/servicebus/{nsId}/entities/{entityPath}/peek", PeekMessagesAsync);

        app.MapGet("/api/servicebus/{nsId}/entities/{entityPath}/dlq", PeekDeadLetterAsync);

        app.MapGet("/api/servicebus/{nsId}/entities/{entityPath}/sessions", PeekSessionsAsync);

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/send", async (
            string nsId,
            string entityPath,
            SbMessage message,
            ProfileRepository profile,
            IServiceBusConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return ApiErrors.NotFound("Namespace not found");

            var client = pool.GetOrCreate(ns);
            await client.SendMessageAsync(entityPath, message, ct);
            return Results.Ok();
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/batch-send", async (
            string nsId,
            string entityPath,
            List<SbMessage> messages,
            ProfileRepository profile,
            IServiceBusConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return ApiErrors.NotFound("Namespace not found");

            var client = pool.GetOrCreate(ns);
            await client.SendBatchAsync(entityPath, messages, ct);
            return Results.Ok(new { sent = messages.Count });
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/schedule", async (
            string nsId,
            string entityPath,
            ScheduleRequest req,
            ProfileRepository profile,
            IServiceBusConnectionPool pool,
            DemoModeService demo,
            ScheduledMessageRepository schedRepo,
            CancellationToken ct) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return ApiErrors.NotFound("Namespace not found");

            var client = pool.GetOrCreate(ns);
            var seq = await client.ScheduleMessageAsync(entityPath, req.Message, req.ScheduledEnqueueTime, ct);

            var entry = new ScheduledMessageEntry
            {
                NamespaceId = ns.Id,
                EntityPath = entityPath,
                SequenceNumber = seq,
                ScheduledEnqueueTime = req.ScheduledEnqueueTime,
                MessageId = req.Message.MessageId,
                Subject = req.Message.Subject,
                CorrelationId = req.Message.CorrelationId,
            };
            await schedRepo.AddAsync(entry);

            return Results.Ok(new { sequenceNumber = seq });
        });

        app.MapGet("/api/servicebus/{nsId}/entities/{entityPath}/scheduled", (
            string nsId,
            string entityPath,
            ScheduledMessageRepository schedRepo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            if (!Guid.TryParse(nsId, out var id))
                return ApiErrors.BadRequest("Invalid namespace ID");

            var entries = schedRepo.GetByEntity(id, entityPath);
            return Results.Ok(entries);
        });

        app.MapDelete("/api/servicebus/{nsId}/entities/{entityPath}/scheduled/{sequenceNumber}", async (
            string nsId,
            string entityPath,
            long sequenceNumber,
            ProfileRepository profile,
            IServiceBusConnectionPool pool,
            DemoModeService demo,
            ScheduledMessageRepository schedRepo,
            CancellationToken ct) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return ApiErrors.NotFound("Namespace not found");

            var client = pool.GetOrCreate(ns);
            await client.CancelScheduledMessageAsync(entityPath, sequenceNumber, ct);

            var entries = schedRepo.GetByEntity(ns.Id, entityPath);
            var entry = entries.FirstOrDefault(e => e.SequenceNumber == sequenceNumber);
            if (entry is not null)
                await schedRepo.RemoveAsync(entry.Id);

            return Results.Ok();
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/complete", CompleteMessagesAsync);

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/deadletter", DeadLetterMessagesAsync);

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/purge", PurgeMessagesAsync);

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/dlq/complete", async (
            string nsId,
            string entityPath,
            string[] sequenceNumbers,
            ProfileRepository profile,
            IServiceBusConnectionPool pool,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return ApiErrors.NotFound("Namespace not found");

            var client = pool.GetOrCreate(ns);
            await client.CompleteDeadLetterAsync(entityPath, sequenceNumbers, ct);
            return Results.Ok();
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/resubmit", ResubmitDeadLetterAsync);

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/dlq/resubmit-edited", ResubmitEditedDeadLetterAsync);

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/resend", ResendMessagesAsync);

        // DLQ triage beyond the peek window — resubmit everything matching a reason/description
        // pair, server-side, up to a limit.
        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/dlq/requeue-by-filter", ResubmitDeadLetterByFilterAsync);

        // ── Reach-message power op ───────────────────────────────────────────
        // The op service is constructed here rather than registered in Program.cs (which a
        // concurrent workstream owns): dependencies are all resolvable from app.Services, and the
        // instance is captured by the lambdas below — effectively a singleton for the app's life.
        var sbOperationJournal = new SbOperationJournalRepository(
            app.Services.GetService<ILogger<SbOperationJournalRepository>>());
        var sbOperations = new SbOperationService(
            sbOperationJournal,
            app.Services.GetService<ILogger<SbOperationService>>());

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/reach-message/preview",
            (string nsId, string entityPath, ReachMessageRequest req,
             ProfileRepository profile, IServiceBusConnectionPool pool, DemoModeService demo,
             CancellationToken ct) =>
                ReachMessagePreviewAsync(nsId, entityPath, req, profile, pool, demo, sbOperations, ct));

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/reach-message/start",
            (string nsId, string entityPath, ReachMessageRequest req,
             ProfileRepository profile, IServiceBusConnectionPool pool, DemoModeService demo,
             CancellationToken ct) =>
                ReachMessageStartAsync(nsId, entityPath, req, profile, pool, demo, sbOperations, ct));

        app.MapGet("/api/servicebus/{nsId}/operations/{operationId}",
            (string nsId, string operationId,
             ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
                GetOperationAsync(nsId, operationId, profile, demo, sbOperations, ct));

        app.MapPost("/api/servicebus/{nsId}/operations/{operationId}/cancel",
            (string nsId, string operationId,
             ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
                CancelOperationAsync(nsId, operationId, profile, demo, sbOperations, ct));

        app.MapPost("/api/servicebus/{nsId}/operations/{operationId}/resume",
            (string nsId, string operationId,
             ProfileRepository profile, IServiceBusConnectionPool pool, DemoModeService demo,
             CancellationToken ct) =>
                ResumeOperationAsync(nsId, operationId, profile, pool, demo, sbOperations, ct));

        app.MapPost("/api/servicebus/{nsId}/operations/{operationId}/dismiss",
            (string nsId, string operationId,
             ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
                DismissOperationAsync(nsId, operationId, profile, demo, sbOperations, ct));

        app.MapGet("/api/servicebus/{nsId}/operations",
            (string nsId, string? entity,
             ProfileRepository profile, IServiceBusConnectionPool pool, DemoModeService demo,
             CancellationToken ct) =>
                ListOperationsAsync(nsId, entity, profile, pool, demo, sbOperations, ct));

        // ── Message Templates ──────────────────────────────────────────────────
        app.MapGet("/api/servicebus/templates", (ProfileRepository profile) =>
        Results.Ok(profile.MessageTemplates));

        app.MapPost("/api/servicebus/templates", (SbMessageTemplate template, ProfileRepository profile) =>
        {
            profile.SaveMessageTemplate(template);
            return Results.Ok(template);
        });

        app.MapDelete("/api/servicebus/templates/{id}", (string id, ProfileRepository profile) =>
        {
            if (!Guid.TryParse(id, out var guid))
                return ApiErrors.BadRequest("Invalid template ID");

            profile.DeleteMessageTemplate(guid);
            return Results.Ok();
        });
    }

    // ── Extracted handlers (unit-testable without a WebApplicationFactory) ────────────

    // The UI offers at most 200 per page; a bigger (or non-positive) count would make the SDK
    // enumerate far more than the list can render — the request that used to hang and 500 on
    // queues with tens of thousands of messages.
    private const int MaxPeekCount = 250;

    internal static int ClampCount(int count) => Math.Clamp(count, 1, MaxPeekCount);

    /// <summary>Handler body for the active-message peek endpoint.</summary>
    internal static async Task<IResult> PeekMessagesAsync(
        string nsId,
        string entityPath,
        int count,
        long? fromSeq,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");

        var client = pool.GetOrCreate(ns);
        var messages = await client.PeekMessagesAsync(entityPath, ClampCount(count), ct, fromSequenceNumber: fromSeq);
        return Results.Ok(messages);
    }

    /// <summary>Handler body for the dead-letter peek endpoint.</summary>
    internal static async Task<IResult> PeekDeadLetterAsync(
        string nsId,
        string entityPath,
        int count,
        long? fromSeq,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");

        var client = pool.GetOrCreate(ns);
        var messages = await client.PeekDeadLetterAsync(entityPath, ClampCount(count), ct, fromSequenceNumber: fromSeq);
        return Results.Ok(messages);
    }

    /// <summary>
    /// Handler body for the session-summary endpoint — the peek window grouped by session id.
    /// Clamped like peek for the same reason: sessions beyond the window are absent by design.
    /// </summary>
    internal static async Task<IResult> PeekSessionsAsync(
        string nsId,
        string entityPath,
        int count,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");

        var client = pool.GetOrCreate(ns);
        var sessions = await client.PeekSessionsAsync(entityPath, ClampCount(count), ct);
        return Results.Ok(sessions);
    }

    /// <summary>
    /// Handler body for the message-complete mutation endpoint. Regression coverage target: the
    /// production-readiness review flagged a possible duplicate-mutation bug (a React key-collision
    /// bug caused duplicate-rendered notifications, which might indicate the underlying action fires
    /// twice, not just renders twice) — tests for this method assert the underlying client's
    /// <c>CompleteMessagesAsync</c> is invoked exactly once per handler invocation.
    /// </summary>
    internal static async Task<IResult> CompleteMessagesAsync(
        string nsId,
        string entityPath,
        long[] sequenceNumbers,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");

        var client = pool.GetOrCreate(ns);
        var count = await client.CompleteMessagesAsync(entityPath, sequenceNumbers, ct);
        return Results.Ok(new { completed = count });
    }

    /// <summary>
    /// Handler body for the move-to-dead-letter mutation endpoint. Same "exactly once" regression
    /// concern as <see cref="CompleteMessagesAsync"/> — tests assert <c>DeadLetterMessagesAsync</c>
    /// is invoked exactly once per handler invocation.
    /// </summary>
    internal static async Task<IResult> DeadLetterMessagesAsync(
        string nsId,
        string entityPath,
        long[] sequenceNumbers,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");

        var client = pool.GetOrCreate(ns);
        var count = await client.DeadLetterMessagesAsync(entityPath, sequenceNumbers, ct);
        return Results.Ok(new { deadLettered = count });
    }

    /// <summary>Handler body for the purge mutation endpoint.</summary>
    internal static async Task<IResult> PurgeMessagesAsync(
        string nsId,
        string entityPath,
        PurgeRequest req,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");

        var client = pool.GetOrCreate(ns);
        var count = await client.PurgeMessagesAsync(entityPath, req.DeadLetter, ct);
        return Results.Ok(new { purged = count });
    }

    /// <summary>
    /// Handler body for the dead-letter resubmit mutation endpoint. Same "exactly once" regression
    /// concern as <see cref="CompleteMessagesAsync"/> — tests assert <c>ResubmitDeadLetterAsync</c> is
    /// invoked exactly once per handler invocation.
    /// </summary>
    internal static async Task<IResult> ResubmitDeadLetterAsync(
        string nsId,
        string entityPath,
        ResubmitRequest req,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");

        var client = pool.GetOrCreate(ns);
        await client.ResubmitDeadLetterAsync(entityPath, req.SequenceNumbers, req.TargetEntityPath, req.RemapRules, ct);
        return Results.Ok();
    }

    /// <summary>
    /// Handler body for the resend-to-original-queue mutation endpoint. Same "exactly once"
    /// regression concern as <see cref="CompleteMessagesAsync"/> — tests assert
    /// <c>ResendMessagesAsync</c> is invoked exactly once per handler invocation.
    /// </summary>
    internal static async Task<IResult> ResendMessagesAsync(
        string nsId,
        string entityPath,
        ResendRequest req,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");

        var client = pool.GetOrCreate(ns);
        var resent = await client.ResendMessagesAsync(entityPath, req.SequenceNumbers, req.DeadLetter, ct);
        return Results.Ok(new { resent });
    }

    /// <summary>
    /// Handler body for resubmit-with-edit: the client settles the DLQ original after sending the
    /// edited clone, so the pre-edit message can't linger next to its edited copy — the duplicate
    /// trap the old copy-only composer flow created.
    /// </summary>
    internal static async Task<IResult> ResubmitEditedDeadLetterAsync(
        string nsId,
        string entityPath,
        ResubmitEditedRequest req,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");

        if (req.Message is null)
        {
            return ApiErrors.BadRequest("message is required");
        }

        var client = pool.GetOrCreate(ns);
        await client.ResubmitEditedDeadLetterAsync(entityPath, req.SequenceNumber, req.Message, req.TargetEntityPath, ct);
        return Results.Ok();
    }

    // ── Reach-message power op ────────────────────────────────────────────────
    //
    // The endpoints are deliberately thin: preview computes consequences + refusals, the
    // SbOperationService owns the background state machine, and the client's park/restore
    // primitives do the broker work. The UI must never oversell restore — the consequences
    // text below is the plan's honest contract, verbatim.

    /// <summary>How many messages the preview peeks to locate the target in the current window.</summary>
    private const int ReachPreviewPeekCount = 1000;
    /// <summary>DLQ requeue-by-filter ceiling per call.</summary>
    private const int RequeueByFilterLimitCap = 5000;

    /// <summary>Clamps an optional caller cap to the plan's 1,000 default / 5,000 ceiling.</summary>
    internal static int ClampMaxParked(int? requested) =>
        requested is null or <= 0
            ? SbOperationService.DefaultMaxParked
            : Math.Min(requested.Value, SbOperationService.AbsoluteMaxParked);

    /// <summary>
    /// Looks the entity up in the namespace topology so preview/start can refuse session-required
    /// entities (every settle path here uses plain receivers — the broker would reject them) and
    /// bare topics (receive-only parents). Returns null when the entity can't be found — the caller
    /// treats that as a warning, not a refusal, since topology listing can lag.
    /// </summary>
    internal static async Task<SbEntityInfo?> FindEntityInfoAsync(IServiceBusClient client, string entityPath, CancellationToken ct)
    {
        const string marker = "/subscriptions/";
        var markerIndex = entityPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex > 0)
        {
            var topic = entityPath[..markerIndex];
            var subs = await client.ListSubscriptionsAsync(topic, ct).ConfigureAwait(false);
            return subs.FirstOrDefault(s => string.Equals(s.EntityPath, entityPath, StringComparison.OrdinalIgnoreCase));
        }

        var queues = await client.ListQueuesAsync(ct).ConfigureAwait(false);
        var queue = queues.FirstOrDefault(q => string.Equals(q.EntityPath, entityPath, StringComparison.OrdinalIgnoreCase));
        if (queue is not null)
        {
            return queue;
        }

        // Bare topic names resolve here so the refusal can say "topics aren't receivable" instead
        // of pretending the entity doesn't exist.
        var topics = await client.ListTopicsAsync(ct).ConfigureAwait(false);
        return topics.FirstOrDefault(t => string.Equals(t.EntityPath, entityPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Refusal checks shared by preview and start. Returns a human-readable reason, or null when the
    /// op may proceed to its window analysis. Session entities are refused outright — the plan gates
    /// them before any receive starts because every settle path needs session receivers.
    /// </summary>
    private static async Task<string?> EntityRefusalAsync(IServiceBusClient client, string entityPath, CancellationToken ct)
    {
        var entity = await FindEntityInfoAsync(client, entityPath, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null; // unknown to topology — warn downstream, don't refuse
        }
        if (entity.IsTopic && !entity.IsSubscription)
        {
            return $"'{entityPath}' is a topic — topics aren't receivable. Select one of its subscriptions instead.";
        }
        if (entity.RequiresSession)
        {
            return $"'{entityPath}' requires sessions — reach-message needs plain peek-lock receivers, which session entities reject. Session entities aren't supported yet.";
        }
        return null;
    }

    /// <summary>
    /// The consequences block the UI must render — the plan's honest contract in words. "Like
    /// nothing happened" is never promised: restored messages are new tail-appended copies that
    /// keep only payload + relative order.
    /// </summary>
    internal static List<string> BuildReachConsequences(ReachMessageRequest req, long? prefixCount, string entityPath)
    {
        var prefix = prefixCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "an unknown number of";
        var consequences = new List<string>
        {
            $"Will park {prefix} message(s) ahead of sequence {req.TargetSequenceNumber} into {entityPath}'s dead-letter queue — each stamped with this operation's id in the same settlement call, so a crash can never silently strand them.",
            req.Action switch
            {
                SbReachTargetAction.Complete =>
                    $"Will complete the target message at sequence {req.TargetSequenceNumber} — settled and removed, no copy returns.",
                SbReachTargetAction.DeadLetter =>
                    $"Will dead-letter the target message at sequence {req.TargetSequenceNumber} — it stays in the DLQ with reason {SbRequeueStamp.TargetDeadLetterReason}.",
                _ =>
                    $"Will resubmit the target message at sequence {req.TargetSequenceNumber} — its copy lands at the queue tail {(req.RestoreBeforeTarget ? "after" : "before")} the restored prefix copies.",
            },
            $"Will restore the parked messages as NEW copies appended at the tail of {entityPath}. Bodies, subjects, correlation ids and application properties survive, and the restored copies keep their original relative order — but sequence numbers, queue positions, enqueue times and delivery counts are NOT restored. Restored copies are stamped SwebKit.RequeueRestored so their provenance is visible.",
        };
        return consequences;
    }

    /// <summary>Warnings every preview carries — plus the beyond-window warning when relevant.</summary>
    internal static List<string> BuildReachWarnings(bool targetBeyondWindow, int maxParked)
    {
        var warnings = new List<string>
        {
            "Other consumers racing this entity can't be detected — a message a consumer completes first is skipped, and restore tolerates the gap.",
            "If you cancel or the app closes mid-operation, in-flight message locks expire within ~5 minutes; already-parked messages stay in the dead-letter queue until you resume or dismiss the operation.",
            "Messages can expire by TTL mid-operation — restore skips any that disappeared and reports the difference.",
        };
        if (targetBeyondWindow)
        {
            warnings.Add($"The target is beyond the current peek window, so the prefix count is unknown — the operation parks up to {maxParked} messages and stops honestly if the cap is hit before the target.");
        }
        return warnings;
    }

    /// <summary>
    /// Preview: refuse clearly for session entities / topics / already-running ops / deferred
    /// targets; otherwise compute the prefix estimate from the peek window and return the honest
    /// consequences list the wizard renders before confirmation.
    /// </summary>
    internal static async Task<IResult> ReachMessagePreviewAsync(
        string nsId,
        string entityPath,
        ReachMessageRequest req,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        SbOperationService ops,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");
        if (req.TargetSequenceNumber <= 0)
        {
            return ApiErrors.BadRequest("targetSequenceNumber must be a positive sequence number.");
        }

        var maxParked = ClampMaxParked(req.MaxParked);
        var client = pool.GetOrCreate(ns);

        var refusal = await EntityRefusalAsync(client, entityPath, ct);
        if (refusal is not null)
        {
            return Results.Ok(ReachMessagePreview.Refused(refusal, maxParked));
        }
        if (await ops.HasRunningAsync(ns.Id, entityPath))
        {
            return Results.Ok(ReachMessagePreview.Refused(
                "A reach-message operation is already running on this entity — wait for it or cancel it first.", maxParked));
        }

        var window = await client.PeekMessagesAsync(entityPath, ReachPreviewPeekCount, ct);
        var inWindow = window.Any(m => m.SequenceNumber == req.TargetSequenceNumber);
        long? prefixCount = null;
        var targetBeyondWindow = false;
        string? windowRefusal = null;

        if (inWindow)
        {
            prefixCount = window.Count(m => m.SequenceNumber < req.TargetSequenceNumber);
            if (prefixCount > maxParked)
            {
                windowRefusal = $"{prefixCount} messages sit ahead of the target — over the {maxParked} park cap. Raise maxParked (up to {SbOperationService.AbsoluteMaxParked}) or pick a closer target.";
            }
        }
        else
        {
            var maxSeen = window.Count > 0 ? window.Max(m => m.SequenceNumber ?? 0) : (long?)null;
            if (maxSeen is null)
            {
                windowRefusal = "The entity has no active messages in its peek window — nothing to reach.";
            }
            else if (req.TargetSequenceNumber > maxSeen)
            {
                // Target beyond the window is allowed — the park loop itself is sequence-bound and
                // stops at the cap or first overshoot — but the prefix count is unknowable.
                targetBeyondWindow = true;
            }
            else
            {
                // Target inside the peeked seq range but absent from peek = invisible to FIFO
                // receive — almost certainly deferred. Deferred messages can't be reached by this
                // operation.
                windowRefusal = $"Sequence {req.TargetSequenceNumber} sits inside the peeked range but isn't peekable — it's almost certainly deferred. Reach-message can't act on deferred messages (they're invisible to ordered receive); settle it with a deferred-receive flow first.";
            }
        }

        if (windowRefusal is not null)
        {
            return Results.Ok(ReachMessagePreview.Refused(windowRefusal, maxParked));
        }

        return Results.Ok(new ReachMessagePreview
        {
            CanStart = true,
            RefusalReason = null,
            PrefixCount = prefixCount,
            TargetBeyondWindow = targetBeyondWindow,
            MaxParked = maxParked,
            Consequences = BuildReachConsequences(req, prefixCount, entityPath),
            Warnings = BuildReachWarnings(targetBeyondWindow, maxParked),
        });
    }

    /// <summary>
    /// Start: same refusals as preview (the client could skip preview), then hand the op to the
    /// service — the journal write happens before the first park batch.
    /// </summary>
    internal static async Task<IResult> ReachMessageStartAsync(
        string nsId,
        string entityPath,
        ReachMessageRequest req,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        SbOperationService ops,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");
        if (req.TargetSequenceNumber <= 0)
        {
            return ApiErrors.BadRequest("targetSequenceNumber must be a positive sequence number.");
        }

        var client = pool.GetOrCreate(ns);
        var refusal = await EntityRefusalAsync(client, entityPath, ct);
        if (refusal is not null)
        {
            return ApiErrors.Status(StatusCodes.Status409Conflict, refusal);
        }

        var started = await ops.TryStartReachAsync(
            ns.Id, entityPath, req.TargetSequenceNumber, req.Action,
            req.RestoreBeforeTarget, ClampMaxParked(req.MaxParked), client);
        if (started is null)
        {
            return ApiErrors.Status(StatusCodes.Status409Conflict,
                "A reach-message operation is already running on this entity — wait for it or cancel it first.");
        }

        return Results.Ok(started);
    }

    /// <summary>Poll — the op snapshot or 404.</summary>
    internal static async Task<IResult> GetOperationAsync(
        string nsId,
        string operationId,
        ProfileRepository profile,
        DemoModeService demo,
        SbOperationService ops,
        CancellationToken ct)
    {
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");
        if (!Guid.TryParse(operationId, out var opId)) return ApiErrors.BadRequest("Invalid operation id");

        var status = await ops.GetAsync(opId);
        return status is null || status.NamespaceId != ns.Id
            ? ApiErrors.NotFound("Operation not found")
            : Results.Ok(status);
    }

    /// <summary>Cancel — observed between receive batches; in-flight locks expire harmlessly.</summary>
    internal static async Task<IResult> CancelOperationAsync(
        string nsId,
        string operationId,
        ProfileRepository profile,
        DemoModeService demo,
        SbOperationService ops,
        CancellationToken ct)
    {
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");
        if (!Guid.TryParse(operationId, out var opId)) return ApiErrors.BadRequest("Invalid operation id");

        var status = await ops.CancelAsync(opId);
        if (status is null || status.NamespaceId != ns.Id)
        {
            return ApiErrors.NotFound("Operation not found or not running");
        }
        return Results.Ok(status);
    }

    /// <summary>
    /// Resume = re-run the restore phase against the entity's DLQ stamp set. Valid for interrupted,
    /// failed and cancelled ops — the only states with parked messages that may still need it.
    /// </summary>
    internal static async Task<IResult> ResumeOperationAsync(
        string nsId,
        string operationId,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        SbOperationService ops,
        CancellationToken ct)
    {
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");
        if (!Guid.TryParse(operationId, out var opId)) return ApiErrors.BadRequest("Invalid operation id");

        var existing = await ops.GetAsync(opId);
        if (existing is null || existing.NamespaceId != ns.Id)
        {
            return ApiErrors.NotFound("Operation not found");
        }

        var client = pool.GetOrCreate(ns);
        var resumed = await ops.ResumeAsync(opId, client);
        return resumed is null
            ? ApiErrors.Status(StatusCodes.Status409Conflict, $"Operation is {existing.State} — only interrupted, failed or cancelled operations can resume.")
            : Results.Ok(resumed);
    }

    /// <summary>"Leave in DLQ" — terminal; the parked copies keep their stamps as a record.</summary>
    internal static async Task<IResult> DismissOperationAsync(
        string nsId,
        string operationId,
        ProfileRepository profile,
        DemoModeService demo,
        SbOperationService ops,
        CancellationToken ct)
    {
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");
        if (!Guid.TryParse(operationId, out var opId)) return ApiErrors.BadRequest("Invalid operation id");

        var existing = await ops.GetAsync(opId);
        if (existing is not null && existing.NamespaceId != ns.Id)
        {
            return ApiErrors.NotFound("Operation not found");
        }

        var dismissed = await ops.DismissAsync(opId);
        return dismissed is null
            ? ApiErrors.Status(StatusCodes.Status409Conflict, "Only interrupted, failed or cancelled operations can be dismissed.")
            : Results.Ok(dismissed);
    }

    /// <summary>
    /// Per-entity op listing — powers the "interrupted operation" banner. Non-terminal ops get a
    /// live DLQ stamp scan so the banner can say "N messages parked" from the broker's truth, not
    /// just the journal's last write.
    /// </summary>
    internal static async Task<IResult> ListOperationsAsync(
        string nsId,
        string? entity,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        SbOperationService ops,
        CancellationToken ct)
    {
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");

        var ops_ = await ops.ListAsync(ns.Id, entity);
        var client = pool.GetOrCreate(ns);
        foreach (var op in ops_)
        {
            if (op.State is SbOperationState.Interrupted or SbOperationState.Failed or SbOperationState.Cancelled)
            {
                try
                {
                    var scan = await client.ScanParkedAsync(op.EntityPath, op.Id.ToString("N"), ct);
                    op.ParkedInDlq = scan.ParkedCount;
                }
                catch (NotSupportedException)
                {
                    // Client can't scan — fall back to the journal count.
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Scan failure is enrichment loss, not a listing failure.
                }
            }
        }

        return Results.Ok(ops_);
    }

    /// <summary>
    /// DLQ triage past the peek window: resubmit every DLQ message matching reason (+ optional
    /// description), capped by <c>limit</c>. Same session-entity refusal as reach-message — it rides
    /// the same plain receive/settle path.
    /// </summary>
    internal static async Task<IResult> ResubmitDeadLetterByFilterAsync(
        string nsId,
        string entityPath,
        DlqRequeueByFilterRequest req,
        ProfileRepository profile,
        IServiceBusConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        entityPath = DecodeEntityPath(entityPath);
        var ns = ResolveNamespace(nsId, profile, demo);
        if (ns is null) return ApiErrors.NotFound("Namespace not found");
        if (string.IsNullOrWhiteSpace(req.Reason))
        {
            return ApiErrors.BadRequest("reason is required — a group is identified by its dead-letter reason.");
        }

        var client = pool.GetOrCreate(ns);
        var entity = await FindEntityInfoAsync(client, entityPath, ct);
        if (entity?.RequiresSession == true)
        {
            return ApiErrors.Status(StatusCodes.Status409Conflict,
                $"'{entityPath}' requires sessions — settle actions need session receivers, which aren't supported yet.");
        }

        var resubmitted = await client.ResubmitDeadLetterByFilterAsync(
            entityPath, req.Reason, req.Description,
            Math.Clamp(req.Limit, 1, RequeueByFilterLimitCap), ct);
        return Results.Ok(new { resubmitted });
    }

    private static string DecodeEntityPath(string entityPath) => Uri.UnescapeDataString(entityPath);

    private static ServiceBusNamespace? ResolveNamespace(
        string nsId,
        ProfileRepository profile,
        DemoModeService demo)
    {
        if (!Guid.TryParse(nsId, out var id))
            return null;

        // Demo namespace ids are reserved — a save made while demo mode was on can persist the
        // overlay into the profile, and that copy must never resolve to a real client.
        if (id == DemoModeService.DemoNamespaceId1 || id == DemoModeService.DemoNamespaceId2)
            return demo.IsDemoMode
                ? demo.GetDemoNamespaces().FirstOrDefault(n => n.Id == id)
                : null;

        var ns = profile.FindServiceBusNamespace(id);
        if (ns is not null)
            return ns;

        return demo.IsDemoMode
            ? demo.GetDemoNamespaces().FirstOrDefault(n => n.Id == id)
            : null;
    }

    public sealed class ResubmitRequest
    {
        public string[] SequenceNumbers { get; set; } = [];
        public string? TargetEntityPath { get; set; }
        public RemapRules? RemapRules { get; set; }
    }

    public sealed class ResubmitEditedRequest
    {
        /// <summary>Sequence number of the dead-lettered original to settle after the edit is sent.</summary>
        public long SequenceNumber { get; set; }
        public SbMessage Message { get; set; } = null!;
        /// <summary>Optional destination override; defaults to the entity the DLQ belongs to.</summary>
        public string? TargetEntityPath { get; set; }
    }

    public sealed class PurgeRequest
    {
        /// <summary>When true every message in the entity's dead-letter sub-queue is removed.</summary>
        public bool DeadLetter { get; set; }
    }

    public sealed class ResendRequest
    {
        public string[] SequenceNumbers { get; set; } = [];
        /// <summary>When true the messages are read from the entity's dead-letter sub-queue.</summary>
        public bool DeadLetter { get; set; }
    }

    public sealed class ScheduleRequest
    {
        public SbMessage Message { get; set; } = null!;
        public DateTimeOffset ScheduledEnqueueTime { get; set; }
    }

    /// <summary>Body for reach-message preview and start.</summary>
    public sealed class ReachMessageRequest
    {
        /// <summary>The message to reach, by broker sequence number.</summary>
        public long TargetSequenceNumber { get; set; }
        /// <summary>What to do when the target is reached — complete / dead-letter / resubmit.</summary>
        public SbReachTargetAction Action { get; set; }
        /// <summary>
        /// Resubmit only: when true the target's copy lands at the tail AFTER the restored prefix
        /// copies (closest to original relative order); when false it lands first.
        /// </summary>
        public bool RestoreBeforeTarget { get; set; } = true;
        /// <summary>Park cap override — defaults to 1,000; hard ceiling 5,000 (clamped server-side).</summary>
        public int? MaxParked { get; set; }
    }

    /// <summary>
    /// The preview contract the wizard renders: consequences are written literally — "park into the
    /// DLQ, restore as new tail-appended copies" — never "put back like nothing happened".
    /// </summary>
    public sealed class ReachMessagePreview
    {
        public bool CanStart { get; set; }
        /// <summary>Why the op is refused when <see cref="CanStart"/> is false.</summary>
        public string? RefusalReason { get; set; }
        /// <summary>Exact count of messages ahead of the target — null when the target is beyond the peek window.</summary>
        public long? PrefixCount { get; set; }
        /// <summary>True when the target wasn't in the peek window but sits past its max — prefix count unknowable.</summary>
        public bool TargetBeyondWindow { get; set; }
        /// <summary>The park cap that applies to this op.</summary>
        public int MaxParked { get; set; }
        /// <summary>What the op will do — render verbatim.</summary>
        public List<string> Consequences { get; set; } = [];
        /// <summary>Honest failure modes — render verbatim.</summary>
        public List<string> Warnings { get; set; } = [];

        public static ReachMessagePreview Refused(string reason, int maxParked) => new()
        {
            CanStart = false,
            RefusalReason = reason,
            MaxParked = maxParked,
        };
    }

    /// <summary>Body for <c>dlq/requeue-by-filter</c> — resubmit a whole reason/description group server-side.</summary>
    public sealed class DlqRequeueByFilterRequest
    {
        /// <summary>The <c>DeadLetterReason</c> to match — required (it identifies the triage group).</summary>
        public string Reason { get; set; } = "";
        /// <summary>Optional <c>DeadLetterErrorDescription</c> to narrow the group; null matches every description under the reason.</summary>
        public string? Description { get; set; }
        /// <summary>Max messages to resubmit — clamped to 5,000.</summary>
        public int Limit { get; set; } = 500;
    }
}
