import { PrefixMemoryPanel } from "../AdvancedPanels";
import { useRedisBrowser, useRedisQueries } from "../RedisPageContext";

export function PrefixTab() {
  const { prefixMemory } = useRedisQueries();
  const { separator, openPrefixInKeys } = useRedisBrowser();

  return (
    <div className="flex-1 overflow-auto p-6" data-testid="redis-prefix">
      <PrefixMemoryPanel
        buckets={prefixMemory.data}
        loading={prefixMemory.isLoading}
        separator={separator}
        onOpenPrefix={openPrefixInKeys}
      />
    </div>
  );
}
