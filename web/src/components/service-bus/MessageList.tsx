import { useState, useMemo, useEffect, useCallback, useRef } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { Search, Filter, X, Columns, Pin, Plus, RotateCw, Check, AlertCircle, ArrowUpRight, Bookmark, Download, Loader2 } from "lucide-react";
import { useSbCompleteMessages, useSbCompleteDlq, useSbResubmitDlq } from "@/lib/hooks";
import type { SbEntityInfo, SbMessage } from "@/lib/types";
import { downloadBlob } from "@/lib/download";
import { buildZip } from "@/lib/zip";
import { useNotification } from "@/components/layout/NotificationSystem";
import { messageToDownloadObject, safeFileName, messageKey as sbMessageKey } from "./exportHelpers";
import { applyFilters } from "./filterLogic";
import { AdvancedFilterPanel } from "./AdvancedFilterPanel";
import type { AdvancedFilterRule } from "./filterTypes";
import { isRuleConfigured, createFilterRule } from "./filterTypes";
import {
  loadSbPreferences,
  saveSbPreferences,
  PEEK_COUNT_OPTIONS,
  AUTO_REFRESH_OPTIONS,
  ALL_BUILTIN_COLUMNS,
  type SbListPreferences,
  type RowDensity,
} from "@/lib/stores/sb-preferences";
import {
  loadSavedFilters,
  addSavedFilter,
  deleteSavedFilter,
  type SbSavedFilter,
} from "@/lib/stores/sb-filters";

interface Props {
  nsId: string | null;
  entity: SbEntityInfo | null;
  viewMode: "active" | "dlq";
  messages: SbMessage[];
  isLoading: boolean;
  isLoadingMore: boolean;
  canLoadMore: boolean;
  totalAvailable: number | null;
  selectedMessage: SbMessage | null;
  onSelectMessage: (message: SbMessage) => void;
  onLoadMore: () => void;
}

const densityClass: Record<RowDensity, string> = {
  compact: "py-0.5",
  default: "py-1.5",
  comfort: "py-2.5",
};

// Estimated row height per density, used as the virtualizer's initial size
// guess before `measureElement` corrects it against the real rendered height.
const ROW_HEIGHT_ESTIMATE: Record<RowDensity, number> = {
  compact: 24,
  default: 32,
  comfort: 40,
};

