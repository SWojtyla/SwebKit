import { formatBytes } from "@/lib/redis-format";
import { useRedisPageContext } from "../RedisPageContext";

function InfoCard({ label, value, testId }: { label: string; value: string; testId: string }) {
  return (
    <div className="rounded-lg border bg-card p-4">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1 text-lg font-semibold" data-testid={testId}>{value}</div>
    </div>
  );
}

export function ServerInfoTab() {
  const { serverInfo } = useRedisPageContext();

  return (
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
  );
}
