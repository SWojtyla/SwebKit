// "Watch this" prefill (agent-colleague item 3): converts an EvidenceItem.watch hint
// into a MonitoringAlertRule draft so clicking the button lands on the existing
// AlertRuleDialog prefilled — via `navigate("/", { state: { prefillRule } })`, the
// convention MonitoringPage already honors (it runs the draft through
// buildPrefilledRuleDraft() which fills safe defaults like id: ""/cooldownMinutes: 5).
//
// Only alert-rule sources the dialog can express are supported — its source picker
// has no StorageBlobCount, and arbitrary KQL/App Insights rules are a non-goal
// (agent R scopes them out entirely). The backend whitelist
// (EvidenceItemParser.ParseWatch) already rejects non-AlertRuleSource names; this
// narrows further to sources the dialog renders.

import type { AlertRuleSource, MonitoringAlertRule } from "../../lib/api/monitoring";

/** Sources the rule dialog's picker offers — the only ones a watch hint may carry. */
const WATCH_SOURCES = new Set<AlertRuleSource>([
    "AksPodHealth",
    "AksPodRestartRate",
    "AksNamespaceHealthScore",
    "ServiceBusDlqDepth",
    "ServiceBusActiveDepth",
    "ServiceBusDeadSubscription",
    "RedisMemoryUsage",
    "RedisConnectedClients",
]);

const SEVERITIES = new Set(["Warning", "Critical"]);

export interface WatchHint {
    source?: string;
    params?: Record<string, unknown> | null;
}

function asStrings(params: Record<string, unknown> | null | undefined): Record<string, string> {
    const out: Record<string, string> = {};
    if (params && typeof params === "object") {
        for (const [key, value] of Object.entries(params)) {
            const str =
                typeof value === "string"
                    ? value.trim()
                    : typeof value === "number" || typeof value === "boolean"
                      ? String(value)
                      : "";
            if (str) out[key] = str;
        }
    }
    return out;
}

function asNumber(params: Record<string, string>, ...keys: string[]): number | undefined {
    for (const key of keys) {
        const raw = params[key];
        if (raw === undefined) continue;
        const n = Number(raw);
        if (!Number.isNaN(n) && Number.isFinite(n)) return n;
    }
    return undefined;
}

/**
 * Builds the partial rule a `state.prefillRule` navigation should carry, or null
 * when the hint is unusable (missing/unsupported source). Only recognized params
 * survive — everything else is ignored, matching the dialog's own field set.
 * Numeric param names follow the MonitoringAlertRule blocks (restartThreshold,
 * healthScoreThreshold, messageCountThreshold, memoryUsageThresholdPercent,
 * clientCountLowerBound); a generic `threshold` is honored per source so model
 * output doesn't need to know the exact block name.
 */
export function buildWatchPrefill(
    watch: WatchHint | null | undefined,
): Partial<MonitoringAlertRule> | null {
    if (!watch || typeof watch.source !== "string") return null;
    const source = watch.source.trim() as AlertRuleSource;
    if (!WATCH_SOURCES.has(source)) return null;

    const params = asStrings(watch.params);

    const draft: Partial<MonitoringAlertRule> = { source };
    if (params.name) draft.name = params.name;
    if (SEVERITIES.has(params.severity)) {
        draft.severity = params.severity as MonitoringAlertRule["severity"];
    }
    const interval = asNumber(params, "intervalSeconds");
    if (interval !== undefined && interval > 0) draft.intervalSeconds = interval;
    const cooldown = asNumber(params, "cooldownMinutes");
    if (cooldown !== undefined && cooldown > 0) draft.cooldownMinutes = cooldown;

    if (source.startsWith("Aks")) {
        // A generic "threshold" lands on the field the selected source actually
        // consumes: restart count for RestartRate, health score for the two
        // health sources.
        const generic = asNumber(params, "threshold");
        const isRestartSource = source === "AksPodRestartRate";
        draft.aksPodParams = {
            namespace: params.namespace ?? "",
            kubeconfigContext: params.kubeconfigContext || undefined,
            restartThreshold:
                asNumber(params, "restartThreshold") ??
                (isRestartSource ? generic : undefined),
            healthScoreThreshold:
                asNumber(params, "healthScoreThreshold") ??
                (isRestartSource ? undefined : generic),
        };
    } else if (source.startsWith("ServiceBus")) {
        draft.serviceBusParams = {
            namespaceConnectionAlias: params.namespaceConnectionAlias ?? params.ns ?? "",
            entityPath: params.entityPath ?? params.entity ?? "",
            messageCountThreshold: asNumber(params, "messageCountThreshold", "threshold"),
        };
    } else if (source.startsWith("Redis")) {
        draft.redisAlertParams = {
            connectionAlias: params.connectionAlias ?? params.cache ?? "",
            memoryUsageThresholdPercent:
                source === "RedisMemoryUsage"
                    ? asNumber(params, "memoryUsageThresholdPercent", "threshold")
                    : asNumber(params, "memoryUsageThresholdPercent"),
            clientCountLowerBound:
                source === "RedisConnectedClients"
                    ? asNumber(params, "clientCountLowerBound", "threshold")
                    : asNumber(params, "clientCountLowerBound"),
        };
    }

    return draft;
}
