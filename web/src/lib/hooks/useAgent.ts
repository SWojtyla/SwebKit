import { useCallback, useRef, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend, streamAgentChat } from "../api";
import type {
  AgentActionApplyResult,
  AgentCapabilityTestResult,
  AgentChatContext,
  AgentChatMode,
  AgentChatScope,
  AgentProfile,
  AgentReply,
  AgentStatus,
  AgentStreamEvent,
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

interface SendMessageVars {
  message: string;
  /** What the current page has open, for a contextual assistant panel. Omitted (as the global
   * /agent page does) means no "Current focus" system-prompt section and no feature-area tool
   * scoping — matches pre-Module-5 behavior for that page. */
  context?: AgentChatContext;
  /** "ask" (default, read-only tools only) or "ask_and_do". Omitting this — as every caller does
   * until Module 6 adds the actual toggle — is equivalent to "ask": the sidecar treats a missing
   * or unrecognized mode as the safe option, never as permission to mutate. */
  mode?: AgentChatMode;
  /** "feature" (default) or "workspace" — the "search across my whole workspace" escalation
   * (workspace-intelligence Module 3). Omitting this is equivalent to "feature": unchanged
   * per-area tool scoping. */
  scope?: AgentChatScope;
}

export function useAgentChat(sessionId?: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ message, context, mode, scope }: SendMessageVars) =>
      apiSend<AgentReply>("/api/agent/chat", "POST", { message, sessionId, context, mode, scope }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["agent", "status", sessionKey(sessionId)] });
    },
  });
}

interface StreamSendOptions {
  context?: AgentChatContext;
  mode?: AgentChatMode;
  scope?: AgentChatScope;
  /** Called for every incremental text chunk, in order — append, don't replace. */
  onToken?: (token: string) => void;
  /** Called when a tool call starts or finishes (Ask & do turns only). */
  onToolEvent?: (event: AgentStreamEvent) => void;
}

/**
 * Streaming counterpart to {@link useAgentChat} — same session scoping, but reports incremental
 * text (via `onToken`) as the model produces it instead of only the finished reply. `send` resolves
 * with the same {@link AgentReply} shape the non-streaming endpoint returns (see
 * `AgentEndpoints.ToWireEvent` on the sidecar side for why the wire shapes were made to match), so a
 * caller can reuse the same "append final assistant message" logic for both.
 *
 * There is deliberately no automatic fallback to the non-streaming endpoint on failure — if the
 * stream errors out (model unreachable, disconnected mid-turn), `send`'s promise rejects and the
 * caller shows that like any other error; nothing here silently redrives a second request.
 */
export function useAgentChatStream(sessionId?: string) {
  const qc = useQueryClient();
  const [isStreaming, setIsStreaming] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  const send = useCallback(
    (message: string, options?: StreamSendOptions) => {
      const controller = new AbortController();
      abortRef.current = controller;
      setIsStreaming(true);

      return new Promise<AgentReply>((resolve, reject) => {
        let settled = false;

        streamAgentChat(
          { message, sessionId, context: options?.context, mode: options?.mode, scope: options?.scope },
          (event) => {
            switch (event.kind) {
              case "token":
                if (event.token) options?.onToken?.(event.token);
                break;
              case "toolCallStarted":
              case "toolCallResult":
                options?.onToolEvent?.(event);
                break;
              case "done":
                if (event.result) {
                  settled = true;
                  resolve(event.result);
                }
                break;
              case "error":
                settled = true;
                reject(new Error(event.errorMessage ?? "The agent stream failed."));
                break;
            }
          },
          controller.signal,
        )
          .catch((err: unknown) => {
            if (!settled) reject(err instanceof Error ? err : new Error(String(err)));
          })
          .finally(() => {
            setIsStreaming(false);
            qc.invalidateQueries({ queryKey: ["agent", "status", sessionKey(sessionId)] });
          });
      });
    },
    [sessionId, qc],
  );

  const cancel = useCallback(() => {
    abortRef.current?.abort();
  }, []);

  return { send, isStreaming, cancel };
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

/**
 * Tests a profile's connection/capability. Sends the full `profile` object as the request body
 * — not just its id — so the test always runs against exactly what's currently on screen. The
 * settings form saves on every keystroke via a fire-and-forget `PUT` the UI never awaits, so
 * looking the profile up by id alone could race that save and silently test a stale value.
 */
export function useTestAgentProfile() {
  return useMutation({
    mutationFn: (profile: AgentProfile) =>
      apiSend<AgentCapabilityTestResult>(`/api/agent/profiles/${profile.id}/test`, "POST", profile),
  });
}
