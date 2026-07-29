using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class MonitoringEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static void MapMonitoringEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/monitoring");

        // ── Rules CRUD ──────────────────────────────────────────────────────

        group.MapGet("/rules", async (IAlertRuleRepository repo) =>
            Results.Ok(await repo.GetAllAsync()));

        group.MapGet("/rules/{id}", async (string id, IAlertRuleRepository repo) =>
        {
            var rule = await repo.GetByIdAsync(id);
            return rule is null ? Results.NotFound() : Results.Ok(rule);
        });

        group.MapPost("/rules", async (
            MonitoringAlertRule rule,
            IAlertRuleRepository repo,
            MonitoringAlertEvaluationService engine) =>
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
                rule.Id = Guid.NewGuid().ToString("N");
            await repo.UpsertAsync(rule);
            await engine.ReloadRulesAsync();
            return Results.Created($"/api/monitoring/rules/{rule.Id}", rule);
        });

        group.MapPut("/rules/{id}", async (
            string id,
            MonitoringAlertRule rule,
            IAlertRuleRepository repo,
            MonitoringAlertEvaluationService engine) =>
        {
            rule.Id = id;
            await repo.UpsertAsync(rule);
            await engine.ReloadRulesAsync();
            return Results.Ok(rule);
        });

        group.MapDelete("/rules/{id}", async (
            string id,
            IAlertRuleRepository repo,
            MonitoringAlertEvaluationService engine) =>
        {
            await repo.DeleteAsync(id);
            await engine.ReloadRulesAsync();
            return Results.NoContent();
        });

        // ── History snapshot ────────────────────────────────────────────────

        group.MapGet("/history", (MonitoringAlertEvaluationService engine) =>
            Results.Ok(engine.RecentAlerts));

        // ── Live SSE stream of fired alerts ─────────────────────────────────

        group.MapGet("/stream", async (HttpContext context, MonitoringAlertEvaluationService engine) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.ContentType = "text/event-stream; charset=utf-8";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

            await context.Response.WriteAsync(": connected\n\n", cts.Token);

            void OnAlertFired(AlertFiredEvent evt)
            {
                try
                {
                    var json = JsonSerializer.Serialize(evt, JsonOptions);
                    context.Response.WriteAsync($"data: {json}\n\n", cts.Token).GetAwaiter().GetResult();
                    context.Response.Body.FlushAsync(cts.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { /* client gone */ }
                catch (Exception) { /* swallow — stream resilience */ }
            }

            engine.AlertFired += OnAlertFired;
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
                while (!cts.Token.IsCancellationRequested)
                {
                    try { await timer.WaitForNextTickAsync(cts.Token); }
                    catch (OperationCanceledException) { break; }
                    await context.Response.WriteAsync(": heartbeat\n\n", cts.Token);
                    await context.Response.Body.FlushAsync(cts.Token);
                }
            }
            finally
            {
                engine.AlertFired -= OnAlertFired;
            }
        });
    }
}
