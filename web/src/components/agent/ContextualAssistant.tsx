import { useEffect, useRef, useState } from "react";
import { useContextualAgent } from "@/lib/hooks/useContextualAgent";
import { AgentMarkdown } from "./AgentMarkdown";
import { usePendingApprovals, useUserSettings } from "@/lib/hooks";
import { PendingActionCard } from "./PendingActionCard";
import { ResizablePanel } from "@/components/ui/ResizablePanel";
import { AgentReasoningTrace } from "./AgentReasoningTrace";
import { AgentSummarizedNotice } from "./AgentSummarizedNotice";
import { ContextUsageIndicator } from "./ContextUsageIndicator";
import type { ChatMessage } from "@/lib/types";

let msgIdCounter = 0;
function nextMsgId() {
  return `ctx-msg-${++msgIdCounter}`;
}

interface ContextualAssistantProps {
  /** Must match a backend FeatureArea enum member name (e.g. "Aks", "Redis"). */
  featureArea: string;
  /** Human-readable label for the panel header, e.g. "pod api-7c9f". */
  title: string;
  selection?: Record<string, string>;
  onClose: () => void;
}

/**
 * Docked contextual assistant panel — the "Ask AI" surface embedded in a feature page (AKS pod
 * detail, Redis key detail, etc.), as opposed to the standalone /agent page. Slides in from the
 * right so the user's place in the underlying page isn't lost (technical-plan.md Module 6).
 */
