using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class MonitoringEndpoints
{
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

        group.MapGet("/stream", (
            HttpContext context,
            MonitoringAlertEvaluationService engine,
            ProactiveInsightService insights,
            ILogger<MonitoringEventStream> logger) => StreamAsync(context, engine, insights, logger));
    }

    /// <summary>
    /// Request handler for <c>GET /api/monitoring/stream</c>. Subscribes to the alert engine and the
    /// proactive-insight service, then pumps everything they raise to the client over SSE.
    /// </summary>
    /// <remarks>
    /// The event handlers only enqueue — all socket I/O happens on this request's own loop. That is
    /// the whole point: the handlers run on the alert-evaluation background thread, so a previous
    /// implementation that blocked on <c>WriteAsync(...).GetAwaiter().GetResult()</c> let one stalled
    /// SSE client freeze rule evaluation for every rule, silently.
    /// </remarks>
    internal static async Task StreamAsync(
        HttpContext context,
        MonitoringAlertEvaluationService engine,
        ProactiveInsightService insights,
        ILogger? logger = null)
    {
        var stream = new MonitoringEventStream(logger);

        void OnAlertFired(AlertFiredEvent evt) => stream.Enqueue("alertFired", evt);
        void OnInsightReady(ProactiveInsightReadyEvent evt) => stream.Enqueue("proactiveInsightReady", evt);
        void OnEvaluationCompleted(AlertEvaluatedEvent evt) => stream.Enqueue("evaluationCompleted", evt);

        engine.AlertFired += OnAlertFired;
        engine.EvaluationCompleted += OnEvaluationCompleted;
        insights.InsightReady += OnInsightReady;
        try
        {
            await stream.RunAsync(context, context.RequestAborted);
        }
        finally
        {
            engine.AlertFired -= OnAlertFired;
            engine.EvaluationCompleted -= OnEvaluationCompleted;
            insights.InsightReady -= OnInsightReady;
            stream.Complete();
        }
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
