import { useQuery, useMutation, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend } from "../api";
import { useNotification } from "@/components/layout/NotificationSystem";
import type {
  SbEntityInfo,
  SbEntityStats,
  SbMessage,
  SbNamespaceInfo,
  SbMessageTemplate,
  ScheduledMessageEntry,
} from "../types";

// ── Service Bus ──────────────────────────────────────────────────────────────

export function useSbTestConnection(nsId: string | null, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["sb-test", nsId],
    queryFn: () => apiFetch<{ connected: boolean; error?: string }>(
      `/api/servicebus/${nsId}/test`,
    ),
    enabled: !!nsId && (options?.enabled ?? true),
  });
}

export function useSbNamespaceInfo(nsId: string | null) {
  return useQuery({
    queryKey: ["sb-info", nsId],
    queryFn: () => apiFetch<SbNamespaceInfo>(`/api/servicebus/${nsId}/info`),
    enabled: !!nsId,
  });
}

export function useSbQueues(nsId: string | null) {
  return useQuery({
    queryKey: ["sb-queues", nsId],
    queryFn: () => apiFetch<SbEntityInfo[]>(`/api/servicebus/${nsId}/queues`),
    enabled: !!nsId,
  });
}

export function useSbTopics(nsId: string | null) {
  return useQuery({
    queryKey: ["sb-topics", nsId],
    queryFn: () => apiFetch<SbEntityInfo[]>(`/api/servicebus/${nsId}/topics`),
    enabled: !!nsId,
  });
}

export function useSbSubscriptions(nsId: string | null, topic: string | null) {
  return useQuery({
    queryKey: ["sb-subs", nsId, topic],
    queryFn: () =>
      apiFetch<SbEntityInfo[]>(
        `/api/servicebus/${nsId}/topics/${topic}/subscriptions`,
      ),
    enabled: !!nsId && !!topic,
  });
}

export function useSbPeekMessages(nsId: string | null, entityPath: string | null, count = 50) {
  return useQuery({
    queryKey: ["sb-peek", nsId, entityPath, count],
    queryFn: () =>
      apiFetch<SbMessage[]>(
        `/api/servicebus/${nsId}/entities/${entityPath}/peek?count=${count}`,
      ),
    enabled: !!nsId && !!entityPath,
  });
}

export function useSbPeekDlq(nsId: string | null, entityPath: string | null, count = 50) {
  return useQuery({
    queryKey: ["sb-dlq", nsId, entityPath, count],
    queryFn: () =>
      apiFetch<SbMessage[]>(
        `/api/servicebus/${nsId}/entities/${entityPath}/dlq?count=${count}`,
      ),
    enabled: !!nsId && !!entityPath,
  });
}

export function useSbEntityStats(nsId: string | null, entityPath: string | null) {
  return useQuery({
    queryKey: ["sb-entity-stats", nsId, entityPath],
    queryFn: () =>
      apiFetch<SbEntityStats>(`/api/servicebus/${nsId}/entities/${encodeURIComponent(entityPath!)}/stats`),
    enabled: !!nsId && !!entityPath,
  });
}

/**
 * Invalidates every query for one entity (peek/DLQ/stats/scheduled) plus the namespace's
 * queue/topic/subscription lists. Exported so call sites outside this file — the Refresh
 * command in `ServiceBusPage.tsx` in particular — can reuse the real key set instead of a
 * hand-rolled `invalidateQueries({ queryKey: ["sb-"] })`, which matches nothing: TanStack Query
 * compares key *elements*, not string prefixes, and every real key here is `["sb-peek", nsId,
 * entityPath, count]` and friends (same class of bug as `aks-query-keys.ts`'s note on `"aks-"`).
 */
export function invalidateServiceBusQueries(qc: QueryClient, nsId: string, entityPath: string) {
  qc.invalidateQueries({ queryKey: ["sb-peek", nsId, entityPath] });
  qc.invalidateQueries({ queryKey: ["sb-dlq", nsId, entityPath] });
  qc.invalidateQueries({ queryKey: ["sb-entity-stats", nsId, entityPath] });
  qc.invalidateQueries({ queryKey: ["sb-queues", nsId] });
  qc.invalidateQueries({ queryKey: ["sb-topics", nsId] });
  qc.invalidateQueries({ queryKey: ["sb-subs", nsId] });
  qc.invalidateQueries({ queryKey: ["sb-scheduled", nsId, entityPath] });
}

