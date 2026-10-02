import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useEffectEvent, useRef, useState } from "react";
import { describeApiError,
    getMonitoringRules,
    createMonitoringRule,
    updateMonitoringRule,
    deleteMonitoringRule,
    getMonitoringHistory,
    getMonitoringInsights,
    deleteMonitoringInsight,
    openInsightChat,
    getMonitoringSilences,
    createMonitoringSilence,
    deleteMonitoringSilence,
    muteMonitoringRule,
    getMonitoringHistorySummary,
} from "../api";
import { useNotification } from "@/components/layout/notification-context";
import { formatLocalDateTime } from "@/lib/datetime";
import { useMonitoringStreamApi } from "@/lib/monitoring-stream-context";
import type {
    MonitoringAlertRule,
    MonitoringSilence,
    AlertFiredEvent,
    AlertResolvedEvent,
    AlertSignalStatus,
    AlertEvaluatedEvent,
    ProactiveInsightReadyEvent,
    ProactiveInsightStatusEvent,
    PendingActionProposedEvent,
} from "../api";

// ── Monitoring hooks ──────────────────────────────────────────────────────────

export function useMonitoringRules(enabled = true) {
    return useQuery({
        queryKey: ["monitoring", "rules"],
        queryFn: ({ signal }) => getMonitoringRules(signal),
        enabled,
    });
}

export function useCreateMonitoringRule() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (rule: MonitoringAlertRule) => createMonitoringRule(rule),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["monitoring", "rules"] });
        },
        onError: (error) =>
            notify("error", "Couldn't create alert rule", describeApiError(error)),
    });
}

export function useUpdateMonitoringRule() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (rule: MonitoringAlertRule) => updateMonitoringRule(rule),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["monitoring", "rules"] });
        },
        onError: (error) =>
            notify("error", "Couldn't save alert rule", describeApiError(error)),
    });
}

export function useDeleteMonitoringRule() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (id: string) => deleteMonitoringRule(id),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["monitoring", "rules"] });
        },
        onError: (error) =>
            notify("error", "Couldn't delete alert rule", describeApiError(error)),
    });
}

export function useMonitoringHistory() {
    return useQuery({
        queryKey: ["monitoring", "history"],
        queryFn: ({ signal }) => getMonitoringHistory(signal),
        refetchInterval: 15_000,
    });
}

/** Ops-dashboard aggregate over the durable history (monitoring-closed-loop item 4):
 * firings/hour, severity split, open incidents, MTTR. The Ops tab mounts the consumer,
 * so no `enabled` gate is needed — it only fetches while visible. */
export function useMonitoringHistorySummary(windowHours: number) {
    return useQuery({
        queryKey: ["monitoring", "history-summary", windowHours],
        queryFn: ({ signal }) => getMonitoringHistorySummary(windowHours, signal),
        refetchInterval: 30_000,
    });
}

/** Persisted silence windows (monitoring-closed-loop item 3). Modest poll — the windows
 * change only on user action; the firing-side check is server-side anyway. */
export function useMonitoringSilences() {
    return useQuery({
        queryKey: ["monitoring", "silences"],
        queryFn: ({ signal }) => getMonitoringSilences(signal),
        refetchInterval: 30_000,
    });
}

export function useCreateMonitoringSilence() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (silence: Omit<MonitoringSilence, "id">) =>
            createMonitoringSilence(silence),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["monitoring", "silences"] });
        },
        onError: (error) =>
            notify("error", "Couldn't create the silence", describeApiError(error)),
    });
}

export function useDeleteMonitoringSilence() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (id: string) => deleteMonitoringSilence(id),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["monitoring", "silences"] });
        },
        onError: (error) =>
            notify("error", "Couldn't delete the silence", describeApiError(error)),
    });
}

/** Sets or clears a rule's per-rule mute. `until: null` unmutes. The rules query refreshes so
 * the row's muted badge updates; a firing during the mute lands in history flagged
 * `suppressed` (audit trail), but no toast or AI investigation. */
export function useMuteMonitoringRule() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: ({ id, until }: { id: string; until: string | null }) =>
            muteMonitoringRule(id, until),
        onSuccess: (rule, { until }) => {
            qc.invalidateQueries({ queryKey: ["monitoring", "rules"] });
            notify(
                "info",
                until === null ? "Rule unmuted" : "Rule muted",
                until === null
                    ? `"${rule.name}" will alert normally.`
                    : `"${rule.name}" is muted until ${formatLocalDateTime(until)}.`,
                undefined,
                "/monitoring",
            );
        },
        onError: (error) =>
            notify("error", "Couldn't update the rule mute", describeApiError(error)),
    });
}

/**
 * Persisted AI investigation reports backing the Monitoring "AI Reports" tab
 * (ai-insight-reports). The query is invalidated by `useMonitoringStream`'s
 * `proactiveInsightReady` handler on the page so a completed investigation shows
 * up without waiting for a poll; the modest refetchInterval covers reports written
 * while no monitoring page was mounted.
 */
export function useMonitoringInsights() {
    return useQuery({
        queryKey: ["monitoring", "insights"],
        queryFn: ({ signal }) => getMonitoringInsights(signal),
        refetchInterval: 30_000,
    });
}

export function useDeleteMonitoringInsight() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (id: string) => deleteMonitoringInsight(id),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["monitoring", "insights"] });
        },
        onError: (error) =>
            notify("error", "Couldn't delete the AI report", describeApiError(error)),
    });
}

/** Materializes a report's chat session (re-seeded server-side if evicted) and
 * returns it with its transcript — the "Discuss in chat" handoff. */
export function useOpenInsightChat() {
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (id: string) => openInsightChat(id),
        onError: (error) =>
            notify(
                "error",
                "Couldn't open the report conversation",
                String(error),
            ),
    });
}

