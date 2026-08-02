import { useState } from "react";
import { Pencil, Trash2, Sparkles } from "lucide-react";
import { ContextualAssistant } from "@/components/agent/ContextualAssistant";
import type { MonitoringAlertRule, AlertSignalStatus } from "../../lib/api";

// There's no "Monitoring" backend FeatureArea (no monitoring-specific agent tools exist) — a rule's
// signal source already names the area it's actually about (an AksPodHealth rule should let the
// assistant use AKS tools, not a nonexistent "Monitoring" set), so derive from that instead of
// inventing an enum value with nothing behind it.
function featureAreaForSource(source: string): string {
  if (source.startsWith("Aks")) return "Aks";
  if (source.startsWith("ServiceBus")) return "ServiceBus";
  if (source.startsWith("Redis")) return "Redis";
  if (source.startsWith("Storage")) return "Storage";
  return source;
}

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
  Ok: "bg-success",
  Firing: "bg-destructive animate-pulse",
  Skipped: "bg-gray-400",
  Error: "bg-warning",
};

const severityBadge: Record<string, string> = {
  Critical: "text-destructive bg-destructive/10",
  Warning: "text-warning bg-warning/10",
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
  const [askAiOpen, setAskAiOpen] = useState(false);

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
            <span className="text-destructive"> · fired {new Date(rule.lastFiredAt).toLocaleTimeString()}</span>
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
        onClick={() => setAskAiOpen(true)}
        className="rounded p-1 hover:bg-accent"
        title="Ask AI about this alert"
        data-testid={`monitoring-rule-ask-ai-${rule.id}`}
      >
        <Sparkles className="h-3.5 w-3.5" />
      </button>
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
      {askAiOpen && (
        <ContextualAssistant
          featureArea={featureAreaForSource(rule.source)}
          title={`alert "${rule.name}"`}
          selection={{ ruleName: rule.name, source: rule.source, severity: rule.severity }}
          onClose={() => setAskAiOpen(false)}
        />
      )}
    </div>
  );
}
