import { useState } from "react";
import type {
  RedisHealthFinding,
  RedisKeyspaceHealthReport,
  RedisPrefixMemoryBucket,
  RedisServerInfo,
  RedisSlowLogSummary,
} from "@/lib/types";

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes}B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}K`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)}M`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)}G`;
}

function formatUptime(seconds: number): string {
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const mins = Math.floor((seconds % 3600) / 60);
  if (days > 0) return `${days}d ${hours}h ${mins}m`;
  if (hours > 0) return `${hours}h ${mins}m`;
  return `${mins}m`;
}

export function KeyspaceHealthPanel({
  info,
  report,
  onOpenKey,
}: {
  info: RedisServerInfo | undefined;
  report: RedisKeyspaceHealthReport | undefined;
  onOpenKey: (key: string) => void;
}) {
  const [severityFilter, setSeverityFilter] = useState<"All" | "Critical" | "Warning" | "Info">("All");

  if (!info || !report) {
    return <div className="text-sm text-muted-foreground" data-testid="keyspace-health-loading">Loading...</div>;
  }

  const hitRate = (info.keyspaceHitRatio * 100).toFixed(1);
  const memUsage = info.maxMemoryBytes > 0
    ? ((info.usedMemoryBytes / info.maxMemoryBytes) * 100).toFixed(1)
    : null;
  const findings = report.findings.filter((finding) => severityFilter === "All" || finding.severity === severityFilter);
  const severityCounts = {
    Critical: report.criticalCount,
    Warning: report.warningCount,
    Info: report.infoCount,
  };

  return (
    <div className="space-y-4" data-testid="keyspace-health-panel">
      <h3 className="text-sm font-semibold">Keyspace Health</h3>

      <div className="flex flex-wrap gap-2" data-testid="health-severity-filters">
        {(["All", "Critical", "Warning", "Info"] as const).map((severity) => (
          <button
            key={severity}
            type="button"
            onClick={() => setSeverityFilter(severity)}
            className={`rounded-full border px-2.5 py-1 text-xs ${severityFilter === severity ? "border-primary bg-primary/10 text-primary" : "text-muted-foreground"}`}
            data-testid={`health-filter-${severity.toLowerCase()}`}
          >
            {severity}{severity === "All" ? ` (${report.findings.length})` : ` (${severityCounts[severity]})`}
          </button>
        ))}
      </div>

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <div className="rounded-lg border p-3" data-testid="health-hit-rate">
          <p className="text-xs text-muted-foreground">Hit Rate</p>
          <p className="mt-1 text-xl font-bold text-success">{hitRate}%</p>
        </div>
        <div className="rounded-lg border p-3" data-testid="health-memory">
          <p className="text-xs text-muted-foreground">Memory Used</p>
          <p className="mt-1 text-xl font-bold">{info.usedMemoryHuman}</p>
          {memUsage && (
            <div className="mt-2 h-1.5 rounded-full bg-muted">
              <div className="h-full rounded-full bg-primary" style={{ width: `${memUsage}%` }} />
            </div>
          )}
          {memUsage && <p className="mt-1 text-xs text-muted-foreground">{memUsage}% of {formatBytes(info.maxMemoryBytes)}</p>}
        </div>
        <div className="rounded-lg border p-3" data-testid="health-clients">
          <p className="text-xs text-muted-foreground">Connected Clients</p>
          <p className="mt-1 text-xl font-bold">{info.connectedClients}</p>
        </div>
        <div className="rounded-lg border p-3" data-testid="health-uptime">
          <p className="text-xs text-muted-foreground">Uptime</p>
          <p className="mt-1 text-xl font-bold">{formatUptime(info.uptimeSeconds)}</p>
        </div>
      </div>

      <div className="rounded-lg border">
        <table className="w-full text-sm" data-testid="health-db-table">
          <thead className="border-b bg-muted/50">
            <tr>
              <th className="px-3 py-2 text-left">DB</th>
              <th className="px-3 py-2 text-right">Keys</th>
              <th className="px-3 py-2 text-right">Expires</th>
              <th className="px-3 py-2 text-right">Avg TTL</th>
            </tr>
          </thead>
          <tbody>
            {info.databases.map((db) => (
              <tr key={db.index} className="border-b last:border-0">
                <td className="px-3 py-2 font-mono">db{db.index}</td>
                <td className="px-3 py-2 text-right">{db.keys}</td>
                <td className="px-3 py-2 text-right">{db.expires}</td>
                <td className="px-3 py-2 text-right">{db.avgTtl > 0 ? `${(db.avgTtl / 1000).toFixed(0)}s` : "-"}</td>
              </tr>
            ))}
            {info.databases.length === 0 && (
              <tr><td colSpan={4} className="px-3 py-4 text-center text-muted-foreground">No databases with keys</td></tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="rounded-lg border" data-testid="health-findings">
        <div className="border-b px-3 py-2 text-sm font-medium">
          Findings ({findings.length}) · {report.confidenceLabel} confidence
        </div>
        <div className="divide-y">
          {findings.map((finding, index) => (
            <HealthFindingRow key={`${finding.target}-${finding.riskType}-${index}`} finding={finding} onOpenKey={onOpenKey} />
          ))}
          {findings.length === 0 && <p className="px-3 py-4 text-sm text-muted-foreground">No findings match this filter.</p>}
        </div>
      </div>
    </div>
  );
}

export function PrefixMemoryPanel({
  buckets,
  loading,
  separator = ":",
}: {
  buckets: RedisPrefixMemoryBucket[] | undefined;
  loading: boolean;
  separator?: string;
}) {
  const sorted = buckets ?? [];

  return (
    <div className="space-y-4" data-testid="prefix-memory-panel">
      <h3 className="text-sm font-semibold">Prefix Memory Breakdown</h3>
      <p className="text-xs text-muted-foreground">Sampled MEMORY USAGE by separator-delimited prefix</p>

      <div className="rounded-lg border">
        <table className="w-full text-sm" data-testid="prefix-memory-table">
          <thead className="border-b bg-muted/50">
            <tr>
              <th className="px-3 py-2 text-left">Prefix</th>
              <th className="px-3 py-2 text-right">Keys</th>
              <th className="px-3 py-2 text-right">Bytes</th>
              <th className="px-3 py-2 text-right">% of Memory</th>
            </tr>
          </thead>
          <tbody>
            {loading && <tr><td colSpan={4} className="px-3 py-4 text-center text-muted-foreground">Sampling memory usage...</td></tr>}
            {!loading && sorted.map((bucket) => (
              <tr key={bucket.prefix} className="border-b last:border-0">
                <td className="px-3 py-2 font-mono text-xs">{bucket.prefix.includes(separator) ? bucket.prefix : `${bucket.prefix}${separator}`}</td>
                <td className="px-3 py-2 text-right">{bucket.keyCount}</td>
                <td className="px-3 py-2 text-right">{formatBytes(bucket.totalBytes)}</td>
                <td className="px-3 py-2 text-right">
                  <div className="flex items-center justify-end gap-2">
                    <div className="h-1.5 w-20 rounded-full bg-muted"><div className="h-full rounded-full bg-primary" style={{ width: `${Math.min(100, bucket.percentage)}%` }} /></div>
                    {bucket.percentage.toFixed(1)}%
                  </div>
                </td>
              </tr>
            ))}
            {!loading && sorted.length === 0 && (
              <tr><td colSpan={4} className="px-3 py-4 text-center text-muted-foreground">No keys sampled yet</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function HealthFindingRow({
  finding,
  onOpenKey,
}: {
  finding: RedisHealthFinding;
  onOpenKey: (key: string) => void;
}) {
  const severityClass = finding.severity === "Critical"
    ? "bg-destructive/15 text-destructive"
    : finding.severity === "Warning"
      ? "bg-warning/15 text-warning"
      : "bg-muted text-muted-foreground";

  return (
    <div className="flex items-center gap-3 px-3 py-2 text-sm">
      <span className={`rounded-full px-2 py-0.5 text-xs ${severityClass}`}>{finding.severity}</span>
      <span className="min-w-0 flex-1">
        <span className="font-mono text-xs">{finding.target}</span>
        <span className="ml-2 text-muted-foreground">{finding.reason}</span>
      </span>
      {finding.drillKey && (
        <button type="button" className="shrink-0 text-xs text-primary hover:underline" onClick={() => onOpenKey(finding.drillKey!)} data-testid={`health-open-${finding.drillKey}`}>
          Open key
        </button>
      )}
    </div>
  );
}

export function OpsInsightsPanel({ info, slowLog }: { info: RedisServerInfo | undefined, slowLog: RedisSlowLogSummary | undefined }) {
  if (!info) {
    return <div className="text-sm text-muted-foreground" data-testid="ops-insights-loading">Loading...</div>;
  }

  const topSlow = slowLog?.entries?.slice(0, 5) ?? [];

  return (
    <div className="space-y-4" data-testid="ops-insights-panel">
      <h3 className="text-sm font-semibold">Operational Insights</h3>

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-3">
        <div className="rounded-lg border p-3" data-testid="ops-total-commands">
          <p className="text-xs text-muted-foreground">Total Commands Processed</p>
          <p className="mt-1 text-xl font-bold">{info.totalCommandsProcessed.toLocaleString()}</p>
        </div>
        <div className="rounded-lg border p-3" data-testid="ops-version">
          <p className="text-xs text-muted-foreground">Redis Version</p>
          <p className="mt-1 text-xl font-bold">{info.redisVersion}</p>
        </div>
        <div className="rounded-lg border p-3" data-testid="ops-total-keys">
          <p className="text-xs text-muted-foreground">Total Keys</p>
          <p className="mt-1 text-xl font-bold">{info.databases.reduce((sum, db) => sum + db.keys, 0)}</p>
        </div>
      </div>

      <div className="rounded-lg border">
        <div className="border-b px-3 py-2">
          <h4 className="text-sm font-medium">Top 5 Slowest Commands</h4>
        </div>
        <table className="w-full text-sm" data-testid="ops-slow-table">
          <thead className="border-b bg-muted/50">
            <tr>
              <th className="px-3 py-2 text-left">Command</th>
              <th className="px-3 py-2 text-left">Arguments</th>
              <th className="px-3 py-2 text-right">Duration</th>
              <th className="px-3 py-2 text-left">Client</th>
            </tr>
          </thead>
          <tbody>
            {topSlow.map((entry) => (
              <tr key={entry.id} className="border-b last:border-0">
                <td className="px-3 py-2 font-mono text-xs">{entry.command}</td>
                <td className="px-3 py-2 font-mono text-xs text-muted-foreground">{entry.arguments}</td>
                <td className="px-3 py-2 text-right text-xs">{entry.duration}</td>
                <td className="px-3 py-2 text-xs">{entry.clientName ?? "-"}</td>
              </tr>
            ))}
            {topSlow.length === 0 && (
              <tr><td colSpan={4} className="px-3 py-4 text-center text-muted-foreground">No slow log entries</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
