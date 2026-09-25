import { useCallback, useEffect, useRef, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend, streamAgentChat } from "../api";
import { useNotification } from "@/components/layout/NotificationSystem";
import type {
  AcpPermission,
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
    queryFn: ({ signal }) => apiFetch<PendingAction[]>("/api/agent/pending-approvals", { signal }),
    refetchInterval: 30_000,
  });
}

/**
 * Parked ACP permission requests (see `AcpPermission` in types). Polled faster than pending
 * approvals because a live agent turn is blocked waiting on the answer — the `permissionRequired`
 * stream event also invalidates this query immediately rather than waiting for the next tick.
 * Returns an empty list on non-ACP profiles and whenever requireToolApproval is off.
 */
export function useAcpPermissions() {
  return useQuery({
    queryKey: ["acp-permissions"],
    queryFn: ({ signal }) => apiFetch<AcpPermission[]>("/api/agent/acp/permissions", { signal }),
    refetchInterval: 5_000,
  });
}

export function useRespondAcpPermission() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: ({ id, optionId }: { id: string; optionId: string }) =>
      apiSend(`/api/agent/acp/permissions/${id}/respond`, "POST", {
        optionId,
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["acp-permissions"] }),
    onError: (error) =>
      notify("error", "Couldn't respond to the agent's permission request", String(error)),
  });
}

/** One entry in a {@link usePendingActionsFeed} feed: either a still-live proposal, or one that was
 * previously shown and has since expired server-side (see {@link reconcilePendingActionsFeed}). */
export interface PendingActionFeedItem {
  action: PendingAction;
  /** True once this action has disappeared from a poll after its own `expiresAt` had passed —
   * distinguishes a silent server-side timeout from a confirm/reject the user just triggered
   * themselves (both also remove the action from the next poll, but neither is silent to the user:
   * confirm keeps rendering its own result inline, and reject is an immediate, user-caused removal). */
  expired: boolean;
}

/**
 * Reconciles the raw `["pending-approvals"]` poll result against what was previously shown, so an
 * action that quietly times out (5 minutes, server-side) gets an explicit "This proposal expired"
 * notice instead of just vanishing on the next 30s poll with no explanation (ux-interaction-
 * consistency unit 7.6). Pure so it's unit-testable without mocking React Query or timers.
 *
 * An action missing from `latest` is treated as expired only if its own `expiresAt` had already
 * passed `now` — an action removed *before* its expiry is assumed to have been resolved by the user
 * (confirmed — the card already showed that result inline — or rejected, which invalidates and
 * removes it immediately as a direct, non-silent consequence of the user's own click), not silently
 * dropped, so it's removed from the feed with no notice, same as before this fix.
 */
export function reconcilePendingActionsFeed(
  previous: PendingActionFeedItem[],
  latest: PendingAction[] | undefined,
  now: number,
): PendingActionFeedItem[] {
  if (!latest) return previous;

  const latestIds = new Set(latest.map((a) => a.id));
  const active: PendingActionFeedItem[] = latest.map((action) => ({ action, expired: false }));
  const stillExpired = previous.filter((item) => item.expired && !latestIds.has(item.action.id));
  const newlyExpired = previous
    .filter(
      (item) =>
        !item.expired &&
        !latestIds.has(item.action.id) &&
        new Date(item.action.expiresAt).getTime() <= now,
    )
    .map((item): PendingActionFeedItem => ({ action: item.action, expired: true }));

  return [...active, ...stillExpired, ...newlyExpired];
}

/**
 * Wraps {@link usePendingApprovals} with the expiry reconciliation above, for the chat surfaces
 * that render a live list of {@link PendingAction} cards (`AgentPage`, `GlobalAgentPanel`,
 * `ContextualAssistant`). Kept separate from `usePendingApprovals` itself since a couple of other
 * call sites (`DashboardPage`, `GenerateApiRequestPanel`) only need the raw count/list, not expiry
 * tracking.
 */
export function usePendingActionsFeed() {
  const query = usePendingApprovals();
  const [feed, setFeed] = useState<PendingActionFeedItem[]>([]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reconciles local feed with each poll result; Date.now() can't run during render
    setFeed((prev) => reconcilePendingActionsFeed(prev, query.data, Date.now()));
    // new Date.now() every render would defeat the reconciliation instead of only running it once
    // per actual poll result.
  }, [query.data]);

  const dismissExpired = useCallback((actionId: string) => {
    setFeed((prev) => prev.filter((item) => item.action.id !== actionId));
  }, []);

  return { feed, dismissExpired, isLoading: query.isLoading };
}

/** Turns a snake_case tool name into a short present-tense phrase for the "Thinking… (…)" loading
 * indicator (ux-interaction-consistency unit 7.3) — e.g. "get_pod_logs" → "fetching pod logs".
 * Deliberately derived from the name rather than a hardcoded per-tool map, so a newly added tool
 * gets a reasonable label for free instead of falling back to nothing. */
