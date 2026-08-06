import { useEffect, useRef, useState } from "react";
import { useGlobalAgentConversation } from "@/lib/hooks/useGlobalAgentConversation";
import { AgentMarkdown } from "./AgentMarkdown";
import { ResizablePanel } from "@/components/ui/ResizablePanel";
import { PendingActionCard } from "./PendingActionCard";
import { AgentReasoningTrace } from "./AgentReasoningTrace";
import { AgentSummarizedNotice } from "./AgentSummarizedNotice";
import { ContextUsageIndicator } from "./ContextUsageIndicator";

interface GlobalAgentPanelProps {
  open: boolean;
  onClose: () => void;
}

/**
 * Always-mounted (in AppLayout), globally-accessible docked panel for the same conversation
 * `AgentPage.tsx` (the `/agent` route) shows — both read/write `useGlobalAgentConversation`'s
 * shared store, so switching between "the full page" and "the side panel" never loses or forks the
 * transcript. Kept out of the router tree entirely (unlike `AgentPage`) specifically so it survives
 * navigating between feature pages — see `useAgentConversationStore`'s doc comment for why a
 * route-scoped `useState` couldn't do this.
 *
 * Rendered as a real flex sibling in `AppLayout`'s layout row (like the left nav `<aside>`), not a
 * `fixed`-position overlay with a click-outside backdrop — the first version used an overlay, and
 * it closed the moment you clicked anywhere else in the app, which defeated the point of a panel
 * you're meant to keep open *while* working elsewhere. It only closes via its own "✕", the top-bar
 * toggle, or the keyboard shortcut now.
 *
 * No Ask/Ask & do mode toggle here, matching `AgentPage`'s existing scope decision (the global
 * session has never had one — see ai-augmented-app technical-plan.md Module 6): this panel is the
 * same global session in a different container, not a new surface with new behavior.
 */
export function GlobalAgentPanel({ open, onClose }: GlobalAgentPanelProps) {
  const [input, setInput] = useState("");
  const [showClearConfirm, setShowClearConfirm] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);

  const { messages, send, isStreaming, clear, isClearPending, status, pendingApprovals } =
    useGlobalAgentConversation();

  useEffect(() => {
    if (scrollRef.current) scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
  }, [messages, isStreaming]);

  const handleSend = () => {
    if (!input.trim() || isStreaming) return;
    send(input);
    setInput("");
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleClear = () => {
    clear(() => setShowClearConfirm(false));
  };

  return (
    <ResizablePanel
      visible={open}
      position="right"
      defaultWidth={384}
      minWidth={280}
      maxWidth={600}
      storageKey="global-agent-panel"
      showHeader={false}
      data-testid="global-agent-panel"
    >
      <div className="flex h-full flex-col overflow-hidden">
        <div className="flex items-center justify-between border-b px-4 py-3">
        <div>
          <h2 className="text-sm font-semibold" data-testid="global-agent-panel-title">AI Agent</h2>
          <p className="text-xs text-muted-foreground" data-testid="global-agent-panel-history-count">
            {status.data?.historyCount ?? 0} messages in history
            {status.data && status.data.estimatedTokens > 0 && (
              <> · ~{status.data.estimatedTokens.toLocaleString()} tokens</>
            )}
            <ContextUsageIndicator
              percent={status.data?.contextUsagePercent ?? 0}
              warningAt={status.data?.contextUsageWarningPercent ?? 75}
            />
          </p>
        </div>
        <div className="flex items-center gap-2">
          {showClearConfirm ? (
            <>
              <button
                onClick={handleClear}
                disabled={isClearPending}
                className="rounded-md bg-destructive px-2 py-1 text-xs text-destructive-foreground hover:bg-destructive/90 disabled:opacity-50"
                data-testid="global-agent-panel-clear-confirm"
              >
                Yes, clear
              </button>
              <button
                onClick={() => setShowClearConfirm(false)}
                className="rounded-md border px-2 py-1 text-xs hover:bg-accent"
                data-testid="global-agent-panel-clear-cancel"
              >
                Cancel
              </button>
            </>
          ) : (
            <button
              onClick={() => setShowClearConfirm(true)}
              disabled={messages.length === 0}
              className="rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
              data-testid="global-agent-panel-clear"
            >
              Clear
            </button>
          )}
          <button
            onClick={onClose}
            className="rounded-md px-2 py-1 text-sm hover:bg-accent"
            data-testid="global-agent-panel-close"
          >
            ✕
          </button>
        </div>
      </div>

      {pendingApprovals.data && pendingApprovals.data.length > 0 && (
        <div className="space-y-2 border-b px-4 py-3" data-testid="global-agent-panel-pending-actions">
          {pendingApprovals.data.map((action) => (
            <PendingActionCard key={action.id} action={action} />
          ))}
        </div>
      )}

      <div ref={scrollRef} className="flex-1 overflow-auto px-4 py-3 space-y-3" data-testid="global-agent-panel-messages">
        {messages.length === 0 && (
          <p className="text-sm text-muted-foreground" data-testid="global-agent-panel-empty">
            Ask about your Kubernetes clusters, Service Bus queues, Redis caches, Storage accounts, and more.
          </p>
        )}
        {messages.map((msg) => (
          <div key={msg.id} className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}>
            <div
              className={`max-w-[90%] rounded-lg px-3 py-2 text-sm ${
                msg.role === "user"
                  ? "bg-primary text-primary-foreground"
                  : msg.error
                    ? "bg-destructive/10 border border-destructive/30"
                    : "bg-muted"
              }`}
            >
              {msg.role === "assistant" ? (
                <AgentMarkdown
                  content={msg.content}
                  className="prose prose-sm dark:prose-invert max-w-none [&_p]:my-1"
                />
              ) : (
                <div className="whitespace-pre-wrap">{msg.content}</div>
              )}
              {msg.role === "assistant" && msg.steps && <AgentReasoningTrace steps={msg.steps} />}
              {msg.role === "assistant" && msg.summarized && <AgentSummarizedNotice />}
            </div>
          </div>
        ))}
        {isStreaming && messages[messages.length - 1]?.content === "" && (
          <div className="flex justify-start" data-testid="global-agent-panel-loading">
            <div className="rounded-lg bg-muted px-3 py-2 text-sm text-muted-foreground">Thinking…</div>
          </div>
        )}
      </div>

      <div className="border-t px-4 py-3">
        <div className="flex items-end gap-2">
          <textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Ask the AI agent..."
            rows={2}
            className="flex-1 resize-none rounded-md border bg-background px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            disabled={isStreaming}
            data-testid="global-agent-panel-input"
          />
          <button
            onClick={handleSend}
            disabled={!input.trim() || isStreaming}
            className="rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
            data-testid="global-agent-panel-send"
          >
            Send
          </button>
        </div>
      </div>
      </div>
    </ResizablePanel>
  );
}
