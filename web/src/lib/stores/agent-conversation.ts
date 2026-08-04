import { create } from "zustand";
import type { ChatMessage } from "@/lib/types";

/**
 * Holds the transcript for the global agent session (the one every caller that omits a
 * `sessionId` shares — see `useAgent.ts`'s `sessionKey`). A Zustand store, not component
 * `useState`, specifically so the displayed messages survive navigating away from and back to
 * any page: the backend's global session already persists across navigation (that's why
 * `historyCount` never resets), but a `useState` array living inside a route component gets torn
 * down on unmount — which is what made the transcript disappear while the count stayed put.
 * `AgentPage` (the full-page view) and `GlobalAgentPanel` (the always-mounted docked panel) both
 * read/write this same store via `useGlobalAgentConversation`, so they show one identical,
 * continuously-live conversation no matter which of the two the user is looking at.
 */
interface AgentConversationState {
  messages: ChatMessage[];
  addMessage: (message: ChatMessage) => void;
  updateMessage: (id: string, patch: Partial<ChatMessage>) => void;
  appendToken: (id: string, token: string) => void;
  clearMessages: () => void;
}

export const useAgentConversationStore = create<AgentConversationState>((set) => ({
  messages: [],
  addMessage: (message) => set((state) => ({ messages: [...state.messages, message] })),
  updateMessage: (id, patch) =>
    set((state) => ({
      messages: state.messages.map((m) => (m.id === id ? { ...m, ...patch } : m)),
    })),
  appendToken: (id, token) =>
    set((state) => ({
      messages: state.messages.map((m) => (m.id === id ? { ...m, content: m.content + token } : m)),
    })),
  clearMessages: () => set({ messages: [] }),
}));
