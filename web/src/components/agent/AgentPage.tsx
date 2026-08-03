import { useState, useRef, useEffect } from "react";
import ReactMarkdown from "react-markdown";
import { useGlobalAgentConversation } from "@/lib/hooks/useGlobalAgentConversation";
import { PendingActionCard } from "./PendingActionCard";
import { AgentReasoningTrace } from "./AgentReasoningTrace";
import { AgentSummarizedNotice } from "./AgentSummarizedNotice";
import { ContextUsageIndicator } from "./ContextUsageIndicator";

export function AgentPage() {
  const [input, setInput] = useState("");
  const [showClearConfirm, setShowClearConfirm] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);

  const { messages, send, isStreaming, clear, isClearPending, status, pendingApprovals } =
    useGlobalAgentConversation();

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
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
    <div className="flex h-full flex-col" data-testid="agent-page">
      {/* Header */}
      <div className="flex items-center justify-between border-b px-6 py-3">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-bold" data-testid="agent-title">AI Agent</h1>
          <span className="text-xs text-muted-foreground" data-testid="agent-history-count">
            {status.data?.historyCount ?? 0} messages in history
            {status.data && status.data.estimatedTokens > 0 && (
              <> · ~{status.data.estimatedTokens.toLocaleString()} tokens</>
            )}
            <ContextUsageIndicator percent={status.data?.contextUsagePercent ?? 0} />
          </span>
        </div>
        {showClearConfirm ? (
          <div className="flex items-center gap-2">
            <span className="text-sm text-muted-foreground">Clear conversation?</span>
            <button
              data-testid="agent-clear-confirm"
              onClick={handleClear}
              disabled={isClearPending}
              className="rounded-md bg-destructive px-3 py-1 text-sm text-destructive-foreground hover:bg-destructive/90 disabled:opacity-50"
            >
              Yes, clear
            </button>
            <button
              data-testid="agent-clear-cancel"
              onClick={() => setShowClearConfirm(false)}
              className="rounded-md border px-3 py-1 text-sm hover:bg-accent"
            >
              Cancel
            </button>
          </div>
        ) : (
          <button
            data-testid="agent-clear"
            onClick={() => setShowClearConfirm(true)}
            disabled={messages.length === 0}
            className="rounded-md border px-3 py-1 text-sm hover:bg-accent disabled:opacity-50"
          >
            Clear
          </button>
        )}
      </div>

      {/* Pending actions awaiting confirmation ("Ask & do" proposals) */}
      {pendingApprovals.data && pendingApprovals.data.length > 0 && (
        <div className="space-y-2 border-b px-6 py-3" data-testid="pending-actions-list">
          {pendingApprovals.data.map((action) => (
            <PendingActionCard key={action.id} action={action} />
          ))}
        </div>
      )}

      {/* Chat messages */}
      <div
        ref={scrollRef}
        className="flex-1 overflow-auto px-6 py-4 space-y-4"
        data-testid="agent-messages"
      >
        {messages.length === 0 && (
          <div className="flex h-full items-center justify-center text-center" data-testid="agent-empty">
            <div className="space-y-2">
              <p className="text-lg font-medium">No conversation yet</p>
              <p className="text-sm text-muted-foreground">
                Ask about your Kubernetes clusters, Service Bus queues, Redis caches, Storage accounts, and more.
              </p>
            </div>
          </div>
        )}

        {messages.map((msg) => (
          <div
            key={msg.id}
            data-testid={`agent-message-${msg.id}`}
            className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}
          >
            <div
              className={`max-w-[80%] rounded-lg px-4 py-2 ${
                msg.role === "user"
                  ? "bg-primary text-primary-foreground"
                  : msg.error
                    ? "bg-destructive/10 border border-destructive/30"
                    : "bg-muted"
              }`}
            >
              {msg.role === "assistant" ? (
                <div className="prose prose-sm dark:prose-invert max-w-none text-sm [&_p]:my-1 [&_pre]:overflow-x-auto">
                  <ReactMarkdown>{msg.content}</ReactMarkdown>
                </div>
              ) : (
                <div className="whitespace-pre-wrap text-sm">{msg.content}</div>
              )}
              {msg.role === "assistant" && msg.elapsedMs != null && (
                <div className="mt-1 text-xs text-muted-foreground">
                  {msg.elapsedMs}ms
                </div>
              )}
              {msg.role === "assistant" && msg.steps && <AgentReasoningTrace steps={msg.steps} />}
              {msg.role === "assistant" && msg.summarized && <AgentSummarizedNotice />}
            </div>
          </div>
        ))}

        {isStreaming && messages[messages.length - 1]?.content === "" && (
          <div className="flex justify-start" data-testid="agent-loading">
            <div className="rounded-lg bg-muted px-4 py-2">
              <div className="flex items-center gap-2 text-sm text-muted-foreground">
                <span className="inline-block h-2 w-2 animate-pulse rounded-full bg-current"></span>
                Thinking...
              </div>
            </div>
          </div>
        )}
      </div>

      {/* Input */}
      <div className="border-t px-6 py-3">
        <div className="flex items-end gap-2">
          <textarea
            data-testid="agent-input"
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Ask the AI agent..."
            rows={2}
            className="flex-1 resize-none rounded-md border bg-card px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            disabled={isStreaming}
          />
          <button
            data-testid="agent-send"
            onClick={handleSend}
            disabled={!input.trim() || isStreaming}
            className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
          >
            Send
          </button>
        </div>
        <p className="mt-1 text-xs text-muted-foreground">
          Press Enter to send, Shift+Enter for new line
        </p>
      </div>
    </div>
  );
}
