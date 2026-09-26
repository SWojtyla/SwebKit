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

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/resend", ResendMessagesAsync);

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
}
