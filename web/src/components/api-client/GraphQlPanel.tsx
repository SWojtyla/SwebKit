import { useState, useEffect, useRef } from "react";
import { Wand2, Play, Square, Radio } from "lucide-react";
import type { HttpRequestEntry } from "@/lib/types";

interface GraphQlPanelProps {
  request: HttpRequestEntry;
  onChange: (request: HttpRequestEntry) => void;
}

function tryPrettyPrintJson(content: string): string {
  try {
    return JSON.stringify(JSON.parse(content), null, 2);
  } catch {
    return content;
  }
}

export function GraphQlPanel({ request, onChange }: GraphQlPanelProps) {
  const [subscriptionMessages, setSubscriptionMessages] = useState<{ id: number; data: string; timestamp: number }[]>([]);
  const [subscribed, setSubscribed] = useState(false);
  const wsRef = useRef<WebSocket | null>(null);
  const msgIdRef = useRef(0);

  const setQuery = (graphQlQuery: string) => onChange({ ...request, graphQlQuery });
  const setVariables = (graphQlVariables: string) => onChange({ ...request, graphQlVariables });
  const setOperation = (graphQlSelectedOperation: string) =>
    onChange({ ...request, graphQlSelectedOperation: graphQlSelectedOperation || null });

  const prettyPrintVariables = () => {
    if (request.graphQlVariables) {
      setVariables(tryPrettyPrintJson(request.graphQlVariables));
    }
  };

  // Extract operation names from query (simple regex)
  const operations = request.graphQlQuery
    ? [...request.graphQlQuery.matchAll(/(?:query|mutation|subscription)\s+(\w+)/g)].map((m) => m[1])
    : [];

  const isSubscription = request.graphQlQuery?.trim().toLowerCase().startsWith("subscription");

  const startSubscription = () => {
    const wsUrl = request.url.replace(/^http/, "ws");
    try {
      const ws = new WebSocket(wsUrl);
      wsRef.current = ws;
      ws.onopen = () => {
        ws.send(JSON.stringify({
          query: request.graphQlQuery,
          variables: request.graphQlVariables ? JSON.parse(request.graphQlVariables) : {},
        }));
        setSubscribed(true);
      };
      ws.onmessage = (e) => {
        const id = ++msgIdRef.current;
        setSubscriptionMessages((prev) => [{ id, data: e.data, timestamp: Date.now() }, ...prev].slice(0, 100));
      };
      ws.onerror = () => { setSubscribed(false); };
      ws.onclose = () => { setSubscribed(false); };
    } catch {}
  };

  const stopSubscription = () => {
    wsRef.current?.close();
    wsRef.current = null;
    setSubscribed(false);
  };

  useEffect(() => {
    return () => { wsRef.current?.close(); };
  }, []);

  return (
    <div className="space-y-3" data-testid="graphql-panel">
      {/* Query editor */}
      <div>
        <div className="mb-1 flex items-center justify-between">
          <label className="text-xs font-medium text-muted-foreground">Query</label>
        </div>
        <textarea
          data-testid="graphql-query-input"
          value={request.graphQlQuery ?? ""}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="query MyQuery {\n  field\n}"
          className="h-48 w-full resize-y rounded border bg-background px-3 py-2 font-mono text-sm"
          spellCheck={false}
        />
      </div>

      {/* Operation selector */}
      {operations.length > 1 && (
        <div>
          <label className="mb-1 block text-xs font-medium text-muted-foreground">Operation</label>
          <select
            data-testid="graphql-operation-select"
            value={request.graphQlSelectedOperation ?? ""}
            onChange={(e) => setOperation(e.target.value)}
            className="w-full rounded border bg-background px-2 py-1.5 text-sm"
          >
            <option value="">Auto (first)</option>
            {operations.map((op) => (
              <option key={op} value={op}>{op}</option>
            ))}
          </select>
        </div>
      )}

      {/* Variables editor */}
      <div>
        <div className="mb-1 flex items-center justify-between">
          <label className="text-xs font-medium text-muted-foreground">Variables (JSON)</label>
          <button
            onClick={prettyPrintVariables}
            className="flex items-center gap-1 text-xs text-primary hover:underline"
            data-testid="graphql-pretty-variables"
          >
            <Wand2 className="h-3 w-3" /> Format
          </button>
        </div>
        <textarea
          data-testid="graphql-variables-input"
          value={request.graphQlVariables ?? ""}
          onChange={(e) => setVariables(e.target.value)}
          placeholder='{\n  "key": "value"\n}'
          className="h-32 w-full resize-y rounded border bg-background px-3 py-2 font-mono text-sm"
          spellCheck={false}
        />
      </div>

      {/* Subscription support */}
      {isSubscription && (
        <div data-testid="graphql-subscription-panel">
          <div className="mb-2 flex items-center gap-2">
            <Radio className="h-4 w-4 text-muted-foreground" />
            <span className="text-sm font-medium">Subscription</span>
            {!subscribed ? (
              <button
                onClick={startSubscription}
                className="flex items-center gap-1 rounded bg-primary px-2 py-1 text-xs text-primary-foreground"
                data-testid="graphql-subscribe"
              >
                <Play className="h-3 w-3" /> Subscribe
              </button>
            ) : (
              <button
                onClick={stopSubscription}
                className="flex items-center gap-1 rounded bg-destructive px-2 py-1 text-xs text-destructive-foreground"
                data-testid="graphql-unsubscribe"
              >
                <Square className="h-3 w-3" /> Stop
              </button>
            )}
          </div>
          <div className="max-h-48 overflow-auto rounded border bg-card p-2" data-testid="graphql-subscription-messages">
            {subscriptionMessages.length === 0 ? (
              <span className="text-xs text-muted-foreground">No messages received yet</span>
            ) : (
              subscriptionMessages.map((msg) => (
                <div key={msg.id} className="border-b py-1 text-xs last:border-0" data-testid={`graphql-sub-msg-${msg.id}`}>
                  <span className="text-muted-foreground">{new Date(msg.timestamp).toLocaleTimeString()}: </span>
                  <span className="font-mono">{msg.data}</span>
                </div>
              ))
            )}
          </div>
        </div>
      )}
    </div>
  );
}
