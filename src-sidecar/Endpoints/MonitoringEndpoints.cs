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

        group.MapGet("/rules", GetRulesAsync);

        group.MapGet("/rules/{id}", GetRuleByIdAsync);

        group.MapPost("/rules", CreateRuleAsync);

        group.MapPut("/rules/{id}", UpdateRuleAsync);

        group.MapDelete("/rules/{id}", DeleteRuleAsync);

        // ── History snapshot ────────────────────────────────────────────────

        group.MapGet("/history", GetHistory);

        // ── Live SSE stream of fired alerts ─────────────────────────────────

        group.MapGet("/stream", async (HttpContext context, MonitoringAlertEvaluationService engine, ProactiveInsightService insights) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.ContentType = "text/event-stream; charset=utf-8";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

            await context.Response.WriteAsync(": connected\n\n", cts.Token);

            // Wrapped in a {kind, event} envelope (workspace-intelligence Module 4) so this one
            // stream can carry both the pre-existing AlertFiredEvent and the new
            // ProactiveInsightReadyEvent — see useMonitoringStream on the frontend for the matching
            // parsing side.
            void WriteEvent(string kind, object evt)
            {
                try
                {
                    var json = JsonSerializer.Serialize(new { kind, @event = evt }, JsonOptions);
                    context.Response.WriteAsync($"data: {json}\n\n", cts.Token).GetAwaiter().GetResult();
                    context.Response.Body.FlushAsync(cts.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { /* client gone */ }
                catch (Exception) { /* swallow — stream resilience */ }
            }

            void OnAlertFired(AlertFiredEvent evt) => WriteEvent("alertFired", evt);
            void OnInsightReady(ProactiveInsightReadyEvent evt) => WriteEvent("proactiveInsightReady", evt);

            engine.AlertFired += OnAlertFired;
            insights.InsightReady += OnInsightReady;
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
                insights.InsightReady -= OnInsightReady;
            }
        });
    }

    internal static async Task<Ok<IReadOnlyList<MonitoringAlertRule>>> GetRulesAsync(IAlertRuleRepository repo) =>
        TypedResults.Ok(await repo.GetAllAsync());

    internal static async Task<Results<Ok<MonitoringAlertRule>, NotFound>> GetRuleByIdAsync(string id, IAlertRuleRepository repo)
    {
        var rule = await repo.GetByIdAsync(id);
        return rule is null ? TypedResults.NotFound() : TypedResults.Ok(rule);
    }

    internal static async Task<Created<MonitoringAlertRule>> CreateRuleAsync(
        MonitoringAlertRule rule,
        IAlertRuleRepository repo,
        MonitoringAlertEvaluationService engine)
    {
        if (string.IsNullOrWhiteSpace(rule.Id))
            rule.Id = Guid.NewGuid().ToString("N");
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();
        return TypedResults.Created($"/api/monitoring/rules/{rule.Id}", rule);
    }

    internal static async Task<Ok<MonitoringAlertRule>> UpdateRuleAsync(
        string id,
        MonitoringAlertRule rule,
        IAlertRuleRepository repo,
        MonitoringAlertEvaluationService engine)
    {
        rule.Id = id;
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();
        return TypedResults.Ok(rule);
    }

    internal static async Task<NoContent> DeleteRuleAsync(
        string id,
        IAlertRuleRepository repo,
        MonitoringAlertEvaluationService engine)
    {
        await repo.DeleteAsync(id);
        await engine.ReloadRulesAsync();
        return TypedResults.NoContent();
    }

    internal static Ok<IReadOnlyList<AlertFiredEvent>> GetHistory(MonitoringAlertEvaluationService engine) =>
        TypedResults.Ok(engine.RecentAlerts);
}
