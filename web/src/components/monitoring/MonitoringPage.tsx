import { useEffect, useState } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router";
import { Plus, AlertCircle } from "lucide-react";
import { SkeletonRows } from "@/components/shared/Skeleton";
import type { AlertSignalStatus, MonitoringAlertRule, AlertFiredEvent, ProactiveInsightReadyEvent } from "../../lib/api";
import {
  useMonitoringRules,
  useCreateMonitoringRule,
  useUpdateMonitoringRule,
  useDeleteMonitoringRule,
  useMonitoringHistory,
  useMonitoringStream,
  useProactiveInsightsFeed,
  useUpdateSearchParams,
} from "../../lib/hooks";
import { useNotification } from "../layout/NotificationSystem";
import { useAgentConversationStore } from "../../lib/stores/agent-conversation";
import { useScreenStateProvider } from "../../lib/stores/screen-state";
import { AlertRuleGroups } from "./AlertRuleGroups";
import { AlertRuleDialog } from "./AlertRuleDialog";
import { AlertHistoryPanel } from "./AlertHistoryPanel";
import { ProactiveInsightCard } from "./ProactiveInsightCard";

let proactiveMsgIdCounter = 0;
function nextProactiveMsgId() {
  return `proactive-msg-${++proactiveMsgIdCounter}`;
}

// Keeps a burst of proactive insights from pushing the tab strip below the fold — a "+N more"
// toggle (scrollable once expanded) surfaces the rest without an unbounded list.
const VISIBLE_INSIGHT_CAP = 3;

