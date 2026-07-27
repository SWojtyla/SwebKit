import { useState } from "react";
import { Plus } from "lucide-react";
import type { AlertSignalStatus, MonitoringAlertRule, AlertFiredEvent } from "../../lib/api";
import {
  useMonitoringRules,
  useCreateMonitoringRule,
  useUpdateMonitoringRule,
  useDeleteMonitoringRule,
  useMonitoringHistory,
  useMonitoringStream,
} from "../../lib/hooks";
import { showNotification } from "../../lib/tauri-bridge";
import { useNotification } from "../layout/NotificationSystem";
import { AlertRuleGroups } from "./AlertRuleGroups";
import { AlertRuleDialog } from "./AlertRuleDialog";
import { AlertHistoryPanel } from "./AlertHistoryPanel";

export function MonitoringPage() {
  const { data: rules = [], isLoading } = useMonitoringRules();
  const { data: history = [] } = useMonitoringHistory();
  const createRule = useCreateMonitoringRule();
  const updateRule = useUpdateMonitoringRule();
  const deleteRule = useDeleteMonitoringRule();
  const { notify } = useNotification();

  const [activeTab, setActiveTab] = useState<"rules" | "history">("rules");
  const [showEditor, setShowEditor] = useState(false);
  const [editingRule, setEditingRule] = useState<MonitoringAlertRule | null>(null);
  // Live status dots, derived from a synthetic evaluation event merged in from the stream + history.
  const [statuses, setStatuses] = useState<Record<string, AlertSignalStatus>>({});
  const [liveEvents, setLiveEvents] = useState<AlertFiredEvent[]>([]);

  // Subscribe to the SSE stream: push fired events into history + raise notifications.
  useMonitoringStream((evt) => {
    setLiveEvents((prev) => [evt, ...prev].slice(0, 200));
    setStatuses((s) => ({ ...s, [evt.ruleId]: "Firing" }));
    void showNotification(evt.ruleName, evt.message);
    notify(evt.severity === "Critical" ? "error" : "success", evt.ruleName, evt.message);
  });

  const mergedHistory = [...liveEvents, ...history].sort(
    (a, b) => new Date(b.firedAt).getTime() - new Date(a.firedAt).getTime(),
  );

  const toggleRule = (rule: MonitoringAlertRule) => {
    updateRule.mutate({ ...rule, enabled: !rule.enabled });
  };

  const handleSave = (rule: MonitoringAlertRule) => {
    if (rule.id) updateRule.mutate(rule);
    else createRule.mutate(rule);
    setShowEditor(false);
    setEditingRule(null);
  };

  const handleDelete = (rule: MonitoringAlertRule) => {
    if (rule.id) deleteRule.mutate(rule.id);
  };

  return (
    <div className="flex h-full flex-col" data-testid="monitoring-page">
      <div className="border-b px-6 py-3">
        <h1 className="text-lg font-bold" data-testid="monitoring-title">Monitoring</h1>
        <p className="mt-1 text-sm text-muted-foreground">Alert rules and live alert history</p>
      </div>

      <div className="flex gap-1 border-b px-6">
        {(["rules", "history"] as const).map((tab) => (
          <button
            key={tab}
            data-testid={`monitoring-tab-${tab}`}
            onClick={() => setActiveTab(tab)}
            className={`px-4 py-2 text-sm font-medium capitalize transition-colors ${
              activeTab === tab
                ? "border-b-2 border-primary text-primary"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab === "rules" ? `Alert Rules (${rules.length})` : `Alert History (${mergedHistory.length})`}
          </button>
        ))}
      </div>

      <div className="flex-1 overflow-auto p-6">
        {activeTab === "rules" && (
          <div data-testid="monitoring-rules">
            <div className="mb-4 flex justify-end">
              <button
                onClick={() => { setEditingRule(null); setShowEditor(true); }}
                className="flex items-center gap-1 rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
                data-testid="monitoring-add-rule"
              >
                <Plus className="h-4 w-4" />
                Add Rule
              </button>
            </div>

            {isLoading ? (
              <div className="text-sm text-muted-foreground" data-testid="monitoring-loading">Loading…</div>
            ) : (
              <AlertRuleGroups
                rules={rules}
                statuses={statuses}
                onToggle={toggleRule}
                onEdit={(r) => { setEditingRule(r); setShowEditor(true); }}
                onDelete={handleDelete}
              />
            )}
          </div>
        )}

        {activeTab === "history" && (
          <AlertHistoryPanel events={mergedHistory} />
        )}
      </div>

      {showEditor && (
        <AlertRuleDialog
          rule={editingRule}
          onSave={handleSave}
          onCancel={() => { setShowEditor(false); setEditingRule(null); }}
        />
      )}
    </div>
  );
}
