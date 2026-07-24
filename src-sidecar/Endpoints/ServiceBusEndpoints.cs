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
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            try
            {
                var client = CreateClient(ns, factory);
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
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            var info = await client.GetNamespaceInfoAsync();
            return Results.Ok(info);
        });

        app.MapGet("/api/servicebus/{nsId}/queues", async (
            string nsId,
            ProfileRepository profile,
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            var queues = await client.ListQueuesAsync();
            return Results.Ok(queues);
        });

        app.MapGet("/api/servicebus/{nsId}/topics", async (
            string nsId,
            ProfileRepository profile,
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            var topics = await client.ListTopicsAsync();
            return Results.Ok(topics);
        });

        app.MapGet("/api/servicebus/{nsId}/topics/{topic}/subscriptions", async (
            string nsId,
            string topic,
            ProfileRepository profile,
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            var subs = await client.ListSubscriptionsAsync(topic);
            return Results.Ok(subs);
        });

        app.MapGet("/api/servicebus/{nsId}/entities/{entityPath}/stats", async (
            string nsId,
            string entityPath,
            ProfileRepository profile,
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            var stats = await client.GetEntityStatsAsync(entityPath);
            return Results.Ok(stats);
        });

        app.MapGet("/api/servicebus/{nsId}/entities/{entityPath}/peek", async (
            string nsId,
            string entityPath,
            int count,
            long? fromSeq,
            ProfileRepository profile,
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            var messages = await client.PeekMessagesAsync(entityPath, count, fromSequenceNumber: fromSeq);
            return Results.Ok(messages);
        });

        app.MapGet("/api/servicebus/{nsId}/entities/{entityPath}/dlq", async (
            string nsId,
            string entityPath,
            int count,
            long? fromSeq,
            ProfileRepository profile,
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            var messages = await client.PeekDeadLetterAsync(entityPath, count, fromSequenceNumber: fromSeq);
            return Results.Ok(messages);
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/send", async (
            string nsId,
            string entityPath,
            SbMessage message,
            ProfileRepository profile,
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            await client.SendMessageAsync(entityPath, message);
            return Results.Ok();
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/complete", async (
            string nsId,
            string entityPath,
            long[] sequenceNumbers,
            ProfileRepository profile,
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            var count = await client.CompleteMessagesAsync(entityPath, sequenceNumbers);
            return Results.Ok(new { completed = count });
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/purge", async (
            string nsId,
            string entityPath,
            bool deadLetter,
            ProfileRepository profile,
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            var count = await client.PurgeMessagesAsync(entityPath, deadLetter);
            return Results.Ok(new { purged = count });
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/dlq/complete", async (
            string nsId,
            string entityPath,
            string[] sequenceNumbers,
            ProfileRepository profile,
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            await client.CompleteDeadLetterAsync(entityPath, sequenceNumbers);
            return Results.Ok();
        });

        app.MapPost("/api/servicebus/{nsId}/entities/{entityPath}/resubmit", async (
            string nsId,
            string entityPath,
            ResubmitRequest req,
            ProfileRepository profile,
            IServiceBusClientFactory factory) =>
        {
            var ns = profile.FindServiceBusNamespace(Guid.Parse(nsId));
            if (ns is null) return Results.NotFound("Namespace not found");

            var client = CreateClient(ns, factory);
            await client.ResubmitDeadLetterAsync(entityPath, req.SequenceNumbers, req.TargetEntityPath, req.RemapRules);
            return Results.Ok();
        });
    }

    private static IServiceBusClient CreateClient(ServiceBusNamespace ns, IServiceBusClientFactory factory)
    {
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
}