export function ContextualAssistant({ featureArea, title, selection, onClose }: ContextualAssistantProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const scrollRef = useRef<HTMLDivElement>(null);
  const { mode, setMode, scope, setScope, chat, status, sendMessage } = useContextualAgent(featureArea, selection);
  const pendingApprovals = usePendingApprovals();
  const { data: userSettings } = useUserSettings();

  const activeProfile = userSettings?.agent.profiles.find(
    (p) => p.id === userSettings.agent.activeProfileId,
  );
  const capability = activeProfile?.capability ?? "Unknown";

  const workspaceScopeDisabled = capability === "ChatOnly" || capability === "Unknown";
  let workspaceScopeReason = "";
  if (capability === "ChatOnly") {
    workspaceScopeReason = "This model doesn't support tool calling — workspace search is unavailable.";
  } else if (capability === "Unknown") {
    workspaceScopeReason = "Run Test Connection first to check whether this profile supports workspace search.";
  }

  useEffect(() => {
    if (workspaceScopeDisabled && scope === "workspace") setScope("feature");
  }, [workspaceScopeDisabled, scope, setScope]);

  useEffect(() => {
    if (scrollRef.current) scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
  }, [messages, chat.isStreaming]);

  const handleSend = () => {
    const text = input.trim();
    if (!text || chat.isStreaming) return;

    const assistantId = nextMsgId();
    setMessages((prev) => [
      ...prev,
      { id: nextMsgId(), role: "user", content: text },
      { id: assistantId, role: "assistant", content: "" },
    ]);
    setInput("");

    sendMessage(text, {
      onToken: (token) => {
        setMessages((prev) =>
          prev.map((m) => (m.id === assistantId ? { ...m, content: m.content + token } : m)),
        );
      },
      onSuccess: (reply) => {
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantId
              ? {
                  ...m,
                  content: reply.text,
                  elapsedMs: reply.elapsedMs,
                  error: reply.error,
                  steps: reply.steps,
                  summarized: reply.summarized,
                }
              : m,
          ),
        );
      },
      onError: (err) => {
        setMessages((prev) =>
          prev.map((m) => (m.id === assistantId ? { ...m, content: `Error: ${err.message}`, error: true } : m)),
        );
      },
    });
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex justify-end" data-testid="contextual-assistant-overlay">
      <div className="flex-1" onClick={onClose} />
      <ResizablePanel
        visible={true}
        position="right"
        defaultWidth={384}
        minWidth={280}
        maxWidth={600}
        storageKey="contextual-assistant-panel"
        showHeader={false}
        className="h-full shadow-xl"
        data-testid="contextual-assistant-panel"
      >
        <div className="flex h-full flex-col overflow-hidden">
          <div className="flex items-center justify-between border-b px-4 py-3">
          <div>
            <h2 className="text-sm font-semibold" data-testid="contextual-assistant-title">
              Ask AI — {title}
            </h2>
            {status.data && status.data.estimatedTokens > 0 && (
              <p className="text-xs text-muted-foreground" data-testid="contextual-assistant-token-estimate">
                ~{status.data.estimatedTokens.toLocaleString()} tokens in this conversation
                <ContextUsageIndicator
                  percent={status.data.contextUsagePercent}
                  warningAt={status.data.contextUsageWarningPercent ?? 75}
                />
              </p>
            )}
          </div>
          <button
            onClick={onClose}
            className="rounded-md px-2 py-1 text-sm hover:bg-accent"
            data-testid="contextual-assistant-close"
          >
            ✕
          </button>
        </div>

        <div className="flex items-center gap-1 border-b px-4 py-2" role="radiogroup" aria-label="Assistant mode">
          <button
            role="radio"
            aria-checked={mode === "ask"}
            onClick={() => setMode("ask")}
            className={`rounded-md px-2 py-1 text-xs ${mode === "ask" ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
            data-testid="contextual-assistant-mode-ask"
          >
            Ask
          </button>
          <button
            role="radio"
            aria-checked={mode === "ask_and_do"}
            onClick={() => setMode("ask_and_do")}
            className={`rounded-md px-2 py-1 text-xs ${mode === "ask_and_do" ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
            data-testid="contextual-assistant-mode-ask-and-do"
          >
            Ask &amp; do
          </button>
          <label
            className={`ml-auto flex items-center gap-1.5 text-xs ${workspaceScopeDisabled ? "text-muted-foreground/60 cursor-not-allowed" : "text-muted-foreground"}`}
            title={
              workspaceScopeDisabled
                ? workspaceScopeReason
                : "Widen tool access to every configured area for this turn, not just this page's"
            }
          >
            <input
              type="checkbox"
              checked={scope === "workspace"}
              onChange={(e) => setScope(e.target.checked ? "workspace" : "feature")}
              disabled={workspaceScopeDisabled}
              data-testid="contextual-assistant-scope-workspace"
            />
            Search across my whole workspace
          </label>
          {workspaceScopeDisabled && (
            <span
              className="max-w-[140px] truncate text-xs text-destructive"
              data-testid="contextual-assistant-scope-reason"
              title={workspaceScopeReason}
            >
              {workspaceScopeReason}
            </span>
          )}
        </div>

        {pendingApprovals.data && pendingApprovals.data.length > 0 && (
          <div className="space-y-2 border-b px-4 py-3" data-testid="contextual-assistant-pending-actions">
            {pendingApprovals.data.map((action) => (
              <PendingActionCard key={action.id} action={action} />
            ))}
          </div>
        )}

        <div ref={scrollRef} className="flex-1 overflow-auto px-4 py-3 space-y-3" data-testid="contextual-assistant-messages">
          {messages.length === 0 && (
            <p className="text-sm text-muted-foreground" data-testid="contextual-assistant-empty">
              Ask a question about {title}.
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
          {chat.isStreaming && messages[messages.length - 1]?.content === "" && (
            <div className="flex justify-start" data-testid="contextual-assistant-loading">
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
              placeholder={`Ask about ${title}...`}
              rows={2}
              className="flex-1 resize-none rounded-md border bg-background px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              disabled={chat.isStreaming}
              data-testid="contextual-assistant-input"
            />
            <button
              onClick={handleSend}
              disabled={!input.trim() || chat.isStreaming}
              className="rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
              data-testid="contextual-assistant-send"
            >
              Send
            </button>
          </div>
        </div>
        </div>
      </ResizablePanel>
    </div>
  );
}
