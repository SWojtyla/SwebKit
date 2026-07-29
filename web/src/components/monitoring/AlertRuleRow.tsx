import { Pencil, Trash2 } from "lucide-react";
import type { MonitoringAlertRule, AlertSignalStatus } from "../../lib/api";

const sourceLabel: Record<string, string> = {
  AksPodHealth: "AKS Pod Health",
  AksPodRestartRate: "AKS Pod Restart Rate",
  AksNamespaceHealthScore: "AKS Namespace Health",
  ServiceBusDlqDepth: "SB DLQ Depth",
  ServiceBusActiveDepth: "SB Active Depth",
  ServiceBusDeadSubscription: "SB Dead Subscription",
  RedisMemoryUsage: "Redis Memory",
  RedisConnectedClients: "Redis Clients",
};

const statusDot: Record<AlertSignalStatus, string> = {
  Ok: "bg-green-500",
  Firing: "bg-red-500 animate-pulse",
  Skipped: "bg-gray-400",
  Error: "bg-yellow-500",
};

const severityBadge: Record<string, string> = {
  Critical: "text-red-500 bg-red-500/10",
  Warning: "text-yellow-500 bg-yellow-500/10",
};

export function AlertRuleRow({
  rule,
  status,
  onToggle,
  onEdit,
  onDelete,
}: {
  rule: MonitoringAlertRule;
  status?: AlertSignalStatus;
  onToggle: (rule: MonitoringAlertRule) => void;
  onEdit: (rule: MonitoringAlertRule) => void;
  onDelete: (rule: MonitoringAlertRule) => void;
}) {
  return (
    <div
      className="flex items-center gap-3 border-b px-3 py-2 last:border-0"
      data-testid={`monitoring-rule-row-${rule.id}`}
    >
      <span
        className={`h-2.5 w-2.5 rounded-full ${status ? statusDot[status] : "bg-gray-300"}`}
        title={status ?? "unknown"}
        data-testid={`monitoring-rule-status-${rule.id}`}
      />
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="truncate text-sm font-medium">{rule.name}</span>
          <span className={`rounded px-1.5 py-0.5 text-[10px] ${severityBadge[rule.severity] ?? severityBadge.Warning}`}>
            {rule.severity}
          </span>
        </div>
        <div className="truncate text-xs text-muted-foreground">
          {sourceLabel[rule.source] ?? rule.source}
          {rule.lastEvaluatedAt && (
            <> · evaluated {new Date(rule.lastEvaluatedAt).toLocaleTimeString()}</>
          )}
          {rule.lastFiredAt && (
            <span className="text-red-500"> · fired {new Date(rule.lastFiredAt).toLocaleTimeString()}</span>
          )}
        </div>
      </div>
      <label className="flex items-center gap-1 text-xs text-muted-foreground">
        <input
          type="checkbox"
          checked={rule.enabled}
          onChange={() => onToggle(rule)}
          data-testid={`monitoring-rule-toggle-${rule.id}`}
        />
        enabled
      </label>
      <button
        onClick={() => onEdit(rule)}
        className="rounded p-1 hover:bg-accent"
        data-testid={`monitoring-rule-edit-${rule.id}`}
      >
        <Pencil className="h-3.5 w-3.5" />
      </button>
      <button
        onClick={() => onDelete(rule)}
        className="rounded p-1 hover:bg-accent"
        data-testid={`monitoring-rule-delete-${rule.id}`}
      >
        <Trash2 className="h-3.5 w-3.5" />
      </button>
    </div>
  );
}