const TOOL_EVENT_VERBS: Record<string, string> = {
  get: "fetching",
  list: "listing",
  search: "searching",
  analyze: "analyzing",
  investigate: "investigating",
  propose: "preparing",
  prepare: "preparing",
};

export function describeAgentToolEvent(event: Pick<AgentStreamEvent, "toolName">): string {
  const toolName = event.toolName?.trim();
  if (!toolName) return "";

  const [verbKey, ...rest] = toolName.split("_");
  const verb = TOOL_EVENT_VERBS[verbKey] ?? "running";
  const subject = (rest.length > 0 ? rest : [verbKey]).join(" ");
  return `${verb} ${subject}`;
}

/** True for the `AbortError` a `fetch`/`ReadableStreamDefaultReader` rejects with when its
 * controller's `signal.abort()` is called — i.e. the user clicked "Stop" (unit 7.3), as opposed to
 * a genuine network/stream failure. Checked by `.name` rather than `instanceof DOMException` so it
 * also recognizes a plain `{ name: "AbortError" }`-shaped rejection in tests. */
export function isAbortError(error: unknown): boolean {
  return typeof error === "object" && error !== null && (error as { name?: unknown }).name === "AbortError";
}

export function useConfirmAction() {
  // Deliberately does NOT invalidate ["pending-approvals"] on success: the card that just
  // confirmed this action needs to keep rendering (with its apply result) after the list refetch
  // would otherwise remove it — the backend's GetPendingActions() already excludes applied actions,
  // so an immediate invalidation would unmount the very card showing the result before the user
  // reads it. The list's own refetchInterval clears it out naturally once the user has moved on.
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (actionId: string) =>
      apiSend<AgentActionApplyResult>(`/api/agent/pending-approvals/${actionId}/confirm`, "POST"),
    onError: (error) => notify("error", "Couldn't confirm action", String(error)),
  });
}

export function useRejectAction() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (actionId: string) =>
      apiSend(`/api/agent/pending-approvals/${actionId}/reject`, "POST"),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["pending-approvals"] }),
    onError: (error) => notify("error", "Couldn't reject action", String(error)),
  });
}

export function useAgentStatus(sessionId?: string) {
  return useQuery({
    queryKey: ["agent", "status", sessionKey(sessionId)],
    queryFn: ({ signal }) =>
      apiFetch<AgentStatus>(
        `/api/agent/status${sessionId ? `?sessionId=${encodeURIComponent(sessionId)}` : ""}`,
        { signal },
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
  const { notify } = useNotification();
  return useMutation({
    mutationFn: ({ message, context, mode, scope }: SendMessageVars) =>
      apiSend<AgentReply>("/api/agent/chat", "POST", { message, sessionId, context, mode, scope }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["agent", "status", sessionKey(sessionId)] });
    },
    onError: (error) => notify("error", "Couldn't send message to the agent", String(error)),
  });
}

interface StreamSendOptions {
  context?: AgentChatContext;
  mode?: AgentChatMode;
  scope?: AgentChatScope;
  /** Called for every incremental text chunk, in order — append, don't replace. */
  onToken?: (token: string) => void;
  /** Called for every incremental reasoning chunk (ACP agent_thought_chunk), in order —
   * append, don't replace. Render muted/collapsed — it's raw model reasoning. */
  onThought?: (token: string) => void;
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
              case "thought":
                if (event.token) options?.onThought?.(event.token);
                break;
              case "toolCallStarted":
              case "toolCallResult":
                options?.onToolEvent?.(event);
                break;
              case "permissionRequired":
                // An ACP agent parked a permission request — refresh the list
                // now instead of waiting out the 5s poll while its turn sits
                // blocked on the user's answer.
                qc.invalidateQueries({ queryKey: ["acp-permissions"] });
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
  const { notify } = useNotification();
  return useMutation({
    mutationFn: () =>
      apiSend(
        `/api/agent/clear${sessionId ? `?sessionId=${encodeURIComponent(sessionId)}` : ""}`,
        "POST",
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["agent", "status", sessionKey(sessionId)] });
    },
    onError: (error) => notify("error", "Couldn't clear conversation", String(error)),
  });
}

/**
 * Tests a profile's connection/capability. Sends the full `profile` object as the request body
 * — not just its id — so the test always runs against exactly what's currently on screen. The
 * settings form saves on every keystroke via a fire-and-forget `PUT` the UI never awaits, so
 * looking the profile up by id alone could race that save and silently test a stale value.
 */
export function useTestAgentProfile() {
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (profile: AgentProfile) =>
      apiSend<AgentCapabilityTestResult>(`/api/agent/profiles/${profile.id}/test`, "POST", profile),
    onError: (error) => notify("error", "Couldn't test agent connection", String(error)),
  });
}
