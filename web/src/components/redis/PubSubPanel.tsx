import { useState } from "react";
import { Radio, RefreshCw, Search } from "lucide-react";
import { useRedisPubSub } from "@/lib/hooks";

interface Props {
  cacheId: string | null;
}

export function PubSubPanel({ cacheId }: Props) {
  const [pattern, setPattern] = useState("");
  const [appliedPattern, setAppliedPattern] = useState<string | null>(null);
  const { data: snapshot, isLoading, error, refetch } = useRedisPubSub(cacheId, appliedPattern);

  const handleFilter = () => {
    setAppliedPattern(pattern.trim() || null);
  };

  return (
    <div className="space-y-4" data-testid="redis-pubsub-panel">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Radio className="h-5 w-5 text-muted-foreground" />
          <h2 className="text-lg font-semibold">Pub/Sub</h2>
        </div>
        <button
          onClick={() => refetch()}
          disabled={isLoading}
          className="flex items-center gap-1 rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
          data-testid="redis-pubsub-refresh-btn"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${isLoading ? "animate-spin" : ""}`} />
          Refresh
        </button>
      </div>

      <p className="text-xs text-muted-foreground">
        Read-only snapshot from PUBSUB CHANNELS / NUMSUB. Live publish/subscribe is not supported.
      </p>

      <div className="flex items-center gap-2">
        <input
          type="text"
          value={pattern}
          onChange={(e) => setPattern(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && handleFilter()}
          placeholder="Channel pattern (e.g. events:*)..."
          className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
          data-testid="redis-pubsub-pattern-input"
        />
        <button
          onClick={handleFilter}
          className="flex items-center gap-1 rounded-md border px-3 py-1.5 text-sm hover:bg-accent"
          data-testid="redis-pubsub-filter-btn"
        >
          <Search className="h-3.5 w-3.5" />
          Filter
        </button>
      </div>

      {isLoading && (
        <div className="text-sm text-muted-foreground" data-testid="redis-pubsub-loading">
          Loading Pub/Sub snapshot...
        </div>
      )}

      {error && (
        <div className="text-sm text-destructive" data-testid="redis-pubsub-error">
          Error: {error.message}
        </div>
      )}

      {!isLoading && !error && snapshot && (
        <>
          {snapshot.capability === "Unsupported" && (
            <div
              className="rounded-md border border-warning/50 bg-warning/10 p-3 text-sm"
              role="status"
              data-testid="redis-pubsub-unsupported"
            >
              <strong>Unsupported:</strong> Pub/Sub channel inspection is not available on this Redis target.
            </div>
          )}

          {snapshot.capability === "PermissionLimited" && (
            <div
              className="rounded-md border border-orange-500/50 bg-orange-500/10 p-3 text-sm"
              role="status"
              data-testid="redis-pubsub-permission-limited"
            >
              <strong>Permission limited:</strong> Insufficient permissions to read Pub/Sub channel information.
            </div>
          )}

          {snapshot.capability === "Loaded" && (
            <div className="space-y-4">
              <div
                className="flex flex-wrap items-center gap-4 text-sm"
                data-testid="redis-pubsub-summary"
              >
                <span>
                  Channels:{" "}
                  <strong data-testid="redis-pubsub-channel-count">{snapshot.channels.length}</strong>
                </span>
                <span>
                  Pattern subscriptions:{" "}
                  <strong data-testid="redis-pubsub-pattern-count">{snapshot.patternSubscriptionCount}</strong>
                </span>
                {snapshot.truncated && (
                  <span className="text-muted-foreground" role="status" data-testid="redis-pubsub-truncated">
                    Truncated to {snapshot.maxChannels} channels.
                  </span>
                )}
              </div>

              {snapshot.channels.length === 0 ? (
                <div className="text-sm text-muted-foreground" data-testid="redis-pubsub-empty">
                  No active channels.
                </div>
              ) : (
                <div className="rounded-md border">
                  <table className="w-full text-sm" data-testid="redis-pubsub-channels-table">
                    <thead className="border-b bg-muted/50">
                      <tr>
                        <th className="px-3 py-2 text-left font-medium">Channel</th>
                        <th className="px-3 py-2 text-right font-medium">Subscribers</th>
                      </tr>
                    </thead>
                    <tbody>
                      {snapshot.channels.map((channel) => (
                        <tr
                          key={channel.channel}
                          className="border-b last:border-0"
                          data-testid={`redis-pubsub-channel-row-${channel.channel}`}
                        >
                          <td className="px-3 py-2 font-mono">{channel.channel}</td>
                          <td
                            className="px-3 py-2 text-right"
                            data-testid={`redis-pubsub-subscriber-count-${channel.channel}`}
                          >
                            {channel.subscriberCount}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          )}
        </>
      )}
    </div>
  );
}
