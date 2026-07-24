import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend } from "./api";
import type {
  ProfileData,
  UserSettings,
  EnvironmentsResponse,
  SbEntityInfo,
  SbMessage,
  SbNamespaceInfo,
} from "./types";

// ── Profile ──────────────────────────────────────────────────────────────────

export function useProfile() {
  return useQuery({
    queryKey: ["profile"],
    queryFn: () => apiFetch<ProfileData>("/api/config/profiles"),
  });
}

export function useUpdateProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: ProfileData) =>
      apiSend("/api/config/profiles", "PUT", data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["profile"] }),
  });
}

// ── User Settings ────────────────────────────────────────────────────────────

export function useUserSettings() {
  return useQuery({
    queryKey: ["user-settings"],
    queryFn: () => apiFetch<UserSettings>("/api/config/user-settings"),
  });
}

export function useUpdateUserSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: UserSettings) =>
      apiSend("/api/config/user-settings", "PUT", data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["user-settings"] }),
  });
}

// ── Environments ─────────────────────────────────────────────────────────────

export function useEnvironments() {
  return useQuery({
    queryKey: ["environments"],
    queryFn: () => apiFetch<EnvironmentsResponse>("/api/config/environments"),
  });
}

// ── Health ───────────────────────────────────────────────────────────────────

export function useHealth() {
  return useQuery({
    queryKey: ["health"],
    queryFn: () => apiFetch<{ status: string; version: string }>("/health"),
    refetchInterval: 10_000,
  });
}

// ── Service Bus ──────────────────────────────────────────────────────────────

export function useSbTestConnection(nsId: string | null) {
  return useQuery({
    queryKey: ["sb-test", nsId],
    queryFn: () => apiFetch<{ connected: boolean; error?: string }>(
      `/api/servicebus/${nsId}/test`,
    ),
    enabled: !!nsId,
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

export function useSbSendMessage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; message: SbMessage }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/send`, "POST", vars.message),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-peek", vars.nsId, vars.entityPath] });
      qc.invalidateQueries({ queryKey: ["sb-queues", vars.nsId] });
      qc.invalidateQueries({ queryKey: ["sb-topics", vars.nsId] });
    },
  });
}

export function useSbCompleteMessages() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; sequenceNumbers: number[] }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/complete`, "POST", vars.sequenceNumbers),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-peek", vars.nsId, vars.entityPath] });
    },
  });
}

export function useSbPurgeMessages() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; deadLetter: boolean }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/purge`, "POST", { deadLetter: vars.deadLetter }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-peek", vars.nsId, vars.entityPath] });
      qc.invalidateQueries({ queryKey: ["sb-dlq", vars.nsId, vars.entityPath] });
    },
  });
}

export function useSbCompleteDlq() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { nsId: string; entityPath: string; sequenceNumbers: string[] }) =>
      apiSend(`/api/servicebus/${vars.nsId}/entities/${vars.entityPath}/dlq/complete`, "POST", vars.sequenceNumbers),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["sb-dlq", vars.nsId, vars.entityPath] });
    },
  });
}

export function useSbResubmitDlq() {
  const qc = useQueryClient();
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
      qc.invalidateQueries({ queryKey: ["sb-dlq", vars.nsId, vars.entityPath] });
    },
  });
}
