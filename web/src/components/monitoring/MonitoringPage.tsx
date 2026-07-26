import { useState } from "react";
import { Plus, CheckCircle, Clock, Trash2, Pencil } from "lucide-react";

interface AlertRule {
  id: string;
  name: string;
  enabled: boolean;
  severity: "critical" | "warning" | "info";
  condition: string;
  resource: string;
  lastTriggered: string | null;
}

interface AlertEvent {
  id: string;
  ruleName: string;
  severity: "critical" | "warning" | "info";
  timestamp: string;
  message: string;
  acknowledged: boolean;
}

const demoRules: AlertRule[] = [
  { id: "1", name: "High CPU Usage", enabled: true, severity: "warning", condition: "CPU > 80% for 5m", resource: "aks-prod-cluster", lastTriggered: "2026-07-25T14:23:00Z" },
  { id: "2", name: "Pod Restart Loop", enabled: true, severity: "critical", condition: "Restarts > 5 in 10m", resource: "order-service", lastTriggered: "2026-07-25T09:15:00Z" },
  { id: "3", name: "Queue Depth High", enabled: true, severity: "warning", condition: "Message count > 1000", resource: "orders-queue", lastTriggered: null },
  { id: "4", name: "Cache Hit Rate Low", enabled: false, severity: "info", condition: "Hit rate < 70%", resource: "redis-prod", lastTriggered: null },
];

const demoEvents: AlertEvent[] = [
  { id: "e1", ruleName: "High CPU Usage", severity: "warning", timestamp: "2026-07-25T14:23:00Z", message: "CPU usage at 87% for 5 minutes on aks-prod-cluster", acknowledged: false },
  { id: "e2", ruleName: "Pod Restart Loop", severity: "critical", timestamp: "2026-07-25T09:15:00Z", message: "Pod order-service-7b4f has restarted 6 times in 10 minutes", acknowledged: true },
  { id: "e3", ruleName: "High CPU Usage", severity: "warning", timestamp: "2026-07-24T22:10:00Z", message: "CPU usage at 85% for 5 minutes on aks-prod-cluster", acknowledged: true },
];

const severityColors: Record<string, string> = {
  critical: "text-red-500 bg-red-500/10",
  warning: "text-yellow-500 bg-yellow-500/10",
  info: "text-blue-500 bg-blue-500/10",
};

