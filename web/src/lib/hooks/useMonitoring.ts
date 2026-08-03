import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import {
  SIDECAR_BASE_URL,
  getMonitoringRules,
  createMonitoringRule,
  updateMonitoringRule,
  deleteMonitoringRule,
  getMonitoringHistory,
} from "../api";
import type { MonitoringAlertRule, AlertFiredEvent, AlertSignalStatus, ProactiveInsightReadyEvent } from "../api";

// ── Monitoring hooks ──────────────────────────────────────────────────────────

export function useMonitoringRules() {
  return useQuery({
    queryKey: ["monitoring", "rules"],
    queryFn: () => getMonitoringRules(),
  });
}

export function useCreateMonitoringRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (rule: MonitoringAlertRule) => createMonitoringRule(rule),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["monitoring", "rules"] });
    },
  });
}

export function useUpdateMonitoringRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (rule: MonitoringAlertRule) => updateMonitoringRule(rule),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["monitoring", "rules"] });
    },
  });
}

export function useDeleteMonitoringRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteMonitoringRule(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["monitoring", "rules"] });
    },
  });
}

export function useMonitoringHistory() {
  return useQuery({
    queryKey: ["monitoring", "history"],
    queryFn: () => getMonitoringHistory(),
    refetchInterval: 15_000,
  });
}

export interface MonitoringEvaluationState {
  status: AlertSignalStatus;
  evaluatedAt: string;
}

/**
 * Subscribes to the sidecar's SSE alert stream. New fired events are merged into the
 * supplied callback (typically to seed/extend the history feed). Mirrors the AKS pod-log
 * EventSource lifecycle pattern used elsewhere in the app.
 *
 * Each frame is a `{kind, event}` envelope (workspace-intelligence Module 4) so this one stream can
 * carry both `AlertFiredEvent` (`kind: "alertFired"`) and the new `ProactiveInsightReadyEvent`
 * (`kind: "proactiveInsightReady"`) — `onInsightReady` is optional since most callers only care
 * about the pre-existing fired-alert feed.
 */
export function useMonitoringStream(
  onEvent: (evt: AlertFiredEvent) => void,
  onInsightReady?: (evt: ProactiveInsightReadyEvent) => void,
) {
  const cbRef = useRef(onEvent);
  cbRef.current = onEvent;
  const insightCbRef = useRef(onInsightReady);
  insightCbRef.current = onInsightReady;

  useEffect(() => {
    const es = new EventSource(`${SIDECAR_BASE_URL}/api/monitoring/stream`);
    es.onmessage = (msg) => {
      try {
        const frame = JSON.parse(msg.data) as { kind: string; event: unknown };
        if (frame.kind === "alertFired") {
          cbRef.current(frame.event as AlertFiredEvent);
        } else if (frame.kind === "proactiveInsightReady") {
          insightCbRef.current?.(frame.event as ProactiveInsightReadyEvent);
        }
      } catch {
        /* ignore malformed frames */
      }
    };
    return () => es.close();
  }, []);
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
    setInsights((prev) => (prev.some((i) => insightKey(i) === insightKey(insight)) ? prev : [insight, ...prev]));
  };

  const dismiss = (insight: ProactiveInsightReadyEvent) => {
    dismissedRef.current.add(insightKey(insight));
    try {
      sessionStorage.setItem(DISMISSED_INSIGHTS_STORAGE_KEY, JSON.stringify([...dismissedRef.current]));
    } catch {
      /* sessionStorage unavailable — dismissal still works for this render, just not persisted */
    }
    setInsights((prev) => prev.filter((i) => insightKey(i) !== insightKey(insight)));
  };

  return { insights, addInsight, dismiss };
}
