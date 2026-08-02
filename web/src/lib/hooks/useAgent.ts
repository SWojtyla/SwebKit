import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend } from "../api";
import type { AgentCapabilityTestResult, AgentReply, AgentStatus } from "../types";

// ── Agent hooks ───────────────────────────────────────────────────────────────

export function usePendingApprovals() {
  return useQuery({
    queryKey: ["pending-approvals"],
    queryFn: () => apiFetch<{ count: number }>("/api/agent/pending-approvals"),
    refetchInterval: 30_000,
  });
}

export function useAgentStatus() {
  return useQuery({
    queryKey: ["agent", "status"],
    queryFn: () => apiFetch<AgentStatus>("/api/agent/status"),
    refetchInterval: 5000,
  });
}

export function useAgentChat() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (message: string) =>
      apiSend<AgentReply>("/api/agent/chat", "POST", { message }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["agent", "status"] });
    },
  });
}

export function useAgentClear() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => apiSend("/api/agent/clear", "POST"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["agent", "status"] });
    },
  });
}

export function useTestAgentProfile() {
  return useMutation({
    mutationFn: (profileId: string) =>
      apiSend<AgentCapabilityTestResult>(`/api/agent/profiles/${profileId}/test`, "POST"),
  });
}
