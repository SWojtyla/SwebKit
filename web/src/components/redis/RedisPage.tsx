import { useState } from "react";
import {
  useProfile,
  useRedisServerInfo,
  useRedisScanKeys,
  useRedisKeyInfo,
  useRedisKeyValue,
  useRedisHashFields,
  useRedisListItems,
  useRedisSetMembers,
  useRedisSortedSetMembers,
  useRedisSlowLog,
  useRedisDeleteKey,
} from "@/lib/hooks";

const typeColors: Record<string, string> = {
  string: "text-green-400",
  hash: "text-blue-400",
  list: "text-yellow-400",
  set: "text-purple-400",
  zset: "text-orange-400",
  none: "text-muted-foreground",
};

function formatTtl(ttl: string | null | undefined): string {
  if (!ttl) return "No expiry";
  try {
    const ts = JSON.parse(ttl);
    if (ts.Ticks !== undefined) {
      const totalMs = ts.Ticks / 10000;
      if (totalMs <= 0) return "Expired";
      const secs = Math.floor(totalMs / 1000);
      if (secs < 60) return `${secs}s`;
      if (secs < 3600) return `${Math.floor(secs / 60)}m ${secs % 60}s`;
      return `${Math.floor(secs / 3600)}h ${Math.floor((secs % 3600) / 60)}m`;
    }
  } catch {
    // not JSON
  }
  return String(ttl);
}

function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return "-";
  if (bytes < 1024) return `${bytes}B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}K`;
  return `${(bytes / (1024 * 1024)).toFixed(1)}M`;
}

