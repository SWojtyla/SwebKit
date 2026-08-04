import { useRef, useState } from "react";
import { useAgentChatStream, useAgentClear, useAgentStatus } from "./useAgent";
import type { AgentChatMode, AgentChatScope, AgentReply, AgentStreamEvent } from "../types";

/**
 * Wires up a contextual assistant panel: a stable per-mount session id (so this panel's
 * conversation never shares history with another one or with the global /agent page — see
 * ai-augmented-app technical-plan.md Module 2), the Ask/Ask & do mode toggle (Module 5), and the
 * chat/clear/status hooks scoped to that session.
 *
 * `featureArea` should match a backend FeatureArea enum member name (e.g. "Aks", "Redis") — see
 * SidecarAgentChatService.cs for the parsing side. `selection` is whatever the calling page already
 * tracks (namespace/pod, cache/key, requestId, ...) — passed straight through, no new state.
 */
export function useContextualAgent(featureArea: string, selection?: Record<string, string>) {
  const sessionIdRef = useRef<string>(crypto.randomUUID());
  const sessionId = sessionIdRef.current;

  // Ask is the default and stays the default for every fresh conversation — see ux-plan.md: a
  // conversation never starts on Ask & do just because a previous one was switched to it.
  const [mode, setMode] = useState<AgentChatMode>("ask");
  // Same for scope — "search across my whole workspace" is a per-turn escalation the user opts
  // into, never a sticky default carried over from a previous conversation.
  const [scope, setScope] = useState<AgentChatScope>("feature");

  const chat = useAgentChatStream(sessionId);
  const clear = useAgentClear(sessionId);
  const status = useAgentStatus(sessionId);

  const sendMessage = (
    message: string,
    options?: {
      onToken?: (token: string) => void;
      onToolEvent?: (event: AgentStreamEvent) => void;
      onSuccess?: (reply: AgentReply) => void;
      onError?: (err: Error) => void;
    },
  ) => {
    chat
      .send(message, {
        context: { featureArea, selection },
        mode,
        scope,
        onToken: options?.onToken,
        onToolEvent: options?.onToolEvent,
      })
      .then((reply) => options?.onSuccess?.(reply))
      .catch((err: Error) => options?.onError?.(err));
  };

  return { sessionId, mode, setMode, scope, setScope, chat, clear, status, sendMessage };
}
