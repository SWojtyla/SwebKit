import { useState, useMemo, useEffect, useCallback, useRef } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { AlertCircle, RefreshCw } from "lucide-react";
import { invalidateServiceBusQueries } from "@/lib/hooks";
import { apiSend } from "@/lib/api";
import type { SbEntityInfo, SbMessage } from "@/lib/types";
import { downloadBlob } from "@/lib/download";
import { buildZip } from "@/lib/zip";
import { useNotification } from "@/components/layout/notification-context";
import {
    messageToDownloadObject,
    safeFileName,
    messageKey as sbMessageKey,
} from "./exportHelpers";
import { resendTargetText, sendableEntityPath } from "./resendHelpers";
import { runInChunks } from "./bulkOps";
import { applyFilters, hasActiveFilters } from "./filterLogic";
import type { AdvancedFilterRule } from "./filterTypes";
import { isRuleConfigured, createFilterRule } from "./filterTypes";
import {
    loadSbPreferences,
    saveSbPreferences,
    type SbListPreferences,
    type RowDensity,
} from "@/lib/stores/sb-preferences";
import {
    loadSavedFilters,
    addSavedFilter,
    deleteSavedFilter,
    type SbSavedFilter,
} from "@/lib/stores/sb-filters";
import {
    MessageListToolbar,
    ColumnTogglePanel,
    SessionPinFilter,
    AdvancedFilterSection,
} from "./MessageListToolbar";
import {
    BulkActionBar,
    BulkConfirmBar,
    type PendingBulkConfirm,
    type BulkProgress,
} from "./MessageListBulkBar";
import {
    MessageTableEmpty,
    MessageTableHeader,
    MessageRow,
    MessageListFooter,
    type MessageGridContext,
} from "./MessageListTable";

interface Props {
    nsId: string | null;
    entity: SbEntityInfo | null;
    viewMode: "active" | "dlq";
    messages: SbMessage[];
    isLoading: boolean;
    /** Distinct from "no messages" — a fetch failure must never render like a genuinely empty queue. */
    isError: boolean;
    error: unknown;
    /** Drives the manual-refresh spinner and the `LastRefreshed` freshness indicator. */
    isFetching: boolean;
    onRefresh: () => void;
    lastRefreshedAt: number | null;
    isLoadingMore: boolean;
    canLoadMore: boolean;
    totalAvailable: number | null;
    selectedMessage: SbMessage | null;
    onSelectMessage: (message: SbMessage) => void;
    onLoadMore: () => void;
}

import {
    CHECKBOX_COL_WIDTH,
    COLUMN_DEFS,
    COLUMN_WIDTHS,
    CUSTOM_COLUMN_WIDTH,
    NSB_COLUMN_DEFS,
    ROW_HEIGHT_ESTIMATE,
} from "./message-columns";

