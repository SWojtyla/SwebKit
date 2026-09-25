import { create } from "zustand";

/**
 * Open-state for the docked `GlobalAgentPanel`, plus a one-shot prompt queue.
 * Lives in a store (not `AppLayout` useState) so any page — the dashboard's command
 * bar being the first — can open the panel and hand it a message. The panel is
 * always mounted (ResizablePanel hides it visually) and owns the single
 * `useAgentChatStream` instance, so it consumes `queuedPrompt` itself via an
 * effect rather than letting callers `send()` through a second hook instance —
 * that kept `isStreaming`/cancel wired to the one stream that actually exists.
 */
interface AgentPanelState {
  open: boolean;
  /** Prompt handed off by another surface; the panel sends and clears it. */
  queuedPrompt: string | null;
  setOpen: (open: boolean) => void;
  toggle: () => void;
  /** Queue a prompt and open the panel so the user sees it being sent. */
  queuePrompt: (text: string) => void;
  clearQueuedPrompt: () => void;
}

export const useAgentPanelStore = create<AgentPanelState>((set) => ({
  open: false,
  queuedPrompt: null,
  setOpen: (open) => set({ open }),
  toggle: () => set((s) => ({ open: !s.open })),
  queuePrompt: (text) => set({ open: true, queuedPrompt: text }),
  clearQueuedPrompt: () => set({ queuedPrompt: null }),
}));
