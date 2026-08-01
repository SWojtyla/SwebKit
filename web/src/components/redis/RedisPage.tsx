import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { Clock, RefreshCw } from "lucide-react";
import { RedisPageProvider, useRedisPageContext, mainTabs } from "./RedisPageContext";
import { KeysTab } from "./tabs/KeysTab";
import { ServerInfoTab } from "./tabs/ServerInfoTab";
import { SlowLogTab } from "./tabs/SlowLogTab";
import { KeyspaceTab } from "./tabs/KeyspaceTab";
import { PrefixTab } from "./tabs/PrefixTab";
import { OpsTab } from "./tabs/OpsTab";
import { PubSubTab } from "./tabs/PubSubTab";

export function RedisPage() {
  return (
    <RedisPageProvider>
      <RedisPageContent />
    </RedisPageProvider>
  );
}

function RedisPageContent() {
  const {
    caches,
    resolvedCacheId,
    handleCacheChange,
    serverInfo,
    autoRefresh,
    setAutoRefresh,
    refreshInterval,
    setRefreshInterval,
    handleManualRefresh,
    activeTab,
    setActiveTab,
    pendingConfirm,
    setPendingConfirm,
  } = useRedisPageContext();

  if (!resolvedCacheId) {
    return (
      <div className="p-6" data-testid="redis-page">
        <h1 className="text-2xl font-bold" data-testid="redis-title">Redis</h1>
        <p className="mt-4 text-muted-foreground" data-testid="redis-no-cache">
          No Redis cache configured. Add one in Settings.
        </p>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col" data-testid="redis-page">
      {/* Connection bar */}
      <div className="flex items-center gap-4 border-b px-6 py-2">
        <h1 className="text-lg font-bold shrink-0" data-testid="redis-title">Redis</h1>
        {caches.length > 0 && (
          <select
            data-testid="redis-cache-select"
            className="rounded-md border bg-card px-3 py-1.5 text-sm"
            value={resolvedCacheId}
            onChange={(e) => handleCacheChange(e.target.value)}
          >
            {caches.map((c) => (
              <option key={c.id} value={c.id}>{c.displayName}</option>
            ))}
          </select>
        )}
        {serverInfo.data && (
          <span className="flex items-center gap-1.5 text-xs text-success" data-testid="redis-connection-status">
            <span className="h-2 w-2 rounded-full bg-success" />
            Connected
          </span>
        )}
        <div className="ml-auto flex items-center gap-3">
          <label className="flex items-center gap-1.5 text-xs" data-testid="redis-auto-refresh">
            <Clock className="h-3.5 w-3.5" />
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
              data-testid="redis-auto-refresh-checkbox"
            />
            <span>Auto</span>
          </label>
          {autoRefresh && (
            <select
              value={refreshInterval}
              onChange={(e) => setRefreshInterval(Number(e.target.value))}
              className="rounded-md border bg-card px-2 py-1 text-xs"
              data-testid="redis-refresh-interval"
            >
              <option value={5}>5s</option>
              <option value={10}>10s</option>
              <option value={30}>30s</option>
              <option value={60}>60s</option>
            </select>
          )}
          <button
            onClick={handleManualRefresh}
            className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
            data-testid="redis-refresh-btn"
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Refresh
          </button>
        </div>
      </div>

      <div className="flex gap-1 border-b px-6" data-testid="redis-tabs">
        {mainTabs.map((tab) => (
          <button
            key={tab.id}
            data-testid={`redis-tab-${tab.id}`}
            onClick={() => setActiveTab(tab.id)}
            className={`px-4 py-2 text-sm font-medium transition-colors ${
              activeTab === tab.id
                ? "border-b-2 border-primary text-primary"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {pendingConfirm && (
        <ConfirmBar
          message={pendingConfirm.message}
          onConfirm={() => {
            pendingConfirm.onConfirm();
            setPendingConfirm(null);
          }}
          onCancel={() => setPendingConfirm(null)}
          confirmLabel="Delete"
          testId="redis-confirm-bar"
          confirmTestId="redis-confirm-yes"
          cancelTestId="redis-confirm-cancel"
        />
      )}

      <div className="flex flex-1 overflow-hidden">
        {activeTab === "keys" && <KeysTab />}
        {activeTab === "info" && <ServerInfoTab />}
        {activeTab === "slowlog" && <SlowLogTab />}
        {activeTab === "keyspace" && <KeyspaceTab />}
        {activeTab === "prefix" && <PrefixTab />}
        {activeTab === "ops" && <OpsTab />}
        {activeTab === "pubsub" && <PubSubTab />}
      </div>
    </div>
  );
}
