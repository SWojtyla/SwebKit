import { PubSubPanel } from "../PubSubPanel";
import { useRedisPageContext } from "../RedisPageContext";

export function PubSubTab() {
  const { resolvedCacheId } = useRedisPageContext();

  return (
    <div className="flex-1 overflow-auto p-6" data-testid="redis-pubsub">
      <PubSubPanel cacheId={resolvedCacheId} />
    </div>
  );
}
