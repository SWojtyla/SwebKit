import { useState } from "react";
import { ChevronDown, ChevronRight } from "lucide-react";
import type { MonitoringAlertRule, AlertSignalStatus } from "../../lib/api";
import { AlertRuleRow } from "./AlertRuleRow";

const sourceGroups: { source: string; label: string }[] = [
  { source: "AksPodHealth", label: "AKS" },
  { source: "AksPodRestartRate", label: "AKS" },
  { source: "AksNamespaceHealthScore", label: "AKS" },
  { source: "ServiceBusDlqDepth", label: "Service Bus" },
  { source: "ServiceBusActiveDepth", label: "Service Bus" },
  { source: "ServiceBusDeadSubscription", label: "Service Bus" },
  { source: "RedisMemoryUsage", label: "Redis" },
  { source: "RedisConnectedClients", label: "Redis" },
];

const groupLabels = Array.from(new Set(sourceGroups.map((g) => g.label)));

export function AlertRuleGroups({
  rules,
  statuses,
  onToggle,
  onEdit,
  onDelete,
}: {
  rules: MonitoringAlertRule[];
  statuses: Record<string, AlertSignalStatus>;
  onToggle: (rule: MonitoringAlertRule) => void;
  onEdit: (rule: MonitoringAlertRule) => void;
  onDelete: (rule: MonitoringAlertRule) => void;
}) {
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});

  if (rules.length === 0) {
    return (
      <div className="rounded-lg border px-3 py-8 text-center text-sm text-muted-foreground" data-testid="monitoring-rules-empty">
        No alert rules configured
      </div>
    );
  }

  return (
    <div className="space-y-3" data-testid="monitoring-rule-groups">
      {groupLabels.map((group) => {
        const groupSources = new Set(
          sourceGroups.filter((g) => g.label === group).map((g) => g.source),
        );
        const groupRules = rules.filter((r) => groupSources.has(r.source));
        if (groupRules.length === 0) return null;
        const isCollapsed = collapsed[group];
        return (
          <div key={group} className="rounded-lg border" data-testid={`monitoring-group-${group}`}>
            <button
              className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm font-semibold"
              onClick={() => setCollapsed((c) => ({ ...c, [group]: !c[group] }))}
              data-testid={`monitoring-group-toggle-${group}`}
            >
              {isCollapsed ? <ChevronRight className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
              {group}
              <span className="text-xs font-normal text-muted-foreground">({groupRules.length})</span>
            </button>
            {!isCollapsed && (
              <div>
                {groupRules.map((rule) => (
                  <AlertRuleRow
                    key={rule.id}
                    rule={rule}
                    status={statuses[rule.id]}
                    onToggle={onToggle}
                    onEdit={onEdit}
                    onDelete={onDelete}
                  />
                ))}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