export function MonitoringPage() {
  const { data: rules = [], isLoading, isError: rulesIsError, error: rulesError } = useMonitoringRules();
  const { data: history = [], isLoading: historyIsLoading, isError: historyIsError, error: historyError } = useMonitoringHistory();
  const createRule = useCreateMonitoringRule();
  const updateRule = useUpdateMonitoringRule();
  const deleteRule = useDeleteMonitoringRule();
  const { notify } = useNotification();
  const navigate = useNavigate();
  const location = useLocation();
  const addAgentMessage = useAgentConversationStore((s) => s.addMessage);

  // `?tab=` keeps the rules/history split deep-linkable and restorable.
  const [searchParams] = useSearchParams();
  const updateParams = useUpdateSearchParams();
  const activeTab: "rules" | "history" =
    searchParams.get("tab") === "history" ? "history" : "rules";
  const setActiveTab = (tab: "rules" | "history") =>
    updateParams({ tab: tab === "rules" ? null : tab });
  const [showEditor, setShowEditor] = useState(false);
  const [editingRule, setEditingRule] = useState<MonitoringAlertRule | null>(null);

  // Open a specific rule's editor when arriving from the command palette (a Monitoring alert-rule
  // resource item carries `state: { ruleId }`), mirroring the same deep-link convention used by
  // every other feature area's palette entries (e.g. Redis's `state.cacheId`).
  useEffect(() => {
    const state = location.state as { ruleId?: string } | null;
    if (state?.ruleId) {
      const rule = rules.find((r) => r.id === state.ruleId);
      if (rule) {
        setActiveTab("rules");
        setEditingRule(rule);
        setShowEditor(true);
        navigate(location.pathname, { replace: true, state: null });
      }
    }
  }, [location, rules, navigate]);
  // Live status dots, derived from a synthetic evaluation event merged in from the stream + history.
  const [statuses, setStatuses] = useState<Record<string, AlertSignalStatus>>({});
  const [liveEvents, setLiveEvents] = useState<AlertFiredEvent[]>([]);
  const { insights, addInsight, dismiss } = useProactiveInsightsFeed();
  // Owned here (not inside AlertRuleGroups) so a group's collapsed/expanded state survives
  // switching to the History tab and back — AlertRuleGroups only mounts while Rules is active.
  const [collapsedGroups, setCollapsedGroups] = useState<Record<string, boolean>>({});
  const toggleGroupCollapse = (group: string) =>
    setCollapsedGroups((c) => ({ ...c, [group]: !c[group] }));
  const [showAllInsights, setShowAllInsights] = useState(false);

  // Subscribe to the SSE stream: push fired events into history and surface any background
  // proactive investigation that completes (workspace-intelligence Module 4). OS + in-app
  // notifications live in AppLayout's always-mounted subscription (agent-workspace-awareness
  // M3) — toasting here too would double-notify, since each subscription sees every event.
  useMonitoringStream(
    (evt) => {
      setLiveEvents((prev) => [evt, ...prev].slice(0, 200));
      setStatuses((s) => ({ ...s, [evt.ruleId]: "Firing" }));
    },
    (insight) => addInsight(insight),
  );

  // Screen-state snapshot (agent-workspace-awareness M1) — which rules are on screen and
  // what's firing right now.
  useScreenStateProvider("monitoring-page", undefined, () => ({
    activeTab,
    rules: rules.slice(0, 30).map((r) => ({
      name: r.name,
      source: r.source,
      severity: r.severity,
      enabled: r.enabled,
      aiInvestigation: r.aiInvestigationEnabled,
      status: statuses[r.id] ?? null,
      lastFiredAt: r.lastFiredAt ?? null,
    })),
    ruleCount: rules.length,
    recentFirings: liveEvents.slice(0, 10).map((e) => ({
      ruleName: e.ruleName,
      severity: e.severity,
      firedAt: e.firedAt,
    })),
  }), [rules, statuses, liveEvents, activeTab]);

  const investigateInsight = (insight: ProactiveInsightReadyEvent) => {
    // Reuses the global agent conversation rather than opening a separate "view this session"
    // surface — the sidecar-seeded session (identified by insight.sessionId) is the real source of
    // truth for any follow-up questions asked from here forward, but the global page/panel only
    // knows how to render the one global session today, so the summary is injected there directly
    // as a real, honest scope reduction (documented in status.md) rather than a half-built session
    // viewer.
    addAgentMessage({
      id: nextProactiveMsgId(),
      role: "user",
      content: `What's related to the "${insight.ruleName}" alert that just fired?`,
    });
    addAgentMessage({ id: nextProactiveMsgId(), role: "assistant", content: insight.summary });
    dismiss(insight);
    navigate("/agent");
  };

  const mergedHistory = [...liveEvents, ...history].sort(
    (a, b) => new Date(b.firedAt).getTime() - new Date(a.firedAt).getTime(),
  );

  const toggleRule = (rule: MonitoringAlertRule) => {
    const nextEnabled = !rule.enabled;
    updateRule.mutate(
      { ...rule, enabled: nextEnabled },
      // Disabling a rule is easy to click by accident right next to Delete in a dense row — give it
      // the same brief, reversible feedback a destructive-adjacent toggle deserves, rather than
      // silently taking effect with no recovery path. Re-enabling needs no such toast: it's already
      // the recovery action for a rule the user meant to keep off.
      nextEnabled
        ? undefined
        : {
            onSuccess: () => {
              notify("info", "Rule disabled", `"${rule.name}" won't fire until re-enabled.`, {
                label: "Undo",
                onClick: () => updateRule.mutate({ ...rule, enabled: true }),
              });
            },
          },
    );
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

        {insights.length > 0 && (
          <div className="mt-3" data-testid="proactive-insights-feed">
            <div
              className={
                showAllInsights && insights.length > VISIBLE_INSIGHT_CAP
                  ? "max-h-64 space-y-2 overflow-y-auto pr-1"
                  : "space-y-2"
              }
            >
              {(showAllInsights ? insights : insights.slice(0, VISIBLE_INSIGHT_CAP)).map((insight) => (
                <ProactiveInsightCard
                  key={`${insight.ruleId}|${insight.firedAt}`}
                  insight={insight}
                  onInvestigate={investigateInsight}
                  onDismiss={dismiss}
                />
              ))}
            </div>
            <div className="mt-2 flex items-center gap-3">
              {insights.length > VISIBLE_INSIGHT_CAP && (
                <button
                  onClick={() => setShowAllInsights((v) => !v)}
                  className="text-xs font-medium text-primary hover:underline"
                  data-testid="proactive-insights-toggle"
                >
                  {showAllInsights ? "Show less" : `+${insights.length - VISIBLE_INSIGHT_CAP} more`}
                </button>
              )}
              {insights.length > 1 && (
                <button
                  onClick={() => insights.forEach(dismiss)}
                  className="text-xs text-muted-foreground hover:underline"
                  data-testid="proactive-insights-dismiss-all"
                >
                  Dismiss all
                </button>
              )}
            </div>
          </div>
        )}
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

            {rulesIsError ? (
              <div
                className="flex items-center gap-2 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-3 text-sm text-destructive"
                data-testid="monitoring-rules-error"
              >
                <AlertCircle className="h-4 w-4 shrink-0" />
                <span>{rulesError instanceof Error ? rulesError.message : String(rulesError)}</span>
              </div>
            ) : isLoading ? (
              <SkeletonRows count={4} />
            ) : (
              <AlertRuleGroups
                rules={rules}
                statuses={statuses}
                collapsed={collapsedGroups}
                onToggleCollapse={toggleGroupCollapse}
                onToggle={toggleRule}
                onEdit={(r) => { setEditingRule(r); setShowEditor(true); }}
                onDelete={handleDelete}
              />
            )}
          </div>
        )}

        {activeTab === "history" && (
          historyIsError ? (
            <div
              className="flex items-center gap-2 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-3 text-sm text-destructive"
              data-testid="monitoring-history-error"
            >
              <AlertCircle className="h-4 w-4 shrink-0" />
              <span>{historyError instanceof Error ? historyError.message : String(historyError)}</span>
            </div>
          ) : historyIsLoading ? (
            <SkeletonRows count={4} />
          ) : (
            <AlertHistoryPanel events={mergedHistory} />
          )
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
