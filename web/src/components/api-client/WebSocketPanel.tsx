import { useState, useRef, useEffect } from "react";
import { Send, Plus, Trash2, Play, Square } from "lucide-react";
import type { HttpRequestEntry, WebSocketSavedMessage } from "@/lib/types";

interface WebSocketPanelProps {
  request: HttpRequestEntry;
  onChange: (request: HttpRequestEntry) => void;
}

interface WsMessage {
  id: string;
  direction: "sent" | "received";
  content: string;
  timestamp: string;
}

export function WebSocketPanel({ request, onChange }: WebSocketPanelProps) {
  const [connected, setConnected] = useState(false);
  const [messages, setMessages] = useState<WsMessage[]>([]);
  const [inputText, setInputText] = useState("");
  const [subProtocol, setSubProtocol] = useState(request.wsSubProtocol ?? "");
  const wsRef = useRef<WebSocket | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  useEffect(() => {
    return () => {
      wsRef.current?.close();
    };
  }, []);

  const connect = () => {
    if (!request.url) return;
    try {
      const ws = subProtocol
        ? new WebSocket(request.url, subProtocol)
        : new WebSocket(request.url);
      wsRef.current = ws;

      ws.onopen = () => setConnected(true);
      ws.onclose = () => setConnected(false);
      ws.onerror = () => setConnected(false);
      ws.onmessage = (event) => {
        setMessages((prev) => [
          ...prev,
          {
            id: crypto.randomUUID(),
            direction: "received",
            content: typeof event.data === "string" ? event.data : "[binary data]",
            timestamp: new Date().toISOString(),
          },
        ]);
      };
    } catch (err) {
      setMessages((prev) => [
        ...prev,
        {
          id: crypto.randomUUID(),
          direction: "received",
          content: `Connection error: ${err instanceof Error ? err.message : "Unknown"}`,
          timestamp: new Date().toISOString(),
        },
      ]);
    }
  };

  const disconnect = () => {
    wsRef.current?.close();
    wsRef.current = null;
    setConnected(false);
  };

  const sendMessage = () => {
    if (!wsRef.current || !inputText.trim()) return;
    wsRef.current.send(inputText);
    setMessages((prev) => [
      ...prev,
      {
        id: crypto.randomUUID(),
        direction: "sent",
        content: inputText,
        timestamp: new Date().toISOString(),
      },
    ]);
    setInputText("");
  };

  const setWsSubProtocol = (proto: string) => {
    setSubProtocol(proto);
    onChange({ ...request, wsSubProtocol: proto || null });
  };

  const addSavedMessage = () => {
    const newMsg: WebSocketSavedMessage = {
      id: crypto.randomUUID(),
      name: "New Message",
      content: "",
      frameType: "Text",
    };
    onChange({ ...request, savedMessages: [...request.savedMessages, newMsg] });
  };

  const updateSavedMessage = (index: number, patch: Partial<WebSocketSavedMessage>) => {
    const savedMessages = request.savedMessages.map((m, i) =>
      i === index ? { ...m, ...patch } : m,
    );
    onChange({ ...request, savedMessages });
  };

  const removeSavedMessage = (index: number) => {
    onChange({ ...request, savedMessages: request.savedMessages.filter((_, i) => i !== index) });
  };

  const sendSavedMessage = (msg: WebSocketSavedMessage) => {
    if (!wsRef.current) return;
    wsRef.current.send(msg.content);
    setMessages((prev) => [
      ...prev,
      {
        id: crypto.randomUUID(),
        direction: "sent",
        content: msg.content,
        timestamp: new Date().toISOString(),
      },
    ]);
  };

  const clearMessages = () => setMessages([]);

  return (
    <div className="flex h-full flex-col gap-3" data-testid="websocket-panel">
      {/* Connection controls */}
      <div className="flex items-center gap-2 border-b pb-2">
        {!connected ? (
          <button
            onClick={connect}
            disabled={!request.url.trim()}
            className="flex items-center gap-1 rounded bg-green-500 px-3 py-1 text-xs text-white hover:opacity-90 disabled:opacity-50"
            data-testid="ws-connect-button"
          >
            <Play className="h-3 w-3" /> Connect
          </button>
        ) : (
          <button
            onClick={disconnect}
            className="flex items-center gap-1 rounded bg-red-500 px-3 py-1 text-xs text-white hover:opacity-90"
            data-testid="ws-disconnect-button"
          >
            <Square className="h-3 w-3" /> Disconnect
          </button>
        )}
        <span className={`text-xs ${connected ? "text-green-500" : "text-muted-foreground"}`} data-testid="ws-status">
          {connected ? "Connected" : "Disconnected"}
        </span>
        <input
          type="text"
          value={subProtocol}
          onChange={(e) => setWsSubProtocol(e.target.value)}
          placeholder="Sub-protocol (optional)"
          className="flex-1 rounded border bg-background px-2 py-1 text-xs"
          data-testid="ws-subprotocol-input"
        />
      </div>

      {/* Messages log */}
      <div className="flex-1 overflow-auto rounded border bg-background p-2" data-testid="ws-messages">
        {messages.length === 0 && (
          <div className="text-xs text-muted-foreground py-4 text-center">No messages yet.</div>
        )}
        {messages.map((msg) => (
          <div
            key={msg.id}
            className={`mb-1 rounded p-2 text-xs ${
              msg.direction === "sent"
                ? "bg-blue-500/10 border-l-2 border-blue-500"
                : "bg-green-500/10 border-l-2 border-green-500"
            }`}
            data-testid={`ws-message-${msg.id}`}
          >
            <div className="flex items-center justify-between">
              <span className="font-semibold">
                {msg.direction === "sent" ? "↑ Sent" : "↓ Received"}
              </span>
              <span className="text-muted-foreground">
                {new Date(msg.timestamp).toLocaleTimeString()}
              </span>
            </div>
            <pre className="mt-1 whitespace-pre-wrap break-all font-mono">{msg.content}</pre>
          </div>
        ))}
        <div ref={messagesEndRef} />
      </div>

      {/* Input */}
      <div className="flex gap-2">
        <textarea
          value={inputText}
          onChange={(e) => setInputText(e.target.value)}
          placeholder="Type message to send..."
          className="flex-1 rounded border bg-background px-2 py-1 text-sm font-mono"
          rows={2}
          data-testid="ws-input"
        />
        <button
          onClick={sendMessage}
          disabled={!connected || !inputText.trim()}
          className="flex items-center gap-1 rounded bg-primary px-3 py-1 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
          data-testid="ws-send-button"
        >
          <Send className="h-3 w-3" /> Send
        </button>
      </div>

      {/* Saved messages */}
      <div className="border-t pt-2">
        <div className="mb-1 flex items-center justify-between">
          <span className="text-xs font-medium text-muted-foreground">Saved Messages</span>
          <div className="flex gap-1">
            <button
              onClick={addSavedMessage}
              className="flex items-center gap-1 text-xs text-primary hover:underline"
              data-testid="ws-add-saved"
            >
              <Plus className="h-3 w-3" /> Add
            </button>
            <button
              onClick={clearMessages}
              className="text-xs text-muted-foreground hover:text-foreground"
              data-testid="ws-clear-messages"
            >
              Clear log
            </button>
          </div>
        </div>
        {request.savedMessages.map((msg, i) => (
          <div key={msg.id} className="mb-1 flex items-center gap-1" data-testid={`ws-saved-${i}`}>
            <input
              type="text"
              value={msg.name}
              onChange={(e) => updateSavedMessage(i, { name: e.target.value })}
              placeholder="Name"
              className="w-24 rounded border bg-background px-1 py-0.5 text-xs"
            />
            <input
              type="text"
              value={msg.content}
              onChange={(e) => updateSavedMessage(i, { content: e.target.value })}
              placeholder="Message content"
              className="flex-1 rounded border bg-background px-1 py-0.5 text-xs font-mono"
            />
            <button
              onClick={() => sendSavedMessage(msg)}
              disabled={!connected}
              className="rounded p-1 text-primary disabled:opacity-50"
              data-testid={`ws-send-saved-${i}`}
            >
              <Send className="h-3 w-3" />
            </button>
            <button
              onClick={() => removeSavedMessage(i)}
              className="rounded p-1 text-destructive"
              data-testid={`ws-remove-saved-${i}`}
            >
              <Trash2 className="h-3 w-3" />
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}
