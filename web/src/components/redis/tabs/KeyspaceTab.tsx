import { KeyspaceHealthPanel } from "../AdvancedPanels";
import { useRedisBrowser, useRedisNav, useRedisQueries } from "../redis-context";

export function KeyspaceTab() {
  const ctx = { ...useRedisNav(), ...useRedisQueries(), ...useRedisBrowser() };

  return (
    <div className="flex-1 overflow-auto p-6" data-testid="redis-keyspace">
      <KeyspaceHealthPanel
        info={ctx.serverInfo.data}
        report={ctx.health.data}
        hasKeys={ctx.displayKeys.length > 0}
        onOpenKey={(key) => {
          ctx.setSelectedKey(key);
          ctx.setActiveTab("keys");
        }}
      />
    </div>
  );
}
