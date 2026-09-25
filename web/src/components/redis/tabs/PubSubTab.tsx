import { PubSubPanel } from "../PubSubPanel";
import { useRedisConnection } from "../redis-context";

export function PubSubTab() {
  const { resolvedCacheId } = useRedisConnection();

  return (
    <div className="flex-1 overflow-auto p-6" data-testid="redis-pubsub">
      <PubSubPanel cacheId={resolvedCacheId} />
    </div>
  );
}
