import { PrefixMemoryPanel } from "../AdvancedPanels";
import { useRedisPageContext } from "../RedisPageContext";

export function PrefixTab() {
  const { prefixMemory, separator } = useRedisPageContext();

  return (
    <div className="flex-1 overflow-auto p-6" data-testid="redis-prefix">
      <PrefixMemoryPanel
        buckets={prefixMemory.data}
        loading={prefixMemory.isLoading}
        separator={separator}
      />
    </div>
  );
}
