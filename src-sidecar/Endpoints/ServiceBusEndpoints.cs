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
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            try
            {
                var client = CreateClient(ns, factory, demo);
                var ok = await client.TestConnectionAsync();
                return Results.Ok(new { connected = ok });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { connected = false, error = ex.Message });
            }
        });

        app.MapGet("/api/servicebus/{nsId}/info", async (
            string nsId,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            var info = await client.GetNamespaceInfoAsync();
            return Results.Ok(info);
        });

        app.MapGet("/api/servicebus/{nsId}/queues", async (
            string nsId,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            var queues = await client.ListQueuesAsync();
            return Results.Ok(queues);
        });

        app.MapGet("/api/servicebus/{nsId}/topics", async (
            string nsId,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            var topics = await client.ListTopicsAsync();
            return Results.Ok(topics);
        });

        app.MapGet("/api/servicebus/{nsId}/topics/{topic}/subscriptions", async (
            string nsId,
            string topic,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            var subs = await client.ListSubscriptionsAsync(topic);
            return Results.Ok(subs);
        });

        app.MapGet("/api/servicebus/{nsId}/entities/{entityPath}/stats", async (
            string nsId,
            string entityPath,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            var stats = await client.GetEntityStatsAsync(entityPath);
            return Results.Ok(stats);
        });

        app.MapGet("/api/servicebus/{nsId}/entities/{entityPath}/peek", async (
            string nsId,
            string entityPath,
            int count,
            long? fromSeq,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            var messages = await client.PeekMessagesAsync(entityPath, count, fromSequenceNumber: fromSeq);
            return Results.Ok(messages);
        });

        app.MapGet("/api/servicebus/{nsId}/entities/{entityPath}/dlq", async (
            string nsId,
            string entityPath,
            int count,
            long? fromSeq,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            var messages = await client.PeekDeadLetterAsync(entityPath, count, fromSequenceNumber: fromSeq);
            return Results.Ok(messages);
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/send", async (
            string nsId,
            string entityPath,
            SbMessage message,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            await client.SendMessageAsync(entityPath, message);
            return Results.Ok();
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/batch-send", async (
            string nsId,
            string entityPath,
            List<SbMessage> messages,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            await client.SendBatchAsync(entityPath, messages);
            return Results.Ok(new { sent = messages.Count });
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/schedule", async (
            string nsId,
            string entityPath,
            ScheduleRequest req,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo,
            ScheduledMessageRepository schedRepo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            var seq = await client.ScheduleMessageAsync(entityPath, req.Message, req.ScheduledEnqueueTime);

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
                return Results.BadRequest("Invalid namespace ID");

            var entries = schedRepo.GetByEntity(id, entityPath);
            return Results.Ok(entries);
        });

        app.MapDelete("/api/servicebus/{nsId}/entities/{entityPath}/scheduled/{sequenceNumber}", async (
            string nsId,
            string entityPath,
            long sequenceNumber,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo,
            ScheduledMessageRepository schedRepo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            await client.CancelScheduledMessageAsync(entityPath, sequenceNumber);

            var entries = schedRepo.GetByEntity(ns.Id, entityPath);
            var entry = entries.FirstOrDefault(e => e.SequenceNumber == sequenceNumber);
            if (entry is not null)
                await schedRepo.RemoveAsync(entry.Id);

            return Results.Ok();
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/complete", async (
            string nsId,
            string entityPath,
            long[] sequenceNumbers,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            var count = await client.CompleteMessagesAsync(entityPath, sequenceNumbers);
            return Results.Ok(new { completed = count });
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/purge", async (
            string nsId,
            string entityPath,
            bool deadLetter,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            var count = await client.PurgeMessagesAsync(entityPath, deadLetter);
            return Results.Ok(new { purged = count });
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/dlq/complete", async (
            string nsId,
            string entityPath,
            string[] sequenceNumbers,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            await client.CompleteDeadLetterAsync(entityPath, sequenceNumbers);
            return Results.Ok();
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/resubmit", async (
            string nsId,
            string entityPath,
            ResubmitRequest req,
            ProfileRepository profile,
            IServiceBusClientFactory factory,
            DemoModeService demo) =>
        {
            entityPath = DecodeEntityPath(entityPath);
            var ns = ResolveNamespace(nsId, profile, demo);
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory, demo);
            await client.ResubmitDeadLetterAsync(entityPath, req.SequenceNumbers, req.TargetEntityPath, req.RemapRules);
            return Results.Ok();
        });

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
                return Results.BadRequest("Invalid template ID");

            profile.DeleteMessageTemplate(guid);
            return Results.Ok();
        });
    }

    private static string DecodeEntityPath(string entityPath) => Uri.UnescapeDataString(entityPath);

    private static ServiceBusNamespace? ResolveNamespace(
        string nsId,
        ProfileRepository profile,
        DemoModeService demo)
    {
        if (!Guid.TryParse(nsId, out var id))
            return null;

        var ns = profile.FindServiceBusNamespace(id);
        if (ns is not null)
            return ns;

        return demo.IsDemoMode
            ? demo.GetDemoNamespaces().FirstOrDefault(n => n.Id == id)
            : null;
    }

    private static IServiceBusClient CreateClient(
        ServiceBusNamespace ns,
        IServiceBusClientFactory factory,
        DemoModeService demo)
    {
        if (demo.IsDemoMode)
            return demo.GetSbClient(ns);

        return ns.AuthMode == SbAuthMode.ConnectionString
            ? factory.Create(ns.CredentialKey, ns.TransportType)
            : factory.CreateWithEntra(ns.FullyQualifiedNamespace, ns.TransportType);
    }

    public sealed class ResubmitRequest
    {
        public string[] SequenceNumbers { get; set; } = [];
        public string? TargetEntityPath { get; set; }
        public RemapRules? RemapRules { get; set; }
    }

    public sealed class ScheduleRequest
    {
        public SbMessage Message { get; set; } = null!;
        public DateTimeOffset ScheduledEnqueueTime { get; set; }
    }
}
