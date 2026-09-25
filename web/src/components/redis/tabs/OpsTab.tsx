import { OpsInsightsPanel } from "../AdvancedPanels";
import { useRedisNav, useRedisQueries } from "../redis-context";

export function OpsTab() {
  const { setSelectedKey, setActiveTab } = useRedisNav();
  const { serverInfo, slowLog } = useRedisQueries();

  return (
    <div className="flex-1 overflow-auto p-6" data-testid="redis-ops">
      <OpsInsightsPanel
        info={serverInfo.data}
        slowLog={slowLog.data}
        onOpenKey={(key) => {
          setSelectedKey(key);
          setActiveTab("keys");
        }}
      />
    </div>
  );
}
