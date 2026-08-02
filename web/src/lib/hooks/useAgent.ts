import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend } from "../api";
import type {
  AgentActionApplyResult,
  AgentCapabilityTestResult,
  AgentReply,
  AgentStatus,
  PendingAction,
} from "../types";

// ── Agent hooks ───────────────────────────────────────────────────────────────
//
// Every hook below takes an optional `sessionId`, scoping the sidecar's conversation history to a
// single contextual assistant panel instance (see ai-augmented-app technical-plan.md Module 2).
// Omitting it (as AgentPage.tsx, the global /agent page, still does) keeps today's behavior exactly
// — one shared, never-evicted session, unchanged since before per-session support existed.

const sessionKey = (sessionId?: string) => sessionId ?? "global";

export function usePendingApprovals() {
  return useQuery({
    queryKey: ["pending-approvals"],
    queryFn: () => apiFetch<PendingAction[]>("/api/agent/pending-approvals"),
    refetchInterval: 30_000,
  });
}

export function useConfirmAction() {
  // Deliberately does NOT invalidate ["pending-approvals"] on success: the card that just
  // confirmed this action needs to keep rendering (with its apply result) after the list refetch
  // would otherwise remove it — the backend's GetPendingActions() already excludes applied actions,
  // so an immediate invalidation would unmount the very card showing the result before the user
  // reads it. The list's own refetchInterval clears it out naturally once the user has moved on.
  return useMutation({
    mutationFn: (actionId: string) =>
      apiSend<AgentActionApplyResult>(`/api/agent/pending-approvals/${actionId}/confirm`, "POST"),
  });
}

export function useRejectAction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (actionId: string) =>
      apiSend(`/api/agent/pending-approvals/${actionId}/reject`, "POST"),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["pending-approvals"] }),
  });
}

export function useAgentStatus(sessionId?: string) {
  return useQuery({
    queryKey: ["agent", "status", sessionKey(sessionId)],
    queryFn: () =>
      apiFetch<AgentStatus>(
        `/api/agent/status${sessionId ? `?sessionId=${encodeURIComponent(sessionId)}` : ""}`,
      ),
    refetchInterval: 5000,
  });
}

export function useAgentChat(sessionId?: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (message: string) =>
      apiSend<AgentReply>("/api/agent/chat", "POST", { message, sessionId }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["agent", "status", sessionKey(sessionId)] });
    },
  });
}

export function useAgentClear(sessionId?: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiSend(
        `/api/agent/clear${sessionId ? `?sessionId=${encodeURIComponent(sessionId)}` : ""}`,
        "POST",
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["agent", "status", sessionKey(sessionId)] });
    },
  });
}

export function useTestAgentProfile() {
  return useMutation({
    mutationFn: (profileId: string) =>
      apiSend<AgentCapabilityTestResult>(`/api/agent/profiles/${profileId}/test`, "POST"),
  });
}
