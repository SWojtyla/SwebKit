import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useEffectEvent, useRef, useState } from "react";
import {
    getMonitoringRules,
    createMonitoringRule,
    updateMonitoringRule,
    deleteMonitoringRule,
    getMonitoringHistory,
    getMonitoringInsights,
    deleteMonitoringInsight,
    openInsightChat,
} from "../api";
import { useNotification } from "@/components/layout/notification-context";
import { useMonitoringStreamApi } from "@/lib/monitoring-stream-context";
import type {
    MonitoringAlertRule,
    AlertFiredEvent,
    AlertSignalStatus,
    AlertEvaluatedEvent,
    ProactiveInsightReadyEvent,
    ProactiveInsightStatusEvent,
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
            notify("error", "Couldn't create alert rule", String(error)),
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
            notify("error", "Couldn't save alert rule", String(error)),
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
            notify("error", "Couldn't delete alert rule", String(error)),
    });
}

export function useMonitoringHistory() {
    return useQuery({
        queryKey: ["monitoring", "history"],
        queryFn: ({ signal }) => getMonitoringHistory(signal),
        refetchInterval: 15_000,
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
            notify("error", "Couldn't delete the AI report", String(error)),
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
 * (`kind: "proactiveInsightReady"`), `AlertEvaluatedEvent` (`kind: "evaluationCompleted"`), and
 * `ProactiveInsightStatusEvent` (`kind: "proactiveInsightStatus"`).
 */
export function useMonitoringStream(
    onEvent: (evt: AlertFiredEvent) => void,
    onInsightReady?: (evt: ProactiveInsightReadyEvent) => void,
    onEvaluation?: (evt: AlertEvaluatedEvent) => void,
    onInsightStatus?: (evt: ProactiveInsightStatusEvent) => void,
) {
    const stream = useMonitoringStreamApi();
    const onEventEffect = useEffectEvent(onEvent);
    const onInsightReadyEffect = useEffectEvent((evt: ProactiveInsightReadyEvent) => onInsightReady?.(evt));
    const onEvaluationEffect = useEffectEvent((evt: AlertEvaluatedEvent) => onEvaluation?.(evt));
    const onInsightStatusEffect = useEffectEvent((evt: ProactiveInsightStatusEvent) => onInsightStatus?.(evt));

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