// The message list renders as a real data table (see ColumnDef below), but
// virtualized rows are absolutely-positioned siblings rather than actual
// <tr> elements in a shared <table> — so every row (and the header) must
// share one explicit `grid-template-columns` string for the columns to line
// up. These widths mirror the `max-w-[...]` classes the columns already had.
const CHECKBOX_COL_WIDTH = "32px";
const CUSTOM_COLUMN_WIDTH = "140px";
const COLUMN_WIDTHS: Record<string, string> = {
  enqueuedAt: "110px",
  sequenceNumber: "90px",
  messageId: "160px",
  correlationId: "140px",
  subject: "220px",
  deliveryCount: "90px",
  contentType: "120px",
  sessionId: "120px",
  partitionKey: "130px",
  deadLetterReason: "200px",
  nsbEndpoint: "200px",
  nsbMessageType: "160px",
  nsbTimeSent: "150px",
  nsbConversation: "200px",
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

const NSB_COLUMN_DEFS: ColumnDef[] = [
  { key: "nsbEndpoint", label: "NSB Endpoint", className: "max-w-[200px]", render: (m) => getNsbProp(m, "NServiceBus.OriginatingEndpoint") },
  { key: "nsbMessageType", label: "NSB Message Type", render: (m) => truncateNsbType(getNsbProp(m, "NServiceBus.EnclosedMessageTypes")) },
  { key: "nsbTimeSent", label: "NSB Time Sent", className: "max-w-[150px]", render: (m) => getNsbProp(m, "NServiceBus.TimeSent") },
  { key: "nsbConversation", label: "NSB Conversation", className: "max-w-[200px]", render: (m) => getNsbProp(m, "NServiceBus.ConversationId") },
];

function getNsbProp(message: SbMessage, key: string): string {
  const value = message.applicationProperties[key];
  return value === undefined || value === null ? "-" : String(value);
}

function truncateNsbType(value: string): string {
  if (value === "-") return value;
  const comma = value.indexOf(",");
  const typePart = comma > 0 ? value.slice(0, comma) : value;
  const lastDot = typePart.lastIndexOf(".");
  return lastDot >= 0 ? typePart.slice(lastDot + 1) : typePart;
}

export function MessageList({
  nsId,
  entity,
  viewMode,
  messages,
  isLoading,
  isLoadingMore,
  canLoadMore,
  totalAvailable,
  selectedMessage,
  onSelectMessage,
  onLoadMore,
}: Props) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  const listRef = useRef<HTMLDivElement>(null);
  const sentinelRef = useRef<HTMLDivElement>(null);
  const [textFilter, setTextFilter] = useState("");
  const [advancedRules, setAdvancedRules] = useState<AdvancedFilterRule[]>([]);
  const [advancedEnabled, setAdvancedEnabled] = useState(false);
  const [pinnedSessionId, setPinnedSessionId] = useState<string | null>(null);
  const [showColumnToggle, setShowColumnToggle] = useState(false);
  const [selectedMsgs, setSelectedMsgs] = useState<Set<string>>(new Set());
  const [customColumnInput, setCustomColumnInput] = useState("");

  // Master filter switch and saved filter state
  const [filtersEnabled, setFiltersEnabled] = useState(true);
  const [savedFilters, setSavedFilters] = useState<SbSavedFilter[]>([]);
  const [showSavedFilters, setShowSavedFilters] = useState(false);
  const [saveFilterName, setSaveFilterName] = useState("");
  const [showSaveFilterInput, setShowSaveFilterInput] = useState(false);

  // Load preferences
  const [prefs, setPrefs] = useState<SbListPreferences>(() => {
    if (nsId && entity) return loadSbPreferences(nsId, entity.entityPath);
    return {
      peekCount: 50,
      autoRefreshInterval: 0,
      rowDensity: "default" as RowDensity,
      visibleColumns: ["subject", "sequenceNumber", "enqueuedAt"],
      customColumns: [],
      nsbMode: false,
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
  const nsbMode = prefs.nsbMode ?? false;

  // Load saved filters when entity changes
  useEffect(() => {
    if (nsId && entity) {
      setSavedFilters(loadSavedFilters(nsId, entity.entityPath));
    }
  }, [nsId, entity?.entityPath]);

  // Auto-refresh
  useEffect(() => {
    if (prefs.autoRefreshInterval === 0 || !nsId || !entity) return;
    const id = setInterval(() => {
      qc.invalidateQueries({ queryKey: ["sb-peek", nsId, entity.entityPath] });
      qc.invalidateQueries({ queryKey: ["sb-dlq", nsId, entity.entityPath] });
      qc.invalidateQueries({ queryKey: ["sb-entity-stats", nsId, entity.entityPath] });
    }, prefs.autoRefreshInterval * 1000);
    return () => clearInterval(id);
  }, [prefs.autoRefreshInterval, nsId, entity, qc]);

  // Infinite-scroll sentinel
  useEffect(() => {
    if (!sentinelRef.current || !listRef.current || !canLoadMore || isLoadingMore) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) onLoadMore();
      },
      { root: listRef.current, threshold: 0 },
    );
    observer.observe(sentinelRef.current);
    return () => observer.disconnect();
  }, [canLoadMore, isLoadingMore, onLoadMore]);

  // Bulk action mutations
  const completeMutation = useSbCompleteMessages();
  const completeDlqMutation = useSbCompleteDlq();
  const resubmitDlqMutation = useSbResubmitDlq();

  const handleBulkComplete = useCallback(() => {
    if (!nsId || !entity || selectedMsgs.size === 0) return;
    const seqNumbers = messages
      .filter((m) => selectedMsgs.has(sbMessageKey(m)))
      .map((m) => m.sequenceNumber)
      .filter((n): n is number => n !== null);
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
      .filter((m) => selectedMsgs.has(sbMessageKey(m)))
      .map((m) => m.sequenceNumber)
      .filter((n): n is number => n !== null);
    if (seqNumbers.length === 0) return;
    if (!confirm(`Resubmit ${seqNumbers.length} message(s)?`)) return;
    resubmitDlqMutation.mutate({ nsId, entityPath: entity.entityPath, sequenceNumbers: seqNumbers.map(String), targetEntityPath: null });
    setSelectedMsgs(new Set());
  }, [nsId, entity, selectedMsgs, messages, resubmitDlqMutation]);

  const toggleSelect = (msg: SbMessage) => {
    const key = sbMessageKey(msg);
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
      setSelectedMsgs(new Set(filteredMessages.map((m) => sbMessageKey(m))));
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
    if (messages.length === 0) return [];
    const allKeys = new Set<string>();
    messages.forEach((m) => Object.keys(m.applicationProperties).forEach((k) => allKeys.add(k)));
    return [...allKeys].filter((k) => !prefs.customColumns.includes(k)).slice(0, 10);
  }, [messages, prefs.customColumns]);

  const filteredMessages = useMemo(() => {
    if (!filtersEnabled) return messages;
    return applyFilters(messages, textFilter, advancedRules, advancedEnabled, pinnedSessionId);
  }, [messages, textFilter, advancedRules, advancedEnabled, pinnedSessionId, filtersEnabled]);

  const activeRuleCount = advancedRules.filter((r) => r.enabled && isRuleConfigured(r)).length;

  const canSaveFilter = Boolean(textFilter.trim() || pinnedSessionId || advancedRules.some(isRuleConfigured));

  const handleSaveFilter = () => {
    if (!nsId || !entity || !saveFilterName.trim()) return;
    const filter: SbSavedFilter = {
      name: saveFilterName.trim(),
      text: textFilter,
      filtersEnabled,
      advancedEnabled,
      advancedRules,
      pinnedSessionId,
    };
    const updated = addSavedFilter(nsId, entity.entityPath, filter);
    setSavedFilters(updated);
    setShowSaveFilterInput(false);
    setSaveFilterName("");
  };

  const handleDownloadZip = useCallback(async () => {
    if (!entity) return;
    const messagesToDownload =
      selectedMsgs.size > 0
        ? messages.filter((m) => selectedMsgs.has(sbMessageKey(m)))
        : filteredMessages;
    if (messagesToDownload.length === 0) return;
    const files: Record<string, string> = {};
    messagesToDownload.forEach((m, i) => {
      const seq = m.sequenceNumber != null ? `-${m.sequenceNumber}` : "";
      const name = `message-${String(i + 1).padStart(3, "0")}-${safeFileName(m.messageId)}${seq}.json`;
      files[name] = JSON.stringify(messageToDownloadObject(m), null, 2);
    });
    const zipped = await buildZip(files);
    const scope = selectedMsgs.size > 0 ? "selected" : "filtered";
    const entitySlug = safeFileName(entity.name || entity.entityPath || "messages");
    const timestamp = new Date().toISOString().slice(0, 19).replace(/[T:]/g, "-");
    const fileName = `${entitySlug}-${scope}-${timestamp}.zip`;
    downloadBlob(fileName, zipped);
    notify("success", `Downloaded ${messagesToDownload.length} message(s) as ZIP`);
  }, [entity, messages, filteredMessages, selectedMsgs, notify]);

  // Columns actually rendered given current view mode + toggles, and the
  // shared grid template both the header and every virtualized row use so
  // columns stay aligned across independently-positioned row elements.
  const activeColumnDefs = COLUMN_DEFS.filter(
    (col) => visibleColumns.has(col.key) && (!col.dlqOnly || viewMode === "dlq"),
  );
  const gridTemplateColumns = [
    CHECKBOX_COL_WIDTH,
    ...activeColumnDefs.map((col) => COLUMN_WIDTHS[col.key] ?? "140px"),
    ...prefs.customColumns.map(() => CUSTOM_COLUMN_WIDTH),
    ...(nsbMode ? NSB_COLUMN_DEFS.map((col) => COLUMN_WIDTHS[col.key] ?? "160px") : []),
  ].join(" ");

  const rowVirtualizer = useVirtualizer({
    count: filteredMessages.length,
    getScrollElement: () => listRef.current,
    estimateSize: () => ROW_HEIGHT_ESTIMATE[prefs.rowDensity],
    getItemKey: (index) => sbMessageKey(filteredMessages[index]),
    measureElement: (el) => el?.getBoundingClientRect().height ?? ROW_HEIGHT_ESTIMATE[prefs.rowDensity],
  });

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

        {/* Saved filters */}
        <div className="relative">
          <button
            data-testid="saved-filters-toggle"
            onClick={() => setShowSavedFilters(!showSavedFilters)}
            disabled={savedFilters.length === 0 && !canSaveFilter}
            title="Saved filters"
            className="flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
          >
            <Bookmark className="h-3.5 w-3.5" />
            Saved
          </button>
          {showSavedFilters && (
            <div className="absolute right-0 top-full z-20 mt-1 w-64 rounded-md border bg-card p-2 shadow-lg">
              {savedFilters.length === 0 ? (
                <div className="text-xs text-muted-foreground">No saved filters</div>
              ) : (
                <div className="space-y-1">
                  {savedFilters.map((f) => (
                    <div key={f.name} className="flex items-center justify-between gap-1">
                      <button
                        className="flex-1 rounded px-1 py-0.5 text-left text-xs hover:bg-accent"
                        onClick={() => {
                          setTextFilter(f.text);
                          setFiltersEnabled(f.filtersEnabled);
                          setAdvancedEnabled(f.advancedEnabled);
                          setAdvancedRules(f.advancedRules);
                          setPinnedSessionId(f.pinnedSessionId);
                          setShowSavedFilters(false);
                        }}
                      >
                        {f.name}
                      </button>
                      <button
                        onClick={() => {
                          if (!nsId || !entity) return;
                          const updated = deleteSavedFilter(nsId, entity.entityPath, f.name);
                          setSavedFilters(updated);
                        }}
                        className="text-muted-foreground hover:text-foreground"
                        title="Delete saved filter"
                      >
                        <X className="h-3 w-3" />
                      </button>
                    </div>
                  ))}
                </div>
              )}
              {showSaveFilterInput ? (
                <div className="mt-2 flex items-center gap-1">
                  <input
                    type="text"
                    value={saveFilterName}
                    onChange={(e) => setSaveFilterName(e.target.value)}
                    onKeyDown={(e) => e.key === "Enter" && handleSaveFilter()}
                    placeholder="Filter name..."
                    className="flex-1 rounded border bg-card px-2 py-1 text-xs"
                    autoFocus
                  />
                  <button
                    onClick={handleSaveFilter}
                    disabled={!saveFilterName.trim()}
                    className="rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                  >
                    Save
                  </button>
                  <button
                    onClick={() => { setShowSaveFilterInput(false); setSaveFilterName(""); }}
                    className="rounded border px-2 py-1 text-xs hover:bg-accent"
                  >
                    Cancel
                  </button>
                </div>
              ) : (
                canSaveFilter && (
                  <button
                    onClick={() => setShowSaveFilterInput(true)}
                    className="mt-2 w-full rounded border px-2 py-1 text-xs hover:bg-accent"
                  >
                    Save current filter
                  </button>
                )
              )}
            </div>
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
          data-testid="toggle-filters-enabled"
          onClick={() => setFiltersEnabled(!filtersEnabled)}
          title="Toggle all filters"
          className={`flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs ${
            filtersEnabled
              ? "border-primary bg-primary/10 text-primary"
              : "text-muted-foreground hover:bg-accent"
          }`}
        >
          {filtersEnabled ? "Filters: On" : "Filters: Off"}
        </button>
        <button
          data-testid="toggle-advanced-filter"
          onClick={() => setAdvancedEnabled((prev) => !prev)}
          disabled={!filtersEnabled}
          title="Advanced filters"
          className={`flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs ${
            advancedEnabled && filtersEnabled
              ? "border-primary bg-primary/10 text-primary"
              : "text-muted-foreground hover:bg-accent disabled:opacity-50"
          }`}
        >
          <Filter className="h-3.5 w-3.5" />
          {activeRuleCount > 0 && (
            <span className="rounded-full bg-primary px-1.5 text-[10px] text-primary-foreground">
              {activeRuleCount}
            </span>
          )}
          <span className="hidden sm:inline">{advancedEnabled ? "Advanced: On" : "Advanced: Off"}</span>
        </button>
        {advancedEnabled && (
          <button
            data-testid="add-rule"
            onClick={() => setAdvancedRules((rules) => [...rules, createFilterRule()])}
            title="Add advanced filter rule"
            className="flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs text-muted-foreground hover:bg-accent"
          >
            <Plus className="h-3.5 w-3.5" /> Rule
          </button>
        )}
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
        <button
          data-testid="message-download-zip"
          onClick={handleDownloadZip}
          disabled={filteredMessages.length === 0 || isLoadingMore}
          title="Download selected or filtered messages as ZIP"
          className="flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs text-muted-foreground hover:bg-accent disabled:opacity-50"
        >
          <Download className="h-3.5 w-3.5" />
          <span className="hidden sm:inline">ZIP</span>
        </button>
        <button
          data-testid="toggle-nsb-mode"
          onClick={() => setPrefs((p) => ({ ...p, nsbMode: !nsbMode }))}
          title="Toggle NServiceBus view — shows endpoint, message type, conversation ID"
          className={`flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs ${
            nsbMode ? "border-primary bg-primary/10 text-primary" : "text-muted-foreground hover:bg-accent"
          }`}
        >
          NSB
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
      {advancedEnabled && (
        <>
          <div className="flex items-center justify-between border-b bg-muted/20 px-2 py-1">
            <span className="text-xs font-medium">Advanced filters</span>
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
          data-testid={messages.length === 0 ? "message-list-no-messages" : "message-list-no-matches"}
        >
          {messages.length === 0
            ? `No ${viewMode === "dlq" ? "dead-lettered" : "active"} messages`
            : "No messages match the current filters"}
        </div>
      ) : (
        <div ref={listRef} className="flex-1 min-h-0 overflow-auto text-xs" data-testid="message-list" role="table" aria-label="Messages">
          <div className="sticky top-0 z-10 grid border-b bg-card" style={{ gridTemplateColumns }} role="row">
            <div className="flex items-center px-2 py-1.5" role="columnheader">
              <input
                type="checkbox"
                checked={selectedMsgs.size > 0 && selectedMsgs.size === filteredMessages.length}
                onChange={toggleSelectAll}
                data-testid="message-select-all-checkbox"
              />
            </div>
            {activeColumnDefs.map((col) => (
              <div key={col.key} className="flex items-center whitespace-nowrap px-2 py-1.5 text-left font-medium text-muted-foreground" role="columnheader">
                {col.label}
              </div>
            ))}
            {prefs.customColumns.map((col) => (
              <div key={col} className="flex items-center whitespace-nowrap px-2 py-1.5 text-left font-medium text-muted-foreground" role="columnheader">
                {col}
              </div>
            ))}
            {nsbMode && NSB_COLUMN_DEFS.map((col) => (
              <div key={col.key} className="flex items-center whitespace-nowrap px-2 py-1.5 text-left font-medium text-muted-foreground" role="columnheader">
                {col.label}
              </div>
            ))}
          </div>

          <div
            style={{ height: `${rowVirtualizer.getTotalSize()}px`, position: "relative", width: "100%" }}
            data-testid="message-list-virtualizer"
            role="rowgroup"
          >
            {rowVirtualizer.getVirtualItems().map((virtualRow) => {
              const msg = filteredMessages[virtualRow.index];
              const msgKey = sbMessageKey(msg);
              const isSelected = selectedMsgs.has(msgKey);
              const isActive =
                selectedMessage?.messageId === msg.messageId &&
                selectedMessage?.sequenceNumber === msg.sequenceNumber;
              return (
                <div
                  key={virtualRow.key}
                  data-index={virtualRow.index}
                  ref={rowVirtualizer.measureElement}
                  role="presentation"
                  style={{
                    position: "absolute",
                    top: 0,
                    left: 0,
                    width: "100%",
                    transform: `translateY(${virtualRow.start}px)`,
                  }}
                >
                  <div
                    data-testid={`message-item-${msg.sequenceNumber}`}
                    onClick={() => onSelectMessage(msg)}
                    role="row"
                    className={`grid cursor-pointer border-b hover:bg-accent ${isActive ? "bg-accent" : ""}`}
                    style={{ gridTemplateColumns }}
                  >
                    <div
                      className={`flex items-center px-2 ${densityClass[prefs.rowDensity]}`}
                      onClick={(e) => e.stopPropagation()}
                      role="cell"
                    >
                      <input
                        type="checkbox"
                        checked={isSelected}
                        onChange={() => toggleSelect(msg)}
                        data-testid={`message-checkbox-${msg.sequenceNumber}`}
                      />
                    </div>
                    {activeColumnDefs.map((col) => {
                      const value = col.render(msg);
                      const isDelivery = col.key === "deliveryCount";
                      const isDlqReason = col.key === "deadLetterReason";
                      return (
                        <div
                          key={col.key}
                          title={value}
                          role="cell"
                          className={`flex min-w-0 items-center truncate px-2 ${densityClass[prefs.rowDensity]} ${col.className ?? ""} ${
                            isDelivery && viewMode === "dlq" && msg.deliveryCount > 0 ? "text-destructive" : ""
                          } ${isDlqReason ? "text-destructive" : ""}`}
                        >
                          {isDlqReason && msg.deadLetterReason && (
                            <AlertCircle className="mr-1 inline h-3 w-3 shrink-0" />
                          )}
                          <span className="truncate">{value}</span>
                        </div>
                      );
                    })}
                    {prefs.customColumns.map((col) => {
                      const val = msg.applicationProperties[col];
                      const display = val === undefined || val === null ? "-" : String(val);
                      return (
                        <div
                          key={col}
                          title={display}
                          role="cell"
                          className={`flex min-w-0 items-center truncate px-2 text-muted-foreground ${densityClass[prefs.rowDensity]}`}
                        >
                          <span className="truncate">{display}</span>
                        </div>
                      );
                    })}
                    {nsbMode && NSB_COLUMN_DEFS.map((col) => {
                      const value = col.render(msg);
                      return (
                        <div
                          key={col.key}
                          title={value}
                          role="cell"
                          className={`flex min-w-0 items-center truncate px-2 text-muted-foreground ${densityClass[prefs.rowDensity]} ${col.className ?? ""}`}
                        >
                          <span className="truncate">{value}</span>
                        </div>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
          <div ref={sentinelRef} className="h-1" data-testid="message-load-sentinel" />
        </div>
      )}

      {/* Filter result count */}
      <div className="flex items-center justify-between border-t px-3 py-1 text-xs text-muted-foreground">
        <span data-testid="message-filter-count">
          {totalAvailable != null
            ? `Showing ${filteredMessages.length} of ${totalAvailable} message(s)`
            : `Showing ${filteredMessages.length} message(s)`}
          {isLoadingMore && <Loader2 className="ml-2 inline h-3 w-3 animate-spin" />}
        </span>
        <button
          data-testid="load-more-button"
          onClick={onLoadMore}
          disabled={!canLoadMore || isLoadingMore}
          className="rounded border px-2 py-0.5 text-xs hover:bg-accent disabled:opacity-50"
        >
          {isLoadingMore ? "Loading…" : canLoadMore ? `Load more (+${prefs.peekCount})` : "All loaded"}
        </button>
        {prefs.autoRefreshInterval > 0 && (
          <span className="ml-2 flex items-center gap-1 text-success" data-testid="auto-refresh-indicator">
            <RotateCw className="h-3 w-3 animate-spin" /> {prefs.autoRefreshInterval}s
          </span>
        )}
      </div>
    </div>
  );
}
