import { OpsInsightsPanel } from "../AdvancedPanels";
import { useRedisPageContext } from "../RedisPageContext";

export function OpsTab() {
  const { serverInfo, slowLog } = useRedisPageContext();

  return (
    <div className="flex-1 overflow-auto p-6" data-testid="redis-ops">
      <OpsInsightsPanel info={serverInfo.data} slowLog={slowLog.data} />
    </div>
  );
}
