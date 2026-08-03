import { useCallback } from "react";
import { useAgentChatStream, useAgentClear, useAgentStatus, usePendingApprovals } from "./useAgent";
import { useAgentConversationStore } from "@/lib/stores/agent-conversation";

let msgIdCounter = 0;
function nextMsgId() {
  return `msg-${++msgIdCounter}`;
}

/**
 * Single source of truth for the global agent session's conversation — shared by `AgentPage.tsx`
 * (full-page view) and `GlobalAgentPanel.tsx` (always-mounted docked panel), so sending a message
 * from either one shows up the same way in both, and neither loses its transcript when the other
 * mounts/unmounts. All state lives in `useAgentConversationStore` (a Zustand store, not
 * component-local `useState`), which is what actually survives route navigation.
 */
export function useGlobalAgentConversation() {
  const messages = useAgentConversationStore((s) => s.messages);
  const addMessage = useAgentConversationStore((s) => s.addMessage);
  const updateMessage = useAgentConversationStore((s) => s.updateMessage);
  const appendToken = useAgentConversationStore((s) => s.appendToken);
  const clearMessages = useAgentConversationStore((s) => s.clearMessages);

  const chat = useAgentChatStream();
  const clear = useAgentClear();
  const status = useAgentStatus();
  const pendingApprovals = usePendingApprovals();

  const send = useCallback(
    (text: string) => {
      const trimmed = text.trim();
      if (!trimmed || chat.isStreaming) return;

      const assistantId = nextMsgId();
      addMessage({ id: nextMsgId(), role: "user", content: trimmed });
      addMessage({ id: assistantId, role: "assistant", content: "" });

      chat
        .send(trimmed, { onToken: (token) => appendToken(assistantId, token) })
        .then((reply) => {
          updateMessage(assistantId, { content: reply.text, elapsedMs: reply.elapsedMs, error: reply.error });
        })
        .catch((err: Error) => {
          updateMessage(assistantId, { content: `Error: ${err.message}`, error: true });
        });
    },
    [chat, addMessage, updateMessage, appendToken],
  );

  const clearConversation = useCallback(
    (onCleared?: () => void) => {
      clear.mutate(undefined, {
        onSuccess: () => {
          clearMessages();
          onCleared?.();
        },
      });
    },
    [clear, clearMessages],
  );

  return {
    messages,
    send,
    isStreaming: chat.isStreaming,
    clear: clearConversation,
    isClearPending: clear.isPending,
    status,
    pendingApprovals,
  };
}
