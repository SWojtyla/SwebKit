import { apiFetch, apiSend } from "./transport";

// ── Monitoring ───────────────────────────────────────────────────────────────

export type AlertRuleSource =
    | "AksPodHealth"
    | "AksPodRestartRate"
    | "AksNamespaceHealthScore"
    | "ServiceBusDlqDepth"
    | "ServiceBusActiveDepth"
    | "ServiceBusDeadSubscription"
    | "RedisMemoryUsage"
    | "RedisConnectedClients";

export type AlertSeverity = "Warning" | "Critical";
export type AlertSignalStatus = "Ok" | "Firing" | "Skipped" | "Error";

export interface AksPodAlertParams {
    namespace: string;
    kubeconfigContext?: string;
    restartThreshold?: number;
    healthScoreThreshold?: number;
}

export interface ServiceBusAlertParams {
    namespaceConnectionAlias?: string;
    entityPath?: string;
    messageCountThreshold?: number;
}

export interface RedisAlertParams {
    connectionAlias?: string;
    memoryUsageThresholdPercent?: number;
    clientCountLowerBound?: number;
}

export interface MonitoringAlertRule {
    id: string;
    name: string;
    enabled: boolean;
    source: AlertRuleSource;
    severity: AlertSeverity;
    intervalSeconds: number;
    cooldownMinutes: number;
    aksPodParams?: AksPodAlertParams | null;
    serviceBusParams?: ServiceBusAlertParams | null;
    redisAlertParams?: RedisAlertParams | null;
    /** When true (default), a firing triggers a background AI investigation that posts a
     * proactive insight. Old persisted rules without the field deserialize to true. */
    aiInvestigationEnabled: boolean;
    lastEvaluatedAt?: string | null;
    lastFiredAt?: string | null;
}

export interface AlertFiredEvent {
    ruleId: string;
    ruleName: string;
    source: AlertRuleSource;
    severity: AlertSeverity;
    message: string;
    detail: string;
    firedAt: string;
    profileName: string;
}

/** Emitted after every rule evaluation so the UI can show each rule's real health —
 * including Error/Skipped states that never produce an alertFired event. */
export interface AlertEvaluatedEvent {
    ruleId: string;
    status: AlertSignalStatus;
    evaluatedAt: string;
    /** Failure/skipped reason when status is Error or Skipped. */
    message?: string | null;
}

/** Lifecycle event for a background proactive investigation: `Started` when the agent begins,
 * `Skipped` when a gate rejects it (reason explains why — no tool-calling profile, AI disabled
 * on the rule, resource not on the Map, another investigation in flight), `Failed` on error. */
export interface ProactiveInsightStatusEvent {
    ruleId: string;
    firedAt: string;
    ruleName: string;
    stage: "Started" | "Skipped" | "Failed";
    reason?: string | null;
}

/** Pushed once a background proactive investigation completes (workspace-intelligence Module 4).
 * `ruleId`+`firedAt` together are the same composite identity the originating `AlertFiredEvent` has
 * — used to de-dup a dismissed insight against the firing event it came from. */
export interface ProactiveInsightReadyEvent {
    ruleId: string;
    firedAt: string;
    ruleName: string;
    summary: string;
    sessionId: string;
    /** Factual findings from the multi-step investigation (agent-workspace-awareness Module 2).
     * Absent/empty on the legacy single-shot fallback path. */
    evidence?: string[];
}

export async function getMonitoringRules(
    signal?: AbortSignal,
): Promise<MonitoringAlertRule[]> {
    return apiFetch<MonitoringAlertRule[]>("/api/monitoring/rules", { signal });
}

export async function createMonitoringRule(
    rule: MonitoringAlertRule,
): Promise<MonitoringAlertRule> {
    return apiSend<MonitoringAlertRule>("/api/monitoring/rules", "POST", rule);
}

export async function updateMonitoringRule(
    rule: MonitoringAlertRule,
): Promise<MonitoringAlertRule> {
    return apiSend<MonitoringAlertRule>(
        `/api/monitoring/rules/${rule.id}`,
        "PUT",
        rule,
    );
}

export async function deleteMonitoringRule(id: string): Promise<void> {
    await apiSend<void>(`/api/monitoring/rules/${id}`, "DELETE");
}

export async function getMonitoringHistory(
    signal?: AbortSignal,
): Promise<AlertFiredEvent[]> {
    return apiFetch<AlertFiredEvent[]>("/api/monitoring/history", { signal });
}

// ── AI insight reports (ai-insight-reports) ──────────────────────────────────

/** One concrete config fix the investigation produced — a minimal corrected snippet
 * (YAML fragment, env var, connection string...) with a one-line explanation. */
interface ProposedFix {
    explanation: string;
    language: string;
    snippet: string;
}

/** Persisted record of one completed background proactive investigation — the
 * permanent record behind the Monitoring "AI Reports" tab. `id` equals `sessionId`
 * (`proactive-{ruleId}-{firedAt ms}`) so a live `ProactiveInsightReadyEvent` can
 * deep-link straight to it. */
export interface ProactiveInsightReport {
    id: string;
    ruleId: string;
    ruleName: string;
    firedAt: string;
    alertMessage?: string | null;
    hypothesis: string;
    severity?: string | null;
    evidence: string[];
    suggestedNextSteps: string[];
    proposedFix?: ProposedFix | null;
    toolsUsed: string[];
    hitMaxRounds: boolean;
    sessionId: string;
    createdAt: string;
}

/** Response of `openInsightChat` — the report's chat session plus its transcript. */
export interface InsightChatSession {
    sessionId: string;
    messages: { role: string; content: string | null }[];
}

export async function getMonitoringInsights(
    signal?: AbortSignal,
): Promise<ProactiveInsightReport[]> {
    return apiFetch<ProactiveInsightReport[]>("/api/monitoring/insights", {
        signal,
    });
}

export async function deleteMonitoringInsight(id: string): Promise<void> {
    await apiSend<void>(
        `/api/monitoring/insights/${encodeURIComponent(id)}`,
        "DELETE",
    );
}

/** Materializes the report's chat session (re-seeded from the persisted report if
 * the in-memory store evicted it) and returns it with its transcript. */
export async function openInsightChat(id: string): Promise<InsightChatSession> {
    return apiSend<InsightChatSession>(
        `/api/monitoring/insights/${encodeURIComponent(id)}/open-chat`,
        "POST",
    );
}

export interface SbNamespaceListItem {
    id: string;
    alias: string;
    fullyQualifiedNamespace: string;
}

export interface RedisCacheListItem {
    id: string;
    displayName: string;
}

/** Returns the configured Service Bus namespaces (alias + id) for the alert entity picker. */
export async function getServiceBusNamespaces(
    signal?: AbortSignal,
): Promise<SbNamespaceListItem[]> {
    const data = await apiFetch<{
        serviceBusNamespaces?: SbNamespaceListItem[];
    }>("/api/config/profiles", { signal });
    return data.serviceBusNamespaces ?? [];
}

/** Returns the configured Redis caches (displayName + id) for the alert connection picker. */
export async function getRedisCaches(
    signal?: AbortSignal,
): Promise<RedisCacheListItem[]> {
    const data = await apiFetch<{
        config?: { redisConfig?: { caches?: RedisCacheListItem[] } };
    }>("/api/config/profiles", { signal });
    return data.config?.redisConfig?.caches ?? [];
}