export function RedisPage() {
  const { data: profile } = useProfile();
  const redisConfig = profile?.config?.redisConfig;
  const caches = redisConfig?.caches ?? [];
  const activeCacheId = redisConfig?.activeCacheId ?? caches[0]?.id ?? null;

  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [pattern, setPattern] = useState("*");
  const [searchInput, setSearchInput] = useState("*");
  const [cursor, setCursor] = useState(0);
  const [allKeys, setAllKeys] = useState<string[]>([]);
  const [activeTab, setActiveTab] = useState<"keys" | "info" | "slowlog">("keys");

  const serverInfo = useRedisServerInfo(activeCacheId);
  const scanResult = useRedisScanKeys(activeCacheId, pattern, cursor, 100);
  const keyInfo = useRedisKeyInfo(activeCacheId, selectedKey);
  const keyValue = useRedisKeyValue(activeCacheId, selectedKey, keyInfo.data?.type ?? null);
  const hashFields = useRedisHashFields(activeCacheId, selectedKey, keyInfo.data?.type ?? null);
  const listItems = useRedisListItems(activeCacheId, selectedKey, keyInfo.data?.type ?? null);
  const setMembers = useRedisSetMembers(activeCacheId, selectedKey, keyInfo.data?.type ?? null);
  const sortedSetMembers = useRedisSortedSetMembers(activeCacheId, selectedKey, keyInfo.data?.type ?? null);
  const deleteKey = useRedisDeleteKey(activeCacheId);

  const handleSearch = () => {
    setPattern(searchInput);
    setCursor(0);
    setAllKeys([]);
  };

  const handleLoadMore = () => {
    if (scanResult.data && !scanResult.data.isComplete) {
      setAllKeys((prev) => [...prev, ...scanResult.data!.keys]);
      setCursor(scanResult.data.cursor);
    }
  };

  const scanKeys = scanResult.data?.keys ?? [];
  const displayKeys = cursor === 0 ? scanKeys : allKeys.length > 0 ? [...allKeys, ...scanKeys] : scanKeys;

  const handleDeleteKey = (key: string) => {
    deleteKey.mutate(key, {
      onSuccess: () => {
        setSelectedKey(null);
        setCursor(0);
        setAllKeys([]);
      },
    });
  };

  if (!activeCacheId) {
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
      <div className="border-b px-6 py-3">
        <div className="flex items-center justify-between">
          <h1 className="text-2xl font-bold" data-testid="redis-title">Redis</h1>
          {caches.length > 1 && (
            <select
              data-testid="redis-cache-select"
              className="rounded-md border bg-card px-3 py-1.5 text-sm"
              value={activeCacheId}
              onChange={() => {}}
              disabled
            >
              {caches.map((c) => (
                <option key={c.id} value={c.id}>{c.displayName}</option>
              ))}
            </select>
          )}
        </div>
      </div>

      <div className="flex gap-1 border-b px-6">
        {(["keys", "info", "slowlog"] as const).map((tab) => (
          <button
            key={tab}
            data-testid={`redis-tab-${tab}`}
            onClick={() => setActiveTab(tab)}
            className={`px-4 py-2 text-sm font-medium capitalize transition-colors ${
              activeTab === tab
                ? "border-b-2 border-primary text-primary"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab === "info" ? "Server Info" : tab === "slowlog" ? "Slow Log" : "Keys"}
          </button>
        ))}
      </div>

      <div className="flex flex-1 overflow-hidden">
        {activeTab === "keys" && (
          <>
            <div className="w-1/3 border-r overflow-hidden flex flex-col" data-testid="redis-key-browser">
              <div className="p-3 border-b">
                <div className="flex gap-2">
                  <input
                    type="text"
                    data-testid="redis-key-search"
                    value={searchInput}
                    onChange={(e) => setSearchInput(e.target.value)}
                    onKeyDown={(e) => e.key === "Enter" && handleSearch()}
                    placeholder="Pattern (e.g. user:*)"
                    className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
                  />
                  <button
                    data-testid="redis-key-search-btn"
                    onClick={handleSearch}
                    className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
                  >
                    Search
                  </button>
                </div>
              </div>

              <div className="flex-1 overflow-auto">
                {scanResult.isLoading && (
                  <div className="p-3 text-sm text-muted-foreground">Loading keys...</div>
                )}
                {scanResult.error && (
                  <div className="p-3 text-sm text-destructive" data-testid="redis-key-error">
                    Error: {scanResult.error.message}
                  </div>
                )}
                {displayKeys.length === 0 && !scanResult.isLoading && (
                  <div className="p-3 text-sm text-muted-foreground">No keys found</div>
                )}
                {displayKeys.map((key) => (
                  <button
                    key={key}
                    data-testid={`redis-key-${key}`}
                    onClick={() => setSelectedKey(key)}
                    className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-accent ${
                      selectedKey === key ? "bg-accent" : ""
                    }`}
                  >
                    <span className="truncate font-mono">{key}</span>
                  </button>
                ))}
                {scanResult.data && !scanResult.data.isComplete && (
                  <button
                    data-testid="redis-load-more"
                    onClick={handleLoadMore}
                    className="w-full px-3 py-2 text-sm text-primary hover:bg-accent"
                  >
                    Load more...
                  </button>
                )}
              </div>
            </div>

            <div className="flex-1 overflow-auto" data-testid="redis-key-detail">
              {!selectedKey ? (
                <div className="flex h-full items-center justify-center text-muted-foreground" data-testid="redis-no-key-selected">
                  Select a key to view details
                </div>
              ) : keyInfo.isLoading ? (
                <div className="p-6 text-sm text-muted-foreground">Loading key info...</div>
              ) : keyInfo.error ? (
                <div className="p-6 text-sm text-destructive">Error: {keyInfo.error.message}</div>
              ) : keyInfo.data ? (
                <div className="p-6 space-y-4">
                  <div className="flex items-center justify-between">
                    <div>
                      <div className="text-lg font-mono font-semibold" data-testid="redis-detail-key-name">
                        {keyInfo.data.key}
                      </div>
                      <div className="mt-1 flex items-center gap-3 text-sm">
                        <span className={`font-medium ${typeColors[keyInfo.data.type] ?? ""}`} data-testid="redis-detail-key-type">
                          {keyInfo.data.type}
                        </span>
                        <span className="text-muted-foreground" data-testid="redis-detail-key-ttl">
                          TTL: {formatTtl(keyInfo.data.ttl)}
                        </span>
                        <span className="text-muted-foreground" data-testid="redis-detail-key-memory">
                          {formatBytes(keyInfo.data.memoryBytes)}
                        </span>
                        {keyInfo.data.encoding && (
                          <span className="text-muted-foreground">enc: {keyInfo.data.encoding}</span>
                        )}
                      </div>
                    </div>
                    <button
                      data-testid="redis-delete-key-btn"
                      onClick={() => handleDeleteKey(keyInfo.data.key)}
                      className="rounded-md border border-destructive px-3 py-1.5 text-sm text-destructive hover:bg-destructive/10"
                    >
                      Delete
                    </button>
                  </div>

                  {keyInfo.data.type === "string" && (
                    <div>
                      <h3 className="mb-2 text-sm font-semibold">Value</h3>
                      <pre
                        className="rounded-md border bg-muted p-4 text-sm font-mono overflow-auto max-h-96"
                        data-testid="redis-detail-string-value"
                      >
                        {keyValue.data?.value ?? "(empty)"}
                      </pre>
                    </div>
                  )}

                  {keyInfo.data.type === "hash" && (
                    <div>
                      <h3 className="mb-2 text-sm font-semibold">Hash Fields</h3>
                      <div className="rounded-md border overflow-hidden" data-testid="redis-detail-hash-fields">
                        <table className="w-full text-sm">
                          <thead className="bg-muted">
                            <tr>
                              <th className="px-3 py-2 text-left font-medium">Field</th>
                              <th className="px-3 py-2 text-left font-medium">Value</th>
                            </tr>
                          </thead>
                          <tbody>
                            {hashFields.data?.map((f) => (
                              <tr key={f.field} className="border-t">
                                <td className="px-3 py-2 font-mono">{f.field}</td>
                                <td className="px-3 py-2 font-mono break-all">{f.value}</td>
                              </tr>
                            ))}
                            {(!hashFields.data || hashFields.data.length === 0) && (
                              <tr><td colSpan={2} className="px-3 py-4 text-center text-muted-foreground">No fields</td></tr>
                            )}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}

                  {keyInfo.data.type === "list" && (
                    <div>
                      <h3 className="mb-2 text-sm font-semibold">List Items</h3>
                      <div className="rounded-md border overflow-hidden" data-testid="redis-detail-list-items">
                        <table className="w-full text-sm">
                          <thead className="bg-muted">
                            <tr>
                              <th className="px-3 py-2 text-left font-medium w-16">#</th>
                              <th className="px-3 py-2 text-left font-medium">Value</th>
                            </tr>
                          </thead>
                          <tbody>
                            {listItems.data?.map((item, i) => (
                              <tr key={i} className="border-t">
                                <td className="px-3 py-2 text-muted-foreground">{i}</td>
                                <td className="px-3 py-2 font-mono break-all">{item}</td>
                              </tr>
                            ))}
                            {(!listItems.data || listItems.data.length === 0) && (
                              <tr><td colSpan={2} className="px-3 py-4 text-center text-muted-foreground">No items</td></tr>
                            )}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}

                  {keyInfo.data.type === "set" && (
                    <div>
                      <h3 className="mb-2 text-sm font-semibold">Set Members</h3>
                      <div className="rounded-md border p-3" data-testid="redis-detail-set-members">
                        {setMembers.data?.length ? (
                          <div className="flex flex-wrap gap-2">
                            {setMembers.data.map((m) => (
                              <span key={m} className="rounded bg-muted px-2 py-1 text-sm font-mono">{m}</span>
                            ))}
                          </div>
                        ) : (
                          <span className="text-sm text-muted-foreground">No members</span>
                        )}
                      </div>
                    </div>
                  )}

                  {keyInfo.data.type === "zset" && (
                    <div>
                      <h3 className="mb-2 text-sm font-semibold">Sorted Set Members</h3>
                      <div className="rounded-md border overflow-hidden" data-testid="redis-detail-zset-members">
                        <table className="w-full text-sm">
                          <thead className="bg-muted">
                            <tr>
                              <th className="px-3 py-2 text-left font-medium">Member</th>
                              <th className="px-3 py-2 text-right font-medium w-24">Score</th>
                            </tr>
                          </thead>
                          <tbody>
                            {sortedSetMembers.data?.map((m) => (
                              <tr key={m.member} className="border-t">
                                <td className="px-3 py-2 font-mono">{m.member}</td>
                                <td className="px-3 py-2 text-right font-mono">{m.score}</td>
                              </tr>
                            ))}
                            {(!sortedSetMembers.data || sortedSetMembers.data.length === 0) && (
                              <tr><td colSpan={2} className="px-3 py-4 text-center text-muted-foreground">No members</td></tr>
                            )}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}

                  {keyInfo.data.type === "none" && (
                    <div className="text-sm text-muted-foreground" data-testid="redis-detail-key-not-found">
                      Key does not exist
                    </div>
                  )}
                </div>
              ) : null}
            </div>
          </>
        )}

        {activeTab === "info" && (
          <div className="flex-1 overflow-auto p-6" data-testid="redis-server-info">
            {serverInfo.isLoading && <div className="text-sm text-muted-foreground">Loading server info...</div>}
            {serverInfo.error && <div className="text-sm text-destructive">Error: {serverInfo.error.message}</div>}
            {serverInfo.data && (
              <div className="space-y-6">
                <div>
                  <h2 className="text-lg font-semibold mb-3">Server Overview</h2>
                  <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                    <InfoCard label="Version" value={serverInfo.data.redisVersion} testId="redis-info-version" />
                    <InfoCard label="Uptime" value={`${Math.floor(serverInfo.data.uptimeSeconds / 3600)}h ${Math.floor((serverInfo.data.uptimeSeconds % 3600) / 60)}m`} testId="redis-info-uptime" />
                    <InfoCard label="Connected Clients" value={String(serverInfo.data.connectedClients)} testId="redis-info-clients" />
                    <InfoCard label="Used Memory" value={serverInfo.data.usedMemoryHuman} testId="redis-info-memory" />
                    <InfoCard label="Max Memory" value={serverInfo.data.maxMemoryBytes > 0 ? formatBytes(serverInfo.data.maxMemoryBytes) : "No limit"} testId="redis-info-maxmemory" />
                    <InfoCard label="Commands Processed" value={String(serverInfo.data.totalCommandsProcessed)} testId="redis-info-commands" />
                    <InfoCard label="Hit Ratio" value={`${(serverInfo.data.keyspaceHitRatio * 100).toFixed(1)}%`} testId="redis-info-hit-ratio" />
                  </div>
                </div>

                <div>
                  <h2 className="text-lg font-semibold mb-3">Databases</h2>
                  <div className="rounded-md border overflow-hidden">
                    <table className="w-full text-sm">
                      <thead className="bg-muted">
                        <tr>
                          <th className="px-3 py-2 text-left font-medium">DB</th>
                          <th className="px-3 py-2 text-right font-medium">Keys</th>
                          <th className="px-3 py-2 text-right font-medium">Expires</th>
                          <th className="px-3 py-2 text-right font-medium">Avg TTL</th>
                        </tr>
                      </thead>
                      <tbody>
                        {serverInfo.data.databases.map((db) => (
                          <tr key={db.index} className="border-t">
                            <td className="px-3 py-2">db{db.index}</td>
                            <td className="px-3 py-2 text-right">{db.keys}</td>
                            <td className="px-3 py-2 text-right">{db.expires}</td>
                            <td className="px-3 py-2 text-right">{db.avgTtl > 0 ? `${db.avgTtl}ms` : "-"}</td>
                          </tr>
                        ))}
                        {serverInfo.data.databases.length === 0 && (
                          <tr><td colSpan={4} className="px-3 py-4 text-center text-muted-foreground">No databases</td></tr>
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>
            )}
          </div>
        )}

        {activeTab === "slowlog" && (
          <div className="flex-1 overflow-auto p-6" data-testid="redis-slowlog">
            <SlowLogTab cacheId={activeCacheId} />
          </div>
        )}
      </div>
    </div>
  );
}

function InfoCard({ label, value, testId }: { label: string; value: string; testId: string }) {
  return (
    <div className="rounded-lg border bg-card p-4">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1 text-lg font-semibold" data-testid={testId}>{value}</div>
    </div>
  );
}

function SlowLogTab({ cacheId }: { cacheId: string }) {
  const slowLog = useRedisSlowLog(cacheId);

  if (slowLog.isLoading) return <div className="text-sm text-muted-foreground">Loading slow log...</div>;
  if (slowLog.error) return <div className="text-sm text-destructive">Error: {slowLog.error.message}</div>;
  if (!slowLog.data) return null;

  const entries = slowLog.data.entries ?? [];

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-4 text-sm text-muted-foreground">
        <span data-testid="redis-slowlog-count">{entries.length} entries</span>
        {slowLog.data.truncated && <span>(truncated at {slowLog.data.maxReturned})</span>}
        <span className="capitalize">Capability: {slowLog.data.capability}</span>
      </div>

      {entries.length === 0 ? (
        <div className="text-sm text-muted-foreground">No slow log entries</div>
      ) : (
        <div className="rounded-md border overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-muted">
              <tr>
                <th className="px-3 py-2 text-left font-medium">ID</th>
                <th className="px-3 py-2 text-left font-medium">Time</th>
                <th className="px-3 py-2 text-right font-medium">Duration</th>
                <th className="px-3 py-2 text-left font-medium">Command</th>
                <th className="px-3 py-2 text-left font-medium">Args</th>
                <th className="px-3 py-2 text-left font-medium">Client</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((e) => (
                <tr key={e.id} className="border-t">
                  <td className="px-3 py-2 text-muted-foreground">{e.id}</td>
                  <td className="px-3 py-2">{new Date(e.executedAt).toLocaleTimeString()}</td>
                  <td className="px-3 py-2 text-right font-mono">{e.duration}</td>
                  <td className="px-3 py-2 font-mono">{e.command}</td>
                  <td className="px-3 py-2 font-mono text-muted-foreground">{e.arguments}</td>
                  <td className="px-3 py-2 text-muted-foreground">{e.clientName ?? "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

