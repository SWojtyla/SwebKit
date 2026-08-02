import { formatDuration } from "@/lib/redis-format";
import { useRedisPageContext } from "../RedisPageContext";

export function SlowLogTab() {
  const { slowLog } = useRedisPageContext();

  return (
    <div className="flex-1 overflow-auto p-6" data-testid="redis-slowlog">
      {slowLog.isLoading && <div className="text-sm text-muted-foreground">Loading slow log...</div>}
      {slowLog.error && <div className="text-sm text-destructive">Error: {slowLog.error.message}</div>}
      {slowLog.data && <SlowLogBody data={slowLog.data} />}
    </div>
  );
}

function SlowLogBody({ data }: { data: NonNullable<ReturnType<typeof useRedisPageContext>["slowLog"]["data"]> }) {
  const entries = data.entries ?? [];

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-4 text-sm text-muted-foreground">
        <span data-testid="redis-slowlog-count">{entries.length} entries</span>
        {data.truncated && <span>(truncated at {data.maxReturned})</span>}
        <span className="capitalize">Capability: {data.capability}</span>
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
                  <td className="px-3 py-2 text-right font-mono">{formatDuration(e.duration)}</td>
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
