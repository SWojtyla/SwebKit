import { useState, useMemo, useEffect, useCallback } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Search, Filter, X, Columns, Pin, Plus, RotateCw, Check, AlertCircle, ArrowUpRight } from "lucide-react";
import { useSbPeekMessages, useSbPeekDlq, useSbCompleteMessages, useSbCompleteDlq, useSbResubmitDlq } from "@/lib/hooks";
import type { SbEntityInfo, SbMessage } from "@/lib/types";
import { applyFilters } from "./filterLogic";
import { AdvancedFilterPanel } from "./AdvancedFilterPanel";
import type { AdvancedFilterRule } from "./filterTypes";
import {
  loadSbPreferences,
  saveSbPreferences,
  PEEK_COUNT_OPTIONS,
  AUTO_REFRESH_OPTIONS,
  ALL_BUILTIN_COLUMNS,
  type SbListPreferences,
  type RowDensity,
} from "@/lib/stores/sb-preferences";

interface Props {
  nsId: string | null;
  entity: SbEntityInfo | null;
  viewMode: "active" | "dlq";
  selectedMessage: SbMessage | null;
  onSelectMessage: (message: SbMessage) => void;
}

const densityClass: Record<RowDensity, string> = {
  compact: "py-0.5",
  default: "py-1.5",
  comfort: "py-2.5",
};

// Real columns rendered as an actual data table — this is what the MAUI
// desktop app's message grid looked like (a dense spreadsheet-style view,
// each field in its own column) rather than a stacked card per message.
// Order matches MAUI's default column order; `dlqOnly` columns only render
// in the DLQ view, same as before.
interface ColumnDef {
  key: string;
  label: string;
  className?: string;
  dlqOnly?: boolean;
  render: (msg: SbMessage) => string;
}

const COLUMN_DEFS: ColumnDef[] = [
  { key: "enqueuedAt", label: "Enqueued", render: (m) => new Date(m.enqueuedAt).toLocaleTimeString() },
  { key: "sequenceNumber", label: "Seq #", render: (m) => (m.sequenceNumber !== null ? `#${m.sequenceNumber}` : "-") },
  { key: "messageId", label: "Message ID", className: "max-w-[160px]", render: (m) => m.messageId },
  { key: "correlationId", label: "Correlation ID", className: "max-w-[140px]", render: (m) => m.correlationId ?? "-" },
  { key: "subject", label: "Subject", className: "max-w-[220px]", render: (m) => m.subject ?? "-" },
  { key: "deliveryCount", label: "Delivery", render: (m) => String(m.deliveryCount) },
  { key: "contentType", label: "Content Type", render: (m) => m.contentType ?? "-" },
  { key: "sessionId", label: "Session", render: (m) => m.sessionId ?? "-" },
  { key: "partitionKey", label: "Partition Key", render: (m) => m.systemProperties?.partitionKey ?? "-" },
  { key: "deadLetterReason", label: "DLQ Reason", className: "max-w-[200px]", dlqOnly: true, render: (m) => m.deadLetterReason ?? "-" },
];