export function useSbSendMessage() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; message: SbMessage }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/send`, "POST", vars.message),
    onSuccess: (_data, vars) => {
      invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
    },
    onError: (error) => notify("error", "Couldn't send message", String(error)),
  });
}

export function useSbScheduleMessage() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; message: SbMessage; scheduledEnqueueTime: string }) =>
      apiSend<{ sequenceNumber: number }>(
        `/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/schedule`,
        "POST",
        { message: vars.message, scheduledEnqueueTime: vars.scheduledEnqueueTime },
      ),
    onSuccess: (_data, vars) => {
      invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
    },
    onError: (error) => notify("error", "Couldn't schedule message", String(error)),
  });
}

export function useSbBatchSend() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; messages: SbMessage[] }) =>
      apiSend<{ sent: number }>(
        `/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/batch-send`,
        "POST",
        vars.messages,
      ),
    onSuccess: (_data, vars) => {
      invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
    },
    onError: (error) => notify("error", "Couldn't send batch", String(error)),
  });
}

export function useSbScheduledMessages(nsId: string | null, entityPath: string | null) {
  return useQuery({
    queryKey: ["sb-scheduled", nsId, entityPath],
    queryFn: () =>
      apiFetch<ScheduledMessageEntry[]>(
        `/api/servicebus/${nsId}/entities/${entityPath}/scheduled`,
      ),
    enabled: !!nsId && !!entityPath,
  });
}

export function useSbCancelScheduled() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; sequenceNumber: number }) =>
      apiSend(
        `/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/scheduled/${vars.sequenceNumber}`,
        "DELETE",
      ),
    onSuccess: (_data, vars) => {
      invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
    },
    onError: (error) => notify("error", "Couldn't cancel scheduled message", String(error)),
  });
}

export function useSbTemplates() {
  return useQuery({
    queryKey: ["sb-templates"],
    queryFn: () => apiFetch<SbMessageTemplate[]>("/api/servicebus/templates"),
  });
}

export function useSbSaveTemplate() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (template: SbMessageTemplate) =>
      apiSend<SbMessageTemplate>("/api/servicebus/templates", "POST", template),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["sb-templates"] });
    },
    onError: (error) => notify("error", "Couldn't save template", String(error)),
  });
}

export function useSbDeleteTemplate() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (id: string) => apiSend(`/api/servicebus/templates/${id}`, "DELETE"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["sb-templates"] });
    },
    onError: (error) => notify("error", "Couldn't delete template", String(error)),
  });
}

export function useSbCompleteMessages() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; sequenceNumbers: number[] }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/complete`, "POST", vars.sequenceNumbers),
    onSuccess: (_data, vars) => {
      invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
    },
    onError: (error) => notify("error", "Couldn't complete messages", String(error)),
  });
}

export function useSbPurgeMessages() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; deadLetter: boolean }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/purge`, "POST", { deadLetter: vars.deadLetter }),
    onSuccess: (_data, vars) => {
      invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
    },
    onError: (error) => notify("error", "Couldn't purge messages", String(error)),
  });
}

export function useSbCompleteDlq() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; sequenceNumbers: string[] }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/dlq/complete`, "POST", vars.sequenceNumbers),
    onSuccess: (_data, vars) => {
      invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
    },
    onError: (error) => notify("error", "Couldn't complete dead-letter messages", String(error)),
  });
}

export function useSbResubmitDlq() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: {
      nsId: string;
      entityPath: string;
      sequenceNumbers: string[];
      targetEntityPath?: string | null;
    }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/resubmit`, "POST", {
        sequenceNumbers: vars.sequenceNumbers,
        targetEntityPath: vars.targetEntityPath ?? null,
        remapRules: null,
      }),
    onSuccess: (_data, vars) => {
      invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
    },
    onError: (error) => notify("error", "Couldn't resubmit messages", String(error)),
  });
}
