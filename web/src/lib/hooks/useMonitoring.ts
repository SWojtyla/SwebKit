import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import {
  SIDECAR_BASE_URL,
  getMonitoringRules,
  createMonitoringRule,
  updateMonitoringRule,
  deleteMonitoringRule,
  getMonitoringHistory,
} from "../api";
import type { MonitoringAlertRule, AlertFiredEvent, AlertSignalStatus } from "../api";

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
 */
export function useMonitoringStream(onEvent: (evt: AlertFiredEvent) => void) {
  const cbRef = useRef(onEvent);
  cbRef.current = onEvent;

  useEffect(() => {
    const es = new EventSource(`${SIDECAR_BASE_URL}/api/monitoring/stream`);
    es.onmessage = (msg) => {
      try {
        const evt = JSON.parse(msg.data) as AlertFiredEvent;
        cbRef.current(evt);
      } catch {
        /* ignore malformed frames */
      }
    };
    return () => es.close();
  }, []);
}