export function MessageList({ nsId, entity, viewMode, selectedMessage, onSelectMessage }: Props) {
  const qc = useQueryClient();
  const [textFilter, setTextFilter] = useState("");
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [advancedRules, setAdvancedRules] = useState<AdvancedFilterRule[]>([]);
  const [advancedEnabled, setAdvancedEnabled] = useState(true);
  const [pinnedSessionId, setPinnedSessionId] = useState<string | null>(null);
  const [showColumnToggle, setShowColumnToggle] = useState(false);
  const [selectedMsgs, setSelectedMsgs] = useState<Set<string>>(new Set());
  const [customColumnInput, setCustomColumnInput] = useState("");

  // Load preferences
  const [prefs, setPrefs] = useState<SbListPreferences>(() => {
    if (nsId && entity) return loadSbPreferences(nsId, entity.entityPath);
    return {
      peekCount: 50,
      autoRefreshInterval: 0,
      rowDensity: "default" as RowDensity,
      visibleColumns: ["subject", "sequenceNumber", "enqueuedAt"],
      customColumns: [],
    };
  });

  // Reload prefs when entity changes
  useEffect(() => {
    if (nsId && entity) {
      setPrefs(loadSbPreferences(nsId, entity.entityPath));
    }
  }, [nsId, entity?.entityPath]);

  // Save prefs on change
  useEffect(() => {
    if (nsId && entity) {
      saveSbPreferences(nsId, entity.entityPath, prefs);
    }
  }, [prefs, nsId, entity?.entityPath]);

  const visibleColumns = new Set(prefs.visibleColumns);

  const activeQuery = useSbPeekMessages(
    viewMode === "active" ? nsId : null,
    entity?.entityPath ?? null,
    prefs.peekCount,
  );
  const dlqQuery = useSbPeekDlq(
    viewMode === "dlq" ? nsId : null,
    entity?.entityPath ?? null,
    prefs.peekCount,
  );

  const messages = viewMode === "active" ? activeQuery.data : dlqQuery.data;
  const isLoading = viewMode === "active" ? activeQuery.isLoading : dlqQuery.isLoading;

  // Auto-refresh
  useEffect(() => {
    if (prefs.autoRefreshInterval === 0 || !nsId || !entity) return;
    const id = setInterval(() => {
      qc.invalidateQueries({ queryKey: ["sb-peek", nsId, entity.entityPath] });
      qc.invalidateQueries({ queryKey: ["sb-dlq", nsId, entity.entityPath] });
    }, prefs.autoRefreshInterval * 1000);
    return () => clearInterval(id);
  }, [prefs.autoRefreshInterval, nsId, entity, qc]);

  // Bulk action mutations
  const completeMutation = useSbCompleteMessages();
  const completeDlqMutation = useSbCompleteDlq();
  const resubmitDlqMutation = useSbResubmitDlq();

  const handleBulkComplete = useCallback(() => {
    if (!nsId || !entity || selectedMsgs.size === 0) return;
    const seqNumbers = messages
      ?.filter((m) => selectedMsgs.has(`${m.messageId}-${m.sequenceNumber}`))
      .map((m) => m.sequenceNumber)
      .filter((n): n is number => n !== null) ?? [];
    if (seqNumbers.length === 0) return;
    if (!confirm(`Complete ${seqNumbers.length} message(s)?`)) return;
    if (viewMode === "active") {
      completeMutation.mutate({ nsId, entityPath: entity.entityPath, sequenceNumbers: seqNumbers });
    } else {
      completeDlqMutation.mutate({ nsId, entityPath: entity.entityPath, sequenceNumbers: seqNumbers.map(String) });
    }
    setSelectedMsgs(new Set());
  }, [nsId, entity, selectedMsgs, messages, viewMode, completeMutation, completeDlqMutation]);

  const handleBulkResubmit = useCallback(() => {
    if (!nsId || !entity || selectedMsgs.size === 0) return;
    const seqNumbers = messages
      ?.filter((m) => selectedMsgs.has(`${m.messageId}-${m.sequenceNumber}`))
      .map((m) => m.sequenceNumber)
      .filter((n): n is number => n !== null) ?? [];
    if (seqNumbers.length === 0) return;
    if (!confirm(`Resubmit ${seqNumbers.length} message(s)?`)) return;
    resubmitDlqMutation.mutate({ nsId, entityPath: entity.entityPath, sequenceNumbers: seqNumbers.map(String), targetEntityPath: null });
    setSelectedMsgs(new Set());
  }, [nsId, entity, selectedMsgs, messages, resubmitDlqMutation]);

  const toggleSelect = (msg: SbMessage) => {
    const key = `${msg.messageId}-${msg.sequenceNumber}`;
    setSelectedMsgs((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  const toggleSelectAll = () => {
    if (selectedMsgs.size === filteredMessages.length) {
      setSelectedMsgs(new Set());
    } else {
      setSelectedMsgs(new Set(filteredMessages.map((m) => `${m.messageId}-${m.sequenceNumber}`)));
    }
  };

  const addCustomColumn = () => {
    const col = customColumnInput.trim();
    if (!col || prefs.customColumns.includes(col)) return;
    setPrefs((p) => ({ ...p, customColumns: [...p.customColumns, col] }));
    setCustomColumnInput("");
  };

  const removeCustomColumn = (col: string) => {
    setPrefs((p) => ({ ...p, customColumns: p.customColumns.filter((c) => c !== col) }));
  };

  const toggleBuiltInColumn = (col: string) => {
    setPrefs((p) => {
      const next = new Set(p.visibleColumns);
      if (next.has(col)) next.delete(col);
      else next.add(col);
      return { ...p, visibleColumns: [...next] };
    });
  };

  // Suggested custom columns from loaded messages
  const suggestedColumns = useMemo(() => {
    if (!messages) return [];
    const allKeys = new Set<string>();
    messages.forEach((m) => Object.keys(m.applicationProperties).forEach((k) => allKeys.add(k)));
    return [...allKeys].filter((k) => !prefs.customColumns.includes(k)).slice(0, 10);
  }, [messages, prefs.customColumns]);

  const filteredMessages = useMemo(
    () =>
      applyFilters(messages ?? [], textFilter, advancedRules, advancedEnabled && showAdvanced, pinnedSessionId),
    [messages, textFilter, advancedRules, advancedEnabled, showAdvanced, pinnedSessionId],
  );

  const activeRuleCount = advancedRules.filter((r) => r.enabled && r.value.trim()).length;

  if (!entity) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground" data-testid="message-list-empty">
        Select an entity
      </div>
    );
  }

  if (isLoading) {
    return <div className="p-4 text-sm text-muted-foreground" data-testid="message-list-loading">Loading messages...</div>;
  }

  return (
    <div className="flex h-full flex-col" data-testid="message-list-container">
      {/* Filter bar with peek count, auto-refresh, density */}
      <div className="flex items-center gap-1.5 border-b px-2 py-1.5">
        <div className="relative flex-1">
          <Search className="absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
          <input
            type="text"
            data-testid="message-text-filter"
            value={textFilter}
            onChange={(e) => setTextFilter(e.target.value)}
            placeholder="Search messages..."
            className="w-full rounded-md border bg-card py-1.5 pl-8 pr-7 text-xs"
          />
          {textFilter && (
            <button
              onClick={() => setTextFilter("")}
              className="absolute right-1.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
              title="Clear search"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>

        {/* Peek count selector */}
        <select
          data-testid="peek-count-select"
          value={prefs.peekCount}
          onChange={(e) => setPrefs((p) => ({ ...p, peekCount: Number(e.target.value) }))}
          className="rounded-md border bg-card px-1.5 py-1.5 text-xs"
          title="Peek count"
        >
          {PEEK_COUNT_OPTIONS.map((c) => (
            <option key={c} value={c}>{c}</option>
          ))}
        </select>

        {/* Auto-refresh selector */}
        <select
          data-testid="auto-refresh-select"
          value={prefs.autoRefreshInterval}
          onChange={(e) => setPrefs((p) => ({ ...p, autoRefreshInterval: Number(e.target.value) }))}
          className="rounded-md border bg-card px-1.5 py-1.5 text-xs"
          title="Auto-refresh"
        >
          {AUTO_REFRESH_OPTIONS.map((opt) => (
            <option key={opt.value} value={opt.value}>{opt.label}</option>
          ))}
        </select>

        {/* Row density selector */}
        <select
          data-testid="row-density-select"
          value={prefs.rowDensity}
          onChange={(e) => setPrefs((p) => ({ ...p, rowDensity: e.target.value as RowDensity }))}
          className="rounded-md border bg-card px-1.5 py-1.5 text-xs"
          title="Row density"
        >
          <option value="compact">Compact</option>
          <option value="default">Default</option>
          <option value="comfort">Comfort</option>
        </select>

        <button
          data-testid="toggle-advanced-filter"
          onClick={() => setShowAdvanced(!showAdvanced)}
          title="Advanced filters"
          className={`flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs ${
            showAdvanced || activeRuleCount > 0
              ? "border-primary bg-primary/10 text-primary"
              : "text-muted-foreground hover:bg-accent"
          }`}
        >
          <Filter className="h-3.5 w-3.5" />
          {activeRuleCount > 0 && (
            <span className="rounded-full bg-primary px-1.5 text-[10px] text-primary-foreground">
              {activeRuleCount}
            </span>
          )}
        </button>
        <button
          data-testid="toggle-column-visibility"
          onClick={() => setShowColumnToggle(!showColumnToggle)}
          title="Column visibility"
          className={`flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs ${
            showColumnToggle ? "border-primary bg-primary/10 text-primary" : "text-muted-foreground hover:bg-accent"
          }`}
        >
          <Columns className="h-3.5 w-3.5" />
        </button>
      </div>

      {/* Column toggle dropdown with custom columns */}
      {showColumnToggle && (
        <div className="border-b bg-muted/20 px-3 py-2" data-testid="column-toggle-dropdown">
          <div className="mb-2 text-xs font-medium text-muted-foreground">Built-in columns</div>
          <div className="flex flex-wrap gap-2">
            {ALL_BUILTIN_COLUMNS.map((col) => (
              <label key={col} className="flex items-center gap-1 text-xs">
                <input
                  type="checkbox"
                  checked={visibleColumns.has(col)}
                  onChange={() => toggleBuiltInColumn(col)}
                  data-testid={`column-toggle-${col}`}
                />
                {col}
              </label>
            ))}
          </div>
          {prefs.customColumns.length > 0 && (
            <>
              <div className="mb-1 mt-2 text-xs font-medium text-muted-foreground">Custom property columns</div>
              <div className="flex flex-wrap gap-2">
                {prefs.customColumns.map((col) => (
                  <span key={col} className="flex items-center gap-1 rounded bg-accent px-1.5 py-0.5 text-xs">
                    {col}
                    <button onClick={() => removeCustomColumn(col)} className="text-muted-foreground hover:text-foreground">
                      <X className="h-3 w-3" />
                    </button>
                  </span>
                ))}
              </div>
            </>
          )}
          {suggestedColumns.length > 0 && (
            <>
              <div className="mb-1 mt-2 text-xs font-medium text-muted-foreground">Suggested from data</div>
              <div className="flex flex-wrap gap-1">
                {suggestedColumns.map((col) => (
                  <button
                    key={col}
                    onClick={() => setPrefs((p) => ({ ...p, customColumns: [...p.customColumns, col] }))}
                    className="flex items-center gap-0.5 rounded border px-1.5 py-0.5 text-xs hover:bg-accent"
                    data-testid={`suggest-column-${col}`}
                  >
                    <Plus className="h-2.5 w-2.5" /> {col}
                  </button>
                ))}
              </div>
            </>
          )}
          <div className="mt-2 flex items-center gap-1">
            <input
              type="text"
              value={customColumnInput}
              onChange={(e) => setCustomColumnInput(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && addCustomColumn()}
              placeholder="Add custom property column..."
              className="flex-1 rounded border bg-card px-2 py-1 text-xs"
              data-testid="custom-column-input"
            />
            <button onClick={addCustomColumn} className="rounded border px-2 py-1 text-xs hover:bg-accent" data-testid="add-custom-column">
              Add
            </button>
          </div>
        </div>
      )}

      {/* Session pinning filter */}
      <div className="flex items-center gap-1.5 border-b px-2 py-1">
        <Pin className="h-3.5 w-3.5 text-muted-foreground" />
        <input
          type="text"
          data-testid="session-pin-filter"
          value={pinnedSessionId ?? ""}
          onChange={(e) => setPinnedSessionId(e.target.value || null)}
          placeholder="Filter by Session ID..."
          className="flex-1 rounded-md border bg-card px-2 py-1 text-xs"
        />
        {pinnedSessionId && (
          <button
            onClick={() => setPinnedSessionId(null)}
            className="text-muted-foreground hover:text-foreground"
            data-testid="session-pin-clear"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        )}
      </div>

      {/* Advanced filter panel */}
      {showAdvanced && (
        <>
          <div className="flex items-center justify-between border-b bg-muted/20 px-2 py-1">
            <label className="flex items-center gap-1.5 text-xs">
              <input
                type="checkbox"
                checked={advancedEnabled}
                onChange={(e) => setAdvancedEnabled(e.target.checked)}
              />
              <span className="font-medium">Advanced filters</span>
            </label>
            {advancedRules.length > 0 && (
              <button
                onClick={() => setAdvancedRules([])}
                className="text-xs text-muted-foreground hover:text-foreground"
              >
                Clear all
              </button>
            )}
          </div>
          <AdvancedFilterPanel rules={advancedRules} onChange={setAdvancedRules} />
        </>
      )}

      {/* Bulk action bar */}
      {selectedMsgs.size > 0 && (
        <div className="flex items-center gap-2 border-b bg-primary/10 px-3 py-1.5" data-testid="bulk-action-bar">
          <span className="text-xs font-medium">{selectedMsgs.size} selected</span>
          <button
            onClick={toggleSelectAll}
            className="text-xs text-muted-foreground hover:text-foreground"
            data-testid="bulk-select-all"
          >
            {selectedMsgs.size === filteredMessages.length ? "Deselect all" : "Select all"}
          </button>
          <div className="ml-auto flex items-center gap-1.5">
            {viewMode === "dlq" && (
              <button
                onClick={handleBulkResubmit}
                disabled={resubmitDlqMutation.isPending}
                className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                data-testid="bulk-resubmit"
              >
                <ArrowUpRight className="h-3 w-3" /> Resubmit
              </button>
            )}
            <button
              onClick={handleBulkComplete}
              disabled={completeMutation.isPending || completeDlqMutation.isPending}
              className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
              data-testid="bulk-complete"
            >
              <Check className="h-3 w-3" /> Complete
            </button>
            <button
              onClick={() => setSelectedMsgs(new Set())}
              className="rounded border px-2 py-1 text-xs hover:bg-accent"
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* Message list — a real data table (columns, not a stacked card per
          message), matching the MAUI grid's dense spreadsheet layout */}
      {filteredMessages.length === 0 ? (
        <div
          className="flex h-full items-center justify-center text-sm text-muted-foreground"
          data-testid={messages && messages.length === 0 ? "message-list-no-messages" : "message-list-no-matches"}
        >
          {messages && messages.length === 0
            ? `No ${viewMode === "dlq" ? "dead-lettered" : "active"} messages`
            : "No messages match the current filters"}
        </div>
      ) : (
        <div className="flex-1 overflow-auto" data-testid="message-list">
          <table className="w-full border-collapse text-xs">
            <thead className="sticky top-0 z-10 bg-card">
              <tr className="border-b">
                <th className="w-8 px-2 py-1.5">
                  <input
                    type="checkbox"
                    checked={selectedMsgs.size > 0 && selectedMsgs.size === filteredMessages.length}
                    onChange={toggleSelectAll}
                    data-testid="message-select-all-checkbox"
                  />
                </th>
                {COLUMN_DEFS.filter((col) => visibleColumns.has(col.key) && (!col.dlqOnly || viewMode === "dlq")).map((col) => (
                  <th key={col.key} className="whitespace-nowrap px-2 py-1.5 text-left font-medium text-muted-foreground">
                    {col.label}
                  </th>
                ))}
                {prefs.customColumns.map((col) => (
                  <th key={col} className="whitespace-nowrap px-2 py-1.5 text-left font-medium text-muted-foreground">
                    {col}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {filteredMessages.map((msg) => {
                const msgKey = `${msg.messageId}-${msg.sequenceNumber}`;
                const isSelected = selectedMsgs.has(msgKey);
                const isActive =
                  selectedMessage?.messageId === msg.messageId &&
                  selectedMessage?.sequenceNumber === msg.sequenceNumber;
                return (
                  <tr
                    key={msgKey}
                    data-testid={`message-item-${msg.sequenceNumber}`}
                    onClick={() => onSelectMessage(msg)}
                    className={`cursor-pointer border-b hover:bg-accent ${isActive ? "bg-accent" : ""}`}
                  >
                    <td className={`px-2 ${densityClass[prefs.rowDensity]}`} onClick={(e) => e.stopPropagation()}>
                      <input
                        type="checkbox"
                        checked={isSelected}
                        onChange={() => toggleSelect(msg)}
                        data-testid={`message-checkbox-${msg.sequenceNumber}`}
                      />
                    </td>
                    {COLUMN_DEFS.filter((col) => visibleColumns.has(col.key) && (!col.dlqOnly || viewMode === "dlq")).map((col) => {
                      const value = col.render(msg);
                      const isDelivery = col.key === "deliveryCount";
                      const isDlqReason = col.key === "deadLetterReason";
                      return (
                        <td
                          key={col.key}
                          title={value}
                          className={`truncate px-2 ${densityClass[prefs.rowDensity]} ${col.className ?? ""} ${
                            isDelivery && viewMode === "dlq" && msg.deliveryCount > 0 ? "text-destructive" : ""
                          } ${isDlqReason ? "text-destructive" : ""}`}
                        >
                          {isDlqReason && msg.deadLetterReason && (
                            <AlertCircle className="mr-1 inline h-3 w-3 shrink-0" />
                          )}
                          {value}
                        </td>
                      );
                    })}
                    {prefs.customColumns.map((col) => {
                      const val = msg.applicationProperties[col];
                      const display = val === undefined || val === null ? "-" : String(val);
                      return (
                        <td key={col} title={display} className={`truncate px-2 text-muted-foreground ${densityClass[prefs.rowDensity]}`}>
                          {display}
                        </td>
                      );
                    })}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Filter result count */}
      <div className="border-t px-3 py-1 text-xs text-muted-foreground" data-testid="message-filter-count">
        {filteredMessages.length} of {messages?.length ?? 0} messages
        {prefs.autoRefreshInterval > 0 && (
          <span className="ml-2 flex items-center gap-1 text-green-500" data-testid="auto-refresh-indicator">
            <RotateCw className="h-3 w-3 animate-spin" /> {prefs.autoRefreshInterval}s
          </span>
        )}
      </div>
    </div>
  );
}
