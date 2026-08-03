import { useEffect, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import { useContextualAgent } from "@/lib/hooks/useContextualAgent";
import { usePendingApprovals } from "@/lib/hooks";
import { PendingActionCard } from "./PendingActionCard";
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
  const { mode, setMode, chat, status, sendMessage } = useContextualAgent(featureArea, selection);
  const pendingApprovals = usePendingApprovals();

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
              ? { ...m, content: reply.text, elapsedMs: reply.elapsedMs, error: reply.error }
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
      <div
        className="flex h-full w-full max-w-md flex-col border-l bg-card shadow-xl"
        data-testid="contextual-assistant-panel"
      >
        <div className="flex items-center justify-between border-b px-4 py-3">
          <div>
            <h2 className="text-sm font-semibold" data-testid="contextual-assistant-title">
              Ask AI — {title}
            </h2>
            {status.data && status.data.estimatedTokens > 0 && (
              <p className="text-xs text-muted-foreground" data-testid="contextual-assistant-token-estimate">
                ~{status.data.estimatedTokens.toLocaleString()} tokens in this conversation
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
                  <div className="prose prose-sm dark:prose-invert max-w-none [&_p]:my-1 [&_pre]:overflow-x-auto">
                    <ReactMarkdown>{msg.content}</ReactMarkdown>
                  </div>
                ) : (
                  <div className="whitespace-pre-wrap">{msg.content}</div>
                )}
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
    </div>
  );
}
