import { useState } from "react";
import { Pencil, Trash2, Sparkles } from "lucide-react";
import { ContextualAssistant } from "@/components/agent/ContextualAssistant";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import type { MonitoringAlertRule, AlertSignalStatus } from "../../lib/api";

// There's a "Monitoring" backend FeatureArea but it only holds alert-rule *mutation* tools
// (propose_create_alert_rule) — a rule's signal source already names the area it's actually
// about (an AksPodHealth rule should let the assistant read AKS state, not the Monitoring set),
// so derive the area from that instead.
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

// Critical gets a solid fill and bolder weight so it reads as more urgent than Warning at a
// glance, rather than the two differing only by hue at the same (10px, 10%-opacity) weight.
const severityBadge: Record<string, string> = {
  Critical: "font-semibold text-destructive-foreground bg-destructive",
  Warning: "font-medium text-warning bg-warning/10",
};

const severityRowAccent: Record<string, string> = {
  Critical: "border-l-2 border-l-destructive",
  Warning: "",
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
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  return (
    <div className="border-b last:border-0" data-testid={`monitoring-rule-row-${rule.id}`}>
      <div className={`flex items-center gap-3 px-3 py-2 ${severityRowAccent[rule.severity] ?? ""}`}>
        <span
          className={`h-2.5 w-2.5 rounded-full ${status ? statusDot[status] : "bg-gray-300"}`}
          title={status ?? "unknown"}
          data-testid={`monitoring-rule-status-${rule.id}`}
        />
        {/* The name/detail area (not just the pencil icon) is the row's real primary action —
            it opens edit, matching how other resource tables in the app make the whole row
            clickable rather than only a secondary icon button. */}
        <div
          role="button"
          tabIndex={0}
          onClick={() => onEdit(rule)}
          onKeyDown={(e) => {
            if (e.key === "Enter" || e.key === " ") {
              e.preventDefault();
              onEdit(rule);
            }
          }}
          className="-mx-1 min-w-0 flex-1 cursor-pointer rounded px-1 py-0.5 hover:bg-accent/50"
          data-testid={`monitoring-rule-open-${rule.id}`}
        >
          <div className="flex items-center gap-2">
            <span className="truncate text-sm font-medium">{rule.name}</span>
            <span className={`rounded px-1.5 py-0.5 text-xs ${severityBadge[rule.severity] ?? severityBadge.Warning}`}>
              {rule.severity}
            </span>
            {rule.aiInvestigationEnabled && (
              <span
                className="flex items-center gap-0.5 rounded px-1.5 py-0.5 text-xs text-primary"
                title="AI investigation on — when this alert fires, the agent investigates related workspace resources and posts an insight. Requires an agent profile with tool calling and the resource on the Map."
                data-testid={`monitoring-rule-ai-badge-${rule.id}`}
              >
                <Sparkles className="h-3 w-3" />
                AI
              </span>
            )}
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
          title="Edit rule"
          data-testid={`monitoring-rule-edit-${rule.id}`}
        >
          <Pencil className="h-3.5 w-3.5" />
        </button>
        <button
          onClick={() => setConfirmingDelete(true)}
          className="rounded p-1 hover:bg-accent"
          title="Delete rule"
          data-testid={`monitoring-rule-delete-${rule.id}`}
        >
          <Trash2 className="h-3.5 w-3.5" />
        </button>
      </div>
      {confirmingDelete && (
        <ConfirmBar
          message={`Delete alert rule "${rule.name}"? This can't be undone.`}
          confirmLabel="Delete"
          onConfirm={() => {
            setConfirmingDelete(false);
            onDelete(rule);
          }}
          onCancel={() => setConfirmingDelete(false)}
          testId={`monitoring-rule-delete-confirm-${rule.id}`}
        />
      )}
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