export interface MonitoringEvaluationState {
    status: AlertSignalStatus;
    evaluatedAt: string;
}

/**
 * Subscribes to the monitoring event stream shared app-wide by
 * `MonitoringStreamProvider` — registering a listener on the shell's single EventSource
 * rather than opening a per-consumer connection. Buffered frames replay on subscribe, so a
 * page mounted late still sees an insight (or alert) that fired while it wasn't mounted.
 *
 * Each frame is a `{kind, event}` envelope (workspace-intelligence Module 4) so the one stream
 * carries `AlertFiredEvent` (`kind: "alertFired"`), `ProactiveInsightReadyEvent`
 * (`kind: "proactiveInsightReady"`), `AlertEvaluatedEvent` (`kind: "evaluationCompleted"`),
 * `ProactiveInsightStatusEvent` (`kind: "proactiveInsightStatus"`), `AlertResolvedEvent`
 * (`kind: "alertResolved"` — the recovery signal when a firing rule evaluates Ok again), and
 * `PendingActionProposedEvent` (`kind: "pendingActionProposed"` — an opted-in investigation just
 * parked a confirmable remediation proposal).
 */
export function useMonitoringStream(
    onEvent: (evt: AlertFiredEvent) => void,
    onInsightReady?: (evt: ProactiveInsightReadyEvent) => void,
    onEvaluation?: (evt: AlertEvaluatedEvent) => void,
    onInsightStatus?: (evt: ProactiveInsightStatusEvent) => void,
    onResolved?: (evt: AlertResolvedEvent) => void,
    onPendingActionProposed?: (evt: PendingActionProposedEvent) => void,
) {
    const stream = useMonitoringStreamApi();
    const onEventEffect = useEffectEvent(onEvent);
    const onInsightReadyEffect = useEffectEvent((evt: ProactiveInsightReadyEvent) => onInsightReady?.(evt));
    const onEvaluationEffect = useEffectEvent((evt: AlertEvaluatedEvent) => onEvaluation?.(evt));
    const onInsightStatusEffect = useEffectEvent((evt: ProactiveInsightStatusEvent) => onInsightStatus?.(evt));
    const onResolvedEffect = useEffectEvent((evt: AlertResolvedEvent) => onResolved?.(evt));
    const onPendingActionProposedEffect = useEffectEvent((evt: PendingActionProposedEvent) => onPendingActionProposed?.(evt));

    // Highest seq this subscription has already consumed — replaying only newer frames keeps
    // a StrictMode re-subscribe from double-appending buffered events into subscriber state.
    const cursorRef = useRef(0);

    useEffect(() => {
        return stream.subscribe((frame) => {
            cursorRef.current = frame.seq;
            if (frame.kind === "alertFired") {
                onEventEffect(frame.event as AlertFiredEvent);
            } else if (frame.kind === "proactiveInsightReady") {
                onInsightReadyEffect(
                    frame.event as ProactiveInsightReadyEvent,
                );
            } else if (frame.kind === "evaluationCompleted") {
                onEvaluationEffect(frame.event as AlertEvaluatedEvent);
            } else if (frame.kind === "proactiveInsightStatus") {
                onInsightStatusEffect(
                    frame.event as ProactiveInsightStatusEvent,
                );
            } else if (frame.kind === "alertResolved") {
                onResolvedEffect(frame.event as AlertResolvedEvent);
            } else if (frame.kind === "pendingActionProposed") {
                onPendingActionProposedEffect(
                    frame.event as PendingActionProposedEvent,
                );
            }
        }, cursorRef.current);
    }, [stream]);
}

const DISMISSED_INSIGHTS_STORAGE_KEY = "swebkit:dismissed-proactive-insights";

function insightKey(insight: ProactiveInsightReadyEvent) {
    return `${insight.ruleId}|${insight.firedAt}`;
}

function loadDismissedKeys(): Set<string> {
    try {
        const raw = sessionStorage.getItem(DISMISSED_INSIGHTS_STORAGE_KEY);
        return raw ? new Set(JSON.parse(raw) as string[]) : new Set();
    } catch {
        return new Set();
    }
}

/**
 * Tracks proactive insight cards fed by {@link useMonitoringStream}'s `onInsightReady` callback.
 * Dismissed insights are persisted to `sessionStorage` (workspace-intelligence Module 4's "at least
 * per-session" de-dup requirement) keyed by `ruleId|firedAt` — the same composite identity the
 * originating fired event has — so a reload doesn't re-surface an insight the user already
 * dismissed for that specific firing, while a brand-new tab/session starts clean.
 */
export function useProactiveInsightsFeed() {
    const [insights, setInsights] = useState<ProactiveInsightReadyEvent[]>([]);
    const dismissedRef = useRef<Set<string>>(loadDismissedKeys());

    const addInsight = (insight: ProactiveInsightReadyEvent) => {
        if (dismissedRef.current.has(insightKey(insight))) return;
        setInsights((prev) =>
            prev.some((i) => insightKey(i) === insightKey(insight))
                ? prev
                : [insight, ...prev],
        );
    };

    const dismiss = (insight: ProactiveInsightReadyEvent) => {
        dismissedRef.current.add(insightKey(insight));
        try {
            sessionStorage.setItem(
                DISMISSED_INSIGHTS_STORAGE_KEY,
                JSON.stringify([...dismissedRef.current]),
            );
        } catch {
            /* sessionStorage unavailable — dismissal still works for this render, just not persisted */
        }
        setInsights((prev) =>
            prev.filter((i) => insightKey(i) !== insightKey(insight)),
        );
    };

    return { insights, addInsight, dismiss };
}
