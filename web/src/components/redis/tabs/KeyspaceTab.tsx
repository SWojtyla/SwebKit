import { KeyspaceHealthPanel } from "../AdvancedPanels";
import { useRedisPageContext } from "../RedisPageContext";

export function KeyspaceTab() {
  const ctx = useRedisPageContext();

  return (
    <div className="flex-1 overflow-auto p-6" data-testid="redis-keyspace">
      <KeyspaceHealthPanel
        info={ctx.serverInfo.data}
        report={ctx.health.data}
        onOpenKey={(key) => {
          ctx.setSelectedKey(key);
          ctx.setActiveTab("keys");
        }}
      />
    </div>
  );
}