export function MonitoringPage() {
  const [rules, setRules] = useState<AlertRule[]>(demoRules);
  const [events, setEvents] = useState<AlertEvent[]>(demoEvents);
  const [activeTab, setActiveTab] = useState<"rules" | "history">("rules");
  const [showEditor, setShowEditor] = useState(false);
  const [editingRule, setEditingRule] = useState<AlertRule | null>(null);

  const toggleRule = (id: string) => {
    setRules((prev) => prev.map((r) => (r.id === id ? { ...r, enabled: !r.enabled } : r)));
  };

  const deleteRule = (id: string) => {
    setRules((prev) => prev.filter((r) => r.id !== id));
  };

  const acknowledgeEvent = (id: string) => {
    setEvents((prev) => prev.map((e) => (e.id === id ? { ...e, acknowledged: true } : e)));
  };

  const handleSaveRule = (rule: AlertRule) => {
    if (editingRule) {
      setRules((prev) => prev.map((r) => (r.id === rule.id ? rule : r)));
    } else {
      setRules((prev) => [...prev, { ...rule, id: String(Date.now()) }]);
    }
    setShowEditor(false);
    setEditingRule(null);
  };

  return (
    <div className="flex h-full flex-col" data-testid="monitoring-page">
      <div className="border-b px-6 py-3">
        <h1 className="text-lg font-bold" data-testid="monitoring-title">Monitoring</h1>
        <p className="mt-1 text-sm text-muted-foreground">Alert rules and alert history</p>
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
            {tab === "rules" ? "Alert Rules" : "Alert History"}
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

            <div className="rounded-lg border">
              <table className="w-full text-sm" data-testid="monitoring-rules-table">
                <thead className="border-b bg-muted/50">
                  <tr>
                    <th className="px-3 py-2 text-left">Name</th>
                    <th className="px-3 py-2 text-left">Severity</th>
                    <th className="px-3 py-2 text-left">Condition</th>
                    <th className="px-3 py-2 text-left">Resource</th>
                    <th className="px-3 py-2 text-left">Last Triggered</th>
                    <th className="px-3 py-2 text-center">Enabled</th>
                    <th className="px-3 py-2 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {rules.map((rule) => (
                    <tr key={rule.id} className="border-b last:border-0">
                      <td className="px-3 py-2 font-medium">{rule.name}</td>
                      <td className="px-3 py-2">
                        <span className={`rounded px-2 py-0.5 text-xs ${severityColors[rule.severity]}`}>
                          {rule.severity}
                        </span>
                      </td>
                      <td className="px-3 py-2 font-mono text-xs text-muted-foreground">{rule.condition}</td>
                      <td className="px-3 py-2 text-xs">{rule.resource}</td>
                      <td className="px-3 py-2 text-xs text-muted-foreground">
                        {rule.lastTriggered ? new Date(rule.lastTriggered).toLocaleString() : "Never"}
                      </td>
                      <td className="px-3 py-2 text-center">
                        <input
                          type="checkbox"
                          checked={rule.enabled}
                          onChange={() => toggleRule(rule.id)}
                          data-testid={`monitoring-rule-toggle-${rule.id}`}
                        />
                      </td>
                      <td className="px-3 py-2 text-right">
                        <button
                          onClick={() => { setEditingRule(rule); setShowEditor(true); }}
                          className="rounded p-1 hover:bg-accent"
                          data-testid={`monitoring-rule-edit-${rule.id}`}
                        >
                          <Pencil className="h-3.5 w-3.5" />
                        </button>
                        <button
                          onClick={() => deleteRule(rule.id)}
                          className="rounded p-1 hover:bg-accent"
                          data-testid={`monitoring-rule-delete-${rule.id}`}
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                        </button>
                      </td>
                    </tr>
                  ))}
                  {rules.length === 0 && (
                    <tr><td colSpan={7} className="px-3 py-8 text-center text-muted-foreground">No alert rules configured</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {activeTab === "history" && (
          <div data-testid="monitoring-history">
            <div className="rounded-lg border">
              <table className="w-full text-sm" data-testid="monitoring-history-table">
                <thead className="border-b bg-muted/50">
                  <tr>
                    <th className="px-3 py-2 text-left">Rule</th>
                    <th className="px-3 py-2 text-left">Severity</th>
                    <th className="px-3 py-2 text-left">Timestamp</th>
                    <th className="px-3 py-2 text-left">Message</th>
                    <th className="px-3 py-2 text-center">Status</th>
                    <th className="px-3 py-2 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {events.map((event) => (
                    <tr key={event.id} className="border-b last:border-0">
                      <td className="px-3 py-2 font-medium">{event.ruleName}</td>
                      <td className="px-3 py-2">
                        <span className={`rounded px-2 py-0.5 text-xs ${severityColors[event.severity]}`}>
                          {event.severity}
                        </span>
                      </td>
                      <td className="px-3 py-2 text-xs text-muted-foreground">
                        {new Date(event.timestamp).toLocaleString()}
                      </td>
                      <td className="px-3 py-2 text-xs">{event.message}</td>
                      <td className="px-3 py-2 text-center">
                        {event.acknowledged ? (
                          <CheckCircle className="mx-auto h-4 w-4 text-green-500" />
                        ) : (
                          <Clock className="mx-auto h-4 w-4 text-yellow-500" />
                        )}
                      </td>
                      <td className="px-3 py-2 text-right">
                        {!event.acknowledged && (
                          <button
                            onClick={() => acknowledgeEvent(event.id)}
                            className="rounded-md border px-2 py-1 text-xs hover:bg-accent"
                            data-testid={`monitoring-event-ack-${event.id}`}
                          >
                            Acknowledge
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                  {events.length === 0 && (
                    <tr><td colSpan={6} className="px-3 py-8 text-center text-muted-foreground">No alert events</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

      {showEditor && (
        <AlertRuleEditor
          rule={editingRule}
          onSave={handleSaveRule}
          onCancel={() => { setShowEditor(false); setEditingRule(null); }}
        />
      )}
    </div>
  );
}

function AlertRuleEditor({ rule, onSave, onCancel }: {
  rule: AlertRule | null;
  onSave: (rule: AlertRule) => void;
  onCancel: () => void;
}) {
  const [name, setName] = useState(rule?.name ?? "");
  const [severity, setSeverity] = useState<AlertRule["severity"]>(rule?.severity ?? "warning");
  const [condition, setCondition] = useState(rule?.condition ?? "");
  const [resource, setResource] = useState(rule?.resource ?? "");

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="alert-rule-editor">
      <div className="w-96 rounded-lg border bg-card p-6 shadow-lg">
        <h2 className="text-lg font-semibold">{rule ? "Edit Rule" : "New Alert Rule"}</h2>
        <div className="mt-4 space-y-3">
          <div>
            <label className="text-sm font-medium">Name</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
              data-testid="alert-rule-name"
            />
          </div>
          <div>
            <label className="text-sm font-medium">Severity</label>
            <select
              value={severity}
              onChange={(e) => setSeverity(e.target.value as AlertRule["severity"])}
              className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
              data-testid="alert-rule-severity"
            >
              <option value="critical">Critical</option>
              <option value="warning">Warning</option>
              <option value="info">Info</option>
            </select>
          </div>
          <div>
            <label className="text-sm font-medium">Condition</label>
            <input
              type="text"
              value={condition}
              onChange={(e) => setCondition(e.target.value)}
              placeholder="e.g. CPU > 80% for 5m"
              className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
              data-testid="alert-rule-condition"
            />
          </div>
          <div>
            <label className="text-sm font-medium">Resource</label>
            <input
              type="text"
              value={resource}
              onChange={(e) => setResource(e.target.value)}
              placeholder="e.g. aks-prod-cluster"
              className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
              data-testid="alert-rule-resource"
            />
          </div>
        </div>
        <div className="mt-6 flex justify-end gap-2">
          <button
            onClick={onCancel}
            className="rounded-md border px-4 py-2 text-sm hover:bg-accent"
            data-testid="alert-rule-cancel"
          >
            Cancel
          </button>
          <button
            onClick={() => onSave({
              id: rule?.id ?? "",
              name,
              enabled: rule?.enabled ?? true,
              severity,
              condition,
              resource,
              lastTriggered: rule?.lastTriggered ?? null,
            })}
            disabled={!name || !condition}
            className="rounded-md bg-primary px-4 py-2 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
            data-testid="alert-rule-save"
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
