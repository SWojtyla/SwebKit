import { useState, useEffect, useRef } from "react";
import { Radio, Send, Plus, Trash2 } from "lucide-react";

interface Props {
  cacheId: string;
}

interface PubSubMessage {
  id: number;
  channel: string;
  message: string;
  timestamp: number;
}

export function PubSubPanel({ cacheId }: Props) {
  const [channels, setChannels] = useState<string[]>([]);
  const [subscribedChannels, setSubscribedChannels] = useState<Set<string>>(new Set());
  const [messages, setMessages] = useState<PubSubMessage[]>([]);
  const [newChannel, setNewChannel] = useState("");
  const [publishChannel, setPublishChannel] = useState("");
  const [publishMessage, setPublishMessage] = useState("");
  const [publishStatus, setPublishStatus] = useState<string | null>(null);
  const msgIdRef = useRef(0);

  const addChannel = () => {
    if (newChannel.trim() && !channels.includes(newChannel.trim())) {
      setChannels([...channels, newChannel.trim()]);
      setNewChannel("");
    }
  };

  const toggleSubscribe = (channel: string) => {
    setSubscribedChannels((prev) => {
      const next = new Set(prev);
      if (next.has(channel)) {
        next.delete(channel);
        setMessages((msgs) => msgs.filter((m) => m.channel !== channel));
      } else {
        next.add(channel);
      }
      return next;
    });
  };

  const publish = async () => {
    if (!publishChannel.trim() || !publishMessage.trim()) return;
    try {
      const res = await fetch(`/api/redis/${cacheId}/pubsub/publish`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ channel: publishChannel, message: publishMessage }),
      });
      if (res.ok) {
        setPublishStatus("Published successfully");
        setPublishMessage("");
      } else {
        setPublishStatus("Publish failed");
      }
    } catch {
      setPublishStatus("Publish endpoint not available");
    }
    setTimeout(() => setPublishStatus(null), 3000);
  };

  useEffect(() => {
    if (subscribedChannels.size === 0) return;
    const eventSource = new EventSource(`/api/redis/${cacheId}/pubsub/subscribe?channels=${Array.from(subscribedChannels).join(",")}`);
    eventSource.onmessage = (e) => {
      try {
        const data = JSON.parse(e.data);
        const id = ++msgIdRef.current;
        setMessages((prev) => [{ id, channel: data.channel, message: data.message, timestamp: Date.now() }, ...prev].slice(0, 100));
      } catch {}
    };
    return () => eventSource.close();
  }, [subscribedChannels, cacheId]);

  return (
    <div className="space-y-6" data-testid="redis-pubsub-panel">
      <div className="flex items-center gap-2">
        <Radio className="h-5 w-5 text-muted-foreground" />
        <h2 className="text-lg font-semibold">Pub/Sub</h2>
      </div>

      {/* Channel management */}
      <div>
        <h3 className="mb-2 text-sm font-semibold">Channels</h3>
        <div className="flex items-center gap-2 mb-2">
          <input
            type="text"
            value={newChannel}
            onChange={(e) => setNewChannel(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && addChannel()}
            placeholder="Channel name..."
            className="flex-1 rounded border bg-card px-3 py-1.5 text-sm"
            data-testid="redis-pubsub-channel-input"
          />
          <button
            onClick={addChannel}
            className="flex items-center gap-1 rounded border px-3 py-1.5 text-sm hover:bg-accent"
            data-testid="redis-pubsub-add-channel"
          >
            <Plus className="h-3.5 w-3.5" /> Add
          </button>
        </div>
        <div className="space-y-1">
          {channels.map((ch) => (
            <div key={ch} className="flex items-center gap-2 rounded border px-3 py-1.5" data-testid={`redis-pubsub-channel-${ch}`}>
              <button
                onClick={() => toggleSubscribe(ch)}
                className={`rounded px-2 py-0.5 text-xs ${subscribedChannels.has(ch) ? "bg-primary text-primary-foreground" : "border hover:bg-accent"}`}
                data-testid={`redis-pubsub-subscribe-${ch}`}
              >
                {subscribedChannels.has(ch) ? "Subscribed" : "Subscribe"}
              </button>
              <span className="flex-1 font-mono text-sm">{ch}</span>
              <button
                onClick={() => {
                  setChannels(channels.filter((c) => c !== ch));
                  setSubscribedChannels((prev) => { const next = new Set(prev); next.delete(ch); return next; });
                }}
                className="text-destructive hover:opacity-80"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </div>
          ))}
          {channels.length === 0 && (
            <p className="text-sm text-muted-foreground">No channels added yet</p>
          )}
        </div>
      </div>

      {/* Publish */}
      <div>
        <h3 className="mb-2 text-sm font-semibold">Publish Message</h3>
        <div className="space-y-2">
          <input
            type="text"
            value={publishChannel}
            onChange={(e) => setPublishChannel(e.target.value)}
            placeholder="Channel name..."
            className="w-full rounded border bg-card px-3 py-1.5 text-sm"
            data-testid="redis-pubsub-publish-channel"
          />
          <div className="flex gap-2">
            <input
              type="text"
              value={publishMessage}
              onChange={(e) => setPublishMessage(e.target.value)}
              placeholder="Message..."
              className="flex-1 rounded border bg-card px-3 py-1.5 text-sm"
              data-testid="redis-pubsub-publish-message"
            />
            <button
              onClick={publish}
              disabled={!publishChannel.trim() || !publishMessage.trim()}
              className="flex items-center gap-1 rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground disabled:opacity-50"
              data-testid="redis-pubsub-publish-btn"
            >
              <Send className="h-3.5 w-3.5" /> Publish
            </button>
          </div>
          {publishStatus && (
            <p className="text-xs text-muted-foreground" data-testid="redis-pubsub-publish-status">{publishStatus}</p>
          )}
        </div>
      </div>

      {/* Messages */}
      <div>
        <h3 className="mb-2 text-sm font-semibold">Received Messages</h3>
        <div className="max-h-96 overflow-auto rounded border" data-testid="redis-pubsub-messages">
          {messages.length === 0 ? (
            <div className="p-4 text-sm text-muted-foreground">No messages received</div>
          ) : (
            messages.map((msg) => (
              <div key={msg.id} className="border-b px-3 py-2 text-xs last:border-0" data-testid={`redis-pubsub-msg-${msg.id}`}>
                <span className="text-muted-foreground">{new Date(msg.timestamp).toLocaleTimeString()}</span>
                <span className="ml-2 font-mono text-primary">{msg.channel}:</span>
                <span className="ml-1 font-mono">{msg.message}</span>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}
