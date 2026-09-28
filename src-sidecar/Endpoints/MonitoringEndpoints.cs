using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class MonitoringEndpoints
{
    /// <summary>Defaults/bounds for <c>/history/summary?windowHours=</c> — 24h default,
    /// 7-day ceiling (168 hourly buckets stays a small payload and a readable chart).</summary>
    internal const int DefaultSummaryWindowHours = 24;
    internal const int MaxSummaryWindowHours = 168;

    public static void MapMonitoringEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/monitoring");

        // ── Rules CRUD ──────────────────────────────────────────────────────

        group.MapGet("/rules", GetRulesAsync);

        group.MapGet("/rules/{id}", GetRuleByIdAsync);

        group.MapPost("/rules", CreateRuleAsync);

        group.MapPut("/rules/{id}", UpdateRuleAsync);

        group.MapDelete("/rules/{id}", DeleteRuleAsync);

        // ── Silences + per-rule mute (monitoring-closed-loop item 3) ────────

        group.MapGet("/silences", GetSilencesAsync);

        group.MapPost("/silences", CreateSilenceAsync);

        group.MapDelete("/silences/{id}", DeleteSilenceAsync);

        group.MapPost("/rules/{id}/mute", MuteRuleAsync);

        // ── History snapshot + ops summary ──────────────────────────────────

        group.MapGet("/history", GetHistory);

        group.MapGet("/history/summary", GetHistorySummaryAsync);

        // ── Persisted AI insight reports (ai-insight-reports) ───────────────

        group.MapGet("/insights", GetInsightsAsync);

        group.MapDelete("/insights/{id}", DeleteInsightAsync);

        group.MapPost("/insights/{id}/open-chat", OpenInsightChatAsync);

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
        void OnAlertResolved(AlertResolvedEvent evt) => stream.Enqueue("alertResolved", evt);
        void OnInsightReady(ProactiveInsightReadyEvent evt) => stream.Enqueue("proactiveInsightReady", evt);
        void OnInsightStatus(ProactiveInsightStatusEvent evt) => stream.Enqueue("proactiveInsightStatus", evt);
        void OnPendingActionProposed(PendingActionProposedEvent evt) => stream.Enqueue("pendingActionProposed", evt);
        void OnEvaluationCompleted(AlertEvaluatedEvent evt) => stream.Enqueue("evaluationCompleted", evt);

        engine.AlertFired += OnAlertFired;
        engine.AlertResolved += OnAlertResolved;
        engine.EvaluationCompleted += OnEvaluationCompleted;
        insights.InsightReady += OnInsightReady;
        insights.InsightStatus += OnInsightStatus;
        insights.PendingActionProposed += OnPendingActionProposed;
        try
        {
            await stream.RunAsync(context, context.RequestAborted);
        }
        finally
        {
            engine.AlertFired -= OnAlertFired;
            engine.AlertResolved -= OnAlertResolved;
            engine.EvaluationCompleted -= OnEvaluationCompleted;
            insights.InsightReady -= OnInsightReady;
            insights.InsightStatus -= OnInsightStatus;
            insights.PendingActionProposed -= OnPendingActionProposed;
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

    /// <summary>Durable alert history (monitoring-closed-loop 4a): the persisted
    /// Fired/Suppressed/Resolved record, merged with the engine's in-memory ring buffer so a
    /// firing that failed to persist still surfaces until the process exits. Newest first.</summary>
    internal static async Task<Ok<IReadOnlyList<AlertHistoryEntry>>> GetHistory(
        MonitoringAlertEvaluationService engine,
        IAlertHistoryRepository history)
    {
        var persisted = await history.GetAllAsync();
        var seen = new HashSet<string>(persisted.Select(Key));

        var merged = persisted
            .Concat(engine.RecentAlerts
                .Select(ToEntry)
                .Where(e => seen.Add(Key(e))))
            .OrderByDescending(e => e.At)
            .ToList();
        return TypedResults.Ok((IReadOnlyList<AlertHistoryEntry>)merged);

        static string Key(AlertHistoryEntry e) => $"{e.RuleId}|{e.At.UtcTicks}|{e.Kind}";

        static AlertHistoryEntry ToEntry(AlertFiredEvent a) => new()
        {
            RuleId = a.RuleId,
            RuleName = a.RuleName,
            Source = a.Source,
            Severity = a.Severity,
            Kind = a.Suppressed ? AlertHistoryKind.Suppressed : AlertHistoryKind.Fired,
            At = a.FiredAt,
            Message = a.SuppressedBy is { } by ? $"{a.Message} (silenced: {by})" : a.Message,
        };
    }

    /// <summary>Aggregated ops summary (monitoring-closed-loop item 4) over the durable store:
    /// firings-per-hour buckets, severity distribution, open incidents, and MTTR over
    /// fired→resolved pairs — with honest gaps (unpairable resolves reported as orphans,
    /// detection latency labelled as the eval-interval bound, never measured). Reads the
    /// persisted record only; the volatile ring buffer is deliberately excluded so the
    /// dashboard means "the durable record says".</summary>
    internal static async Task<Ok<AlertHistorySummary>> GetHistorySummaryAsync(
        int? windowHours,
        IAlertHistoryRepository history,
        IAlertRuleRepository rules)
    {
        var hours = Math.Clamp(
            windowHours ?? DefaultSummaryWindowHours, 1, MaxSummaryWindowHours);
        var entries = await history.GetAllAsync();
        var intervals = (await rules.GetAllAsync())
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().IntervalSeconds, StringComparer.Ordinal);
        return TypedResults.Ok(AlertHistorySummaryBuilder.Build(
            entries, hours, DateTimeOffset.UtcNow, intervals));
    }

    // ── Silences + per-rule mute (monitoring-closed-loop item 3) ──────────────

    internal static async Task<Ok<IReadOnlyList<MonitoringSilence>>> GetSilencesAsync(
        IMonitoringSilenceRepository repo) =>
        TypedResults.Ok(await repo.GetAllAsync());

    internal static async Task<Results<Created<MonitoringSilence>, BadRequest<string>>> CreateSilenceAsync(
        MonitoringSilence silence,
        IMonitoringSilenceRepository repo)
    {
        if (silence.EndUtc <= silence.StartUtc)
            return TypedResults.BadRequest("endUtc must be after startUtc");
        if (string.IsNullOrWhiteSpace(silence.Id))
            silence.Id = Guid.NewGuid().ToString("N");
        await repo.UpsertAsync(silence);
        return TypedResults.Created($"/api/monitoring/silences/{silence.Id}", silence);
    }

    internal static async Task<NoContent> DeleteSilenceAsync(
        string id,
        IMonitoringSilenceRepository repo)
    {
        await repo.DeleteAsync(id);
        return TypedResults.NoContent();
    }

    /// <summary>Sets (or clears, when <c>until</c> is null) a rule's per-rule mute. Goes through
    /// the normal rule upsert + engine reload — the muted rule keeps evaluating, so its status
    /// dot stays honest while its firings come out flagged <c>suppressed</c>.</summary>
    internal static async Task<Results<Ok<MonitoringAlertRule>, NotFound>> MuteRuleAsync(
        string id,
        MuteRuleRequest request,
        IAlertRuleRepository repo,
        MonitoringAlertEvaluationService engine)
    {
        var rule = await repo.GetByIdAsync(id);
        if (rule is null)
            return TypedResults.NotFound();

        // A timestamp in the past is a no-op mute — normalize to "not muted" so the stored
        // value always means what it says.
        rule.MutedUntil = request.Until is { } until && until > DateTimeOffset.UtcNow ? until : null;
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();
        return TypedResults.Ok(rule);
    }

    internal static async Task<Ok<IReadOnlyList<ProactiveInsightReport>>> GetInsightsAsync(
        IProactiveInsightReportRepository repo) =>
        TypedResults.Ok(await repo.GetAllAsync());

    internal static async Task<NoContent> DeleteInsightAsync(
        string id,
        IProactiveInsightReportRepository repo)
    {
        await repo.DeleteAsync(id);
        return TypedResults.NoContent();
    }

    /// <summary>Materializes the report's chat session (re-seeding it from the persisted report
    /// when the in-memory store already evicted it) and returns the session id plus its
    /// transcript — everything the "Discuss in chat" panel needs in one round trip.</summary>
    internal static async Task<Results<Ok<InsightChatSession>, NotFound>> OpenInsightChatAsync(
        string id,
        IProactiveInsightReportRepository repo,
        ProactiveInsightService insights)
    {
        var report = await repo.GetByIdAsync(id);
        if (report is null)
            return TypedResults.NotFound();

        var messages = insights.EnsureSession(report);
        return TypedResults.Ok(new InsightChatSession
        {
            SessionId = report.SessionId,
            Messages = messages.Select(m => new InsightChatMessage { Role = m.Role, Content = m.Content }).ToList(),
        });
    }
}

/// <summary>Body of <c>POST /api/monitoring/rules/{id}/mute</c> — <c>until</c> is an ISO
/// timestamp; null (or a past timestamp, normalized server-side) unmutes the rule.</summary>
public sealed record MuteRuleRequest(DateTimeOffset? Until);

/// <summary>Response of <c>POST /api/monitoring/insights/{id}/open-chat</c>.</summary>
public sealed class InsightChatSession
{
    public required string SessionId { get; init; }
    public required IReadOnlyList<InsightChatMessage> Messages { get; init; }
}

public sealed class InsightChatMessage
{
    public required string Role { get; init; }
    public string? Content { get; init; }
}