export function MessageList({
    nsId,
    entity,
    viewMode,
    messages,
    isLoading,
    isError,
    error,
    isFetching,
    onRefresh,
    lastRefreshedAt,
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
    const [advancedRules, setAdvancedRules] = useState<AdvancedFilterRule[]>(
        [],
    );
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

    const entityPath = entity?.entityPath;

    // Reload prefs when entity changes
    useEffect(() => {
        if (nsId && entityPath) {
            setPrefs(loadSbPreferences(nsId, entityPath));
        }
    }, [nsId, entityPath]);

    // Save prefs on change
    useEffect(() => {
        if (nsId && entityPath) {
            saveSbPreferences(nsId, entityPath, prefs);
        }
    }, [prefs, nsId, entityPath]);

    const visibleColumns = new Set(prefs.visibleColumns);
    const nsbMode = prefs.nsbMode ?? false;

    // Load saved filters when entity changes
    useEffect(() => {
        if (nsId && entityPath) {
            setSavedFilters(loadSavedFilters(nsId, entityPath));
        }
    }, [nsId, entityPath]);

    // Auto-refresh
    useEffect(() => {
        if (prefs.autoRefreshInterval === 0 || !nsId || !entity) return;
        const id = setInterval(() => {
            qc.invalidateQueries({
                queryKey: ["sb-peek", nsId, entity.entityPath],
            });
            qc.invalidateQueries({
                queryKey: ["sb-dlq", nsId, entity.entityPath],
            });
            qc.invalidateQueries({
                queryKey: ["sb-entity-stats", nsId, entity.entityPath],
            });
        }, prefs.autoRefreshInterval * 1000);
        return () => clearInterval(id);
    }, [prefs.autoRefreshInterval, nsId, entity, qc]);

    // Infinite-scroll sentinel
    useEffect(() => {
        if (
            !sentinelRef.current ||
            !listRef.current ||
            !canLoadMore ||
            isLoadingMore
        )
            return;
        const observer = new IntersectionObserver(
            ([entry]) => {
                if (entry.isIntersecting) onLoadMore();
            },
            { root: listRef.current, threshold: 0 },
        );
        observer.observe(sentinelRef.current);
        return () => observer.disconnect();
    }, [canLoadMore, isLoadingMore, onLoadMore]);

    // Bulk action mutations — the chunked runner below POSTs through apiSend
    // directly rather than the mutation hooks: per-chunk onSuccess would
    // invalidate (and refetch) the entity queries once per chunk, so the loop
    // invalidates once when the whole run ends.
    const [pendingBulkConfirm, setPendingBulkConfirm] =
        useState<PendingBulkConfirm | null>(null);
    // Non-null while a chunked bulk run is in flight — drives the progress
    // indicator and disables the action buttons for the run's duration.
    const [bulkProgress, setBulkProgress] = useState<BulkProgress | null>(null);

    const handleBulkComplete = useCallback(() => {
        if (!nsId || !entity || selectedMsgs.size === 0) return;
        const seqNumbers = messages
            .filter((m) => selectedMsgs.has(sbMessageKey(m)))
            .map((m) => m.sequenceNumber)
            .filter((n): n is number => n !== null);
        if (seqNumbers.length === 0) return;
        setPendingBulkConfirm({ kind: "complete", seqNumbers });
    }, [nsId, entity, selectedMsgs, messages]);

    const handleBulkResubmit = useCallback(() => {
        if (!nsId || !entity || selectedMsgs.size === 0) return;
        const seqNumbers = messages
            .filter((m) => selectedMsgs.has(sbMessageKey(m)))
            .map((m) => m.sequenceNumber)
            .filter((n): n is number => n !== null);
        if (seqNumbers.length === 0) return;
        setPendingBulkConfirm({ kind: "resubmit", seqNumbers });
    }, [nsId, entity, selectedMsgs, messages]);

    // Dead-letter is the broker's own move-to-DLQ settlement
    // (DeadLetterMessageAsync): the message leaves the active list and lands in
    // the entity's DLQ with a recorded reason — not a copy-and-delete. Only
    // meaningful on the active view.
    const handleBulkDeadLetter = useCallback(() => {
        if (!nsId || !entity || selectedMsgs.size === 0) return;
        const seqNumbers = messages
            .filter((m) => selectedMsgs.has(sbMessageKey(m)))
            .map((m) => m.sequenceNumber)
            .filter((n): n is number => n !== null);
        if (seqNumbers.length === 0) return;
        setPendingBulkConfirm({ kind: "deadletter", seqNumbers });
    }, [nsId, entity, selectedMsgs, messages]);

    // Resend is move-to-origin semantics: the sidecar forwards each selected
    // message to the queue it failed in (NServiceBus.FailedQ, falling back to this
    // entity) with a fresh MessageId, then removes the original — so resending
    // never leaves a duplicate. The selection is captured at click time so the
    // confirm text and sequence numbers stay stable even if the list refreshes
    // before confirm.
    const handleBulkResend = useCallback(() => {
        if (!nsId || !entity || selectedMsgs.size === 0) return;
        const selected = messages.filter((m) =>
            selectedMsgs.has(sbMessageKey(m)),
        );
        if (selected.length === 0) return;
        setPendingBulkConfirm({ kind: "resend", messages: selected });
    }, [nsId, entity, selectedMsgs, messages]);

    // Bulk runs are chunked so the progress bar reflects real completed work —
    // a single request gives no signal until it finishes. The selection stays
    // checked for the duration so the bar (which only renders with a selection)
    // keeps showing progress; errors stop at the failed chunk with earlier
    // chunks already applied (the ops are per-message, never atomic).
    const runPendingBulkConfirm = useCallback(async () => {
        if (!nsId || !entity || !pendingBulkConfirm || bulkProgress) return;
        const action = pendingBulkConfirm;
        setPendingBulkConfirm(null);

        const base = `/api/servicebus/${nsId}/entities/${encodeURIComponent(entity.entityPath)}`;
        let label: string;
        let failTitle: string;
        let successText: string;
        let run: (chunk: number[]) => Promise<unknown>;
        let items: number[];
        if (action.kind === "resend") {
            const seqNumbers = action.messages
                .map((m) => m.sequenceNumber)
                .filter((n): n is number => n !== null);
            if (seqNumbers.length === 0) return;
            items = seqNumbers;
            const target = resendTargetText(action.messages, entity.entityPath);
            label = "Resending";
            failTitle = "Couldn't resend messages";
            successText = `Resent ${items.length} message(s) to ${target}`;
            run = (chunk) =>
                apiSend(`${base}/resend`, "POST", {
                    sequenceNumbers: chunk.map(String),
                    deadLetter: viewMode === "dlq",
                });
        } else {
            items = action.seqNumbers;
            if (action.kind === "complete") {
                label = "Completing";
                failTitle = "Couldn't complete messages";
                successText = `Completed ${items.length} message(s)`;
                run = (chunk) =>
                    viewMode === "active"
                        ? apiSend(`${base}/complete`, "POST", chunk)
                        : apiSend(
                              `${base}/dlq/complete`,
                              "POST",
                              chunk.map(String),
                          );
            } else if (action.kind === "deadletter") {
                label = "Dead-lettering";
                failTitle = "Couldn't dead-letter messages";
                successText = `Moved ${items.length} message(s) to the dead-letter queue`;
                run = (chunk) => apiSend(`${base}/deadletter`, "POST", chunk);
            } else {
                label = "Resubmitting";
                failTitle = "Couldn't resubmit messages";
                successText = `Resubmitted ${items.length} message(s) to ${sendableEntityPath(entity)}`;
                run = (chunk) =>
                    apiSend(`${base}/resubmit`, "POST", {
                        sequenceNumbers: chunk.map(String),
                        targetEntityPath: null,
                    });
            }
        }

        let done = 0;
        try {
            await runInChunks(items, run, (d, total) => {
                done = d;
                setBulkProgress({ label, done: d, total });
            });
            notify("success", successText);
        } catch (err) {
            // When some chunks landed before the failure, say how far the run
            // got so it doesn't look like nothing happened.
            notify(
                "error",
                failTitle,
                done > 0
                    ? `${String(err)} — ${done} of ${items.length} message(s) processed before failing`
                    : String(err),
            );
        } finally {
            setSelectedMsgs(new Set());
            setBulkProgress(null);
            invalidateServiceBusQueries(qc, nsId, entity.entityPath);
        }
    }, [nsId, entity, viewMode, pendingBulkConfirm, bulkProgress, qc, notify]);

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
            setSelectedMsgs(
                new Set(filteredMessages.map((m) => sbMessageKey(m))),
            );
        }
    };

    const addCustomColumn = () => {
        const col = customColumnInput.trim();
        if (!col || prefs.customColumns.includes(col)) return;
        setPrefs((p) => ({ ...p, customColumns: [...p.customColumns, col] }));
        setCustomColumnInput("");
    };

    const removeCustomColumn = (col: string) => {
        setPrefs((p) => ({
            ...p,
            customColumns: p.customColumns.filter((c) => c !== col),
        }));
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
        messages.forEach((m) =>
            Object.keys(m.applicationProperties).forEach((k) => allKeys.add(k)),
        );
        return [...allKeys]
            .filter((k) => !prefs.customColumns.includes(k))
            .slice(0, 10);
    }, [messages, prefs.customColumns]);

    const filteredMessages = useMemo(() => {
        if (!filtersEnabled) return messages;
        return applyFilters(
            messages,
            textFilter,
            advancedRules,
            advancedEnabled,
            pinnedSessionId,
        );
    }, [
        messages,
        textFilter,
        advancedRules,
        advancedEnabled,
        pinnedSessionId,
        filtersEnabled,
    ]);

    const activeRuleCount = advancedRules.filter(
        (r) => r.enabled && isRuleConfigured(r),
    ).length;

    const canSaveFilter = hasActiveFilters(
        textFilter,
        pinnedSessionId,
        advancedRules,
    );

    const clearAllFilters = () => {
        setTextFilter("");
        setPinnedSessionId(null);
        setAdvancedRules([]);
    };

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
        const entitySlug = safeFileName(
            entity.name || entity.entityPath || "messages",
        );
        const timestamp = new Date()
            .toISOString()
            .slice(0, 19)
            .replace(/[T:]/g, "-");
        const fileName = `${entitySlug}-${scope}-${timestamp}.zip`;
        downloadBlob(fileName, zipped);
        notify(
            "success",
            `Downloaded ${messagesToDownload.length} message(s) as ZIP`,
        );
    }, [entity, messages, filteredMessages, selectedMsgs, notify]);

    // Columns actually rendered given current view mode + toggles, and the
    // shared grid template both the header and every virtualized row use so
    // columns stay aligned across independently-positioned row elements.
    const activeColumnDefs = COLUMN_DEFS.filter(
        (col) =>
            visibleColumns.has(col.key) && (!col.dlqOnly || viewMode === "dlq"),
    );
    const gridTemplateColumns = [
        CHECKBOX_COL_WIDTH,
        ...activeColumnDefs.map((col) => COLUMN_WIDTHS[col.key] ?? "140px"),
        ...prefs.customColumns.map(() => CUSTOM_COLUMN_WIDTH),
        ...(nsbMode
            ? NSB_COLUMN_DEFS.map((col) => COLUMN_WIDTHS[col.key] ?? "160px")
            : []),
    ].join(" ");

    const grid: MessageGridContext = {
        gridTemplateColumns,
        activeColumnDefs,
        customColumns: prefs.customColumns,
        nsbMode,
        rowDensity: prefs.rowDensity,
        viewMode,
    };

    const rowVirtualizer = useVirtualizer({
        count: filteredMessages.length,
        getScrollElement: () => listRef.current,
        estimateSize: () => ROW_HEIGHT_ESTIMATE[prefs.rowDensity],
        getItemKey: (index) => sbMessageKey(filteredMessages[index]),
        measureElement: (el) =>
            el?.getBoundingClientRect().height ??
            ROW_HEIGHT_ESTIMATE[prefs.rowDensity],
    });

    if (!entity) {
        return (
            <div
                className="flex h-full items-center justify-center text-sm text-muted-foreground"
                data-testid="message-list-empty"
            >
                Select an entity
            </div>
        );
    }

    if (isLoading) {
        return (
            <div
                className="p-4 text-sm text-muted-foreground"
                data-testid="message-list-loading"
            >
                Loading messages...
            </div>
        );
    }

    // A fetch failure must never render like a genuinely empty queue — that's precisely the class
    // of bug this initiative exists to fix for a tool whose purpose is telling the truth about
    // what's really in a queue.
    if (isError) {
        return (
            <div
                className="flex h-full flex-col items-center justify-center gap-2 p-4 text-center"
                data-testid="message-list-error"
            >
                <AlertCircle className="h-6 w-6 shrink-0 text-destructive" />
                <p className="text-sm font-medium text-destructive">
                    Couldn't load messages
                </p>
                <p className="text-xs text-muted-foreground">
                    {error instanceof Error ? error.message : String(error)}
                </p>
                <button
                    onClick={onRefresh}
                    className="mt-1 flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
                    data-testid="message-list-retry"
                >
                    <RefreshCw className="h-3 w-3" /> Retry
                </button>
            </div>
        );
    }

    return (
        <div
            className="flex h-full flex-col"
            data-testid="message-list-container"
        >
            <MessageListToolbar
                textFilter={textFilter}
                onTextFilterChange={setTextFilter}
                savedFilters={savedFilters}
                showSavedFilters={showSavedFilters}
                canSaveFilter={canSaveFilter}
                showSaveFilterInput={showSaveFilterInput}
                saveFilterName={saveFilterName}
                onToggleSavedFilters={() =>
                    setShowSavedFilters(!showSavedFilters)
                }
                onShowSaveFilterInput={setShowSaveFilterInput}
                onSaveFilterNameChange={setSaveFilterName}
                onApplySavedFilter={(f) => {
                    setTextFilter(f.text);
                    setFiltersEnabled(f.filtersEnabled);
                    setAdvancedEnabled(f.advancedEnabled);
                    setAdvancedRules(f.advancedRules);
                    setPinnedSessionId(f.pinnedSessionId);
                    setShowSavedFilters(false);
                }}
                onDeleteSavedFilter={(f) => {
                    if (!nsId || !entity) return;
                    setSavedFilters(
                        deleteSavedFilter(nsId, entity.entityPath, f.name),
                    );
                }}
                onSaveFilter={handleSaveFilter}
                prefs={prefs}
                onPrefsChange={setPrefs}
                nsbMode={nsbMode}
                filtersEnabled={filtersEnabled}
                onToggleFiltersEnabled={() =>
                    setFiltersEnabled(!filtersEnabled)
                }
                advancedEnabled={advancedEnabled}
                onToggleAdvanced={() => setAdvancedEnabled((prev) => !prev)}
                activeRuleCount={activeRuleCount}
                anyFiltersActive={hasActiveFilters(
                    textFilter,
                    pinnedSessionId,
                    advancedRules,
                )}
                onClearAllFilters={clearAllFilters}
                onAddRule={() =>
                    setAdvancedRules((rules) => [...rules, createFilterRule()])
                }
                showColumnToggle={showColumnToggle}
                onToggleColumnPanel={() =>
                    setShowColumnToggle(!showColumnToggle)
                }
                onDownloadZip={handleDownloadZip}
                downloadDisabled={
                    filteredMessages.length === 0 || isLoadingMore
                }
            />

            {showColumnToggle && (
                <ColumnTogglePanel
                    prefs={prefs}
                    onPrefsChange={setPrefs}
                    visibleColumns={visibleColumns}
                    suggestedColumns={suggestedColumns}
                    customColumnInput={customColumnInput}
                    onCustomColumnInputChange={setCustomColumnInput}
                    onAddCustomColumn={addCustomColumn}
                    onRemoveCustomColumn={removeCustomColumn}
                    onToggleBuiltInColumn={toggleBuiltInColumn}
                />
            )}

            <SessionPinFilter
                pinnedSessionId={pinnedSessionId}
                onChange={setPinnedSessionId}
            />

            {advancedEnabled && (
                <AdvancedFilterSection
                    rules={advancedRules}
                    onChange={setAdvancedRules}
                />
            )}

            {selectedMsgs.size > 0 && (
                <BulkActionBar
                    selectedCount={selectedMsgs.size}
                    filteredCount={filteredMessages.length}
                    bulkProgress={bulkProgress}
                    viewMode={viewMode}
                    entity={entity}
                    onToggleSelectAll={toggleSelectAll}
                    onResend={handleBulkResend}
                    onResubmit={handleBulkResubmit}
                    onDeadLetter={handleBulkDeadLetter}
                    onComplete={handleBulkComplete}
                    onClearSelection={() => setSelectedMsgs(new Set())}
                />
            )}

            {pendingBulkConfirm && (
                <BulkConfirmBar
                    pending={pendingBulkConfirm}
                    entity={entity}
                    onConfirm={runPendingBulkConfirm}
                    onCancel={() => setPendingBulkConfirm(null)}
                />
            )}

            {/* Message list — a real data table (columns, not a stacked card per
          message), matching the MAUI grid's dense spreadsheet layout. The scroll
          container + virtualizer stay here because both own refs/effects; the
          header and row bodies live in MessageListTable.tsx. */}
            {filteredMessages.length === 0 ? (
                <MessageTableEmpty
                    sourceEmpty={messages.length === 0}
                    viewMode={viewMode}
                />
            ) : (
                <div
                    ref={listRef}
                    className="flex-1 min-h-0 overflow-auto text-xs"
                    data-testid="message-list"
                    role="table"
                    aria-label="Messages"
                >
                    <MessageTableHeader
                        grid={grid}
                        allSelected={
                            selectedMsgs.size > 0 &&
                            selectedMsgs.size === filteredMessages.length
                        }
                        onToggleSelectAll={toggleSelectAll}
                    />
                    <div
                        style={{
                            height: `${rowVirtualizer.getTotalSize()}px`,
                            position: "relative",
                            width: "100%",
                        }}
                        data-testid="message-list-virtualizer"
                        role="rowgroup"
                    >
                        {rowVirtualizer.getVirtualItems().map((virtualRow) => {
                            const msg = filteredMessages[virtualRow.index];
                            const msgKey = sbMessageKey(msg);
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
                                    <MessageRow
                                        msg={msg}
                                        grid={grid}
                                        isSelected={selectedMsgs.has(msgKey)}
                                        isActive={
                                            selectedMessage?.messageId ===
                                                msg.messageId &&
                                            selectedMessage?.sequenceNumber ===
                                                msg.sequenceNumber
                                        }
                                        onSelect={onSelectMessage}
                                        onToggleSelect={toggleSelect}
                                    />
                                </div>
                            );
                        })}
                    </div>
                    <div
                        ref={sentinelRef}
                        className="h-1"
                        data-testid="message-load-sentinel"
                    />
                </div>
            )}

            <MessageListFooter
                filteredCount={filteredMessages.length}
                totalAvailable={totalAvailable}
                isLoadingMore={isLoadingMore}
                canLoadMore={canLoadMore}
                onLoadMore={onLoadMore}
                peekCount={prefs.peekCount}
                onRefresh={onRefresh}
                isFetching={isFetching}
                lastRefreshedAt={lastRefreshedAt}
                autoRefreshInterval={prefs.autoRefreshInterval}
            />
        </div>
    );
}
