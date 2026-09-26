import { Loader2, AlertCircle, RefreshCw, RotateCw } from "lucide-react";
import type { SbMessage } from "@/lib/types";
import { LastRefreshed } from "@/components/shared/LastRefreshed";
import {
    NSB_COLUMN_DEFS,
    densityClass,
    type ColumnDef,
} from "./message-columns";
import type { RowDensity } from "@/lib/stores/sb-preferences";

/** Shared column context for the table header and every virtualized row — the grid template
 * and the active column lists must be byte-identical across both or columns drift apart. */
export interface MessageGridContext {
    gridTemplateColumns: string;
    activeColumnDefs: ColumnDef[];
    customColumns: string[];
    nsbMode: boolean;
    rowDensity: RowDensity;
    viewMode: "active" | "dlq";
}

export function MessageTableEmpty({
    sourceEmpty,
    viewMode,
}: {
    /** messages.length === 0 — distinguishes "queue is empty" from "filtered to nothing". */
    sourceEmpty: boolean;
    viewMode: "active" | "dlq";
}) {
    return (
        <div
            className="flex h-full items-center justify-center text-sm text-muted-foreground"
            data-testid={
                sourceEmpty
                    ? "message-list-no-messages"
                    : "message-list-no-matches"
            }
        >
            {sourceEmpty
                ? `No ${viewMode === "dlq" ? "dead-lettered" : "active"} messages`
                : "No messages match the current filters"}
        </div>
    );
}

export function MessageTableHeader({
    grid,
    allSelected,
    onToggleSelectAll,
}: {
    grid: MessageGridContext;
    allSelected: boolean;
    onToggleSelectAll: () => void;
}) {
    const headerCell =
        "flex items-center whitespace-nowrap px-2 py-1.5 text-left font-medium text-muted-foreground";
    return (
        <div
            className="sticky top-0 z-10 grid border-b bg-card"
            style={{ gridTemplateColumns: grid.gridTemplateColumns }}
            role="row"
        >
            <div
                className="flex items-center px-2 py-1.5"
                role="columnheader"
            >
                <input
                    type="checkbox"
                    checked={allSelected}
                    onChange={onToggleSelectAll}
                    data-testid="message-select-all-checkbox"
                />
            </div>
            {grid.activeColumnDefs.map((col) => (
                <div key={col.key} className={headerCell} role="columnheader">
                    {col.label}
                </div>
            ))}
            {grid.customColumns.map((col) => (
                <div key={col} className={headerCell} role="columnheader">
                    {col}
                </div>
            ))}
            {grid.nsbMode &&
                NSB_COLUMN_DEFS.map((col) => (
                    <div
                        key={col.key}
                        className={headerCell}
                        role="columnheader"
                    >
                        {col.label}
                    </div>
                ))}
        </div>
    );
}

export function MessageRow({
    msg,
    grid,
    isSelected,
    isActive,
    onSelect,
    onToggleSelect,
}: {
    msg: SbMessage;
    grid: MessageGridContext;
    isSelected: boolean;
    isActive: boolean;
    onSelect: (message: SbMessage) => void;
    onToggleSelect: (message: SbMessage) => void;
}) {
    const density = densityClass[grid.rowDensity];
    return (
        <div
            data-testid={`message-item-${msg.sequenceNumber}`}
            onClick={() => onSelect(msg)}
            role="row"
            className={`grid cursor-pointer border-b hover:bg-accent ${isActive ? "bg-accent" : ""}`}
            style={{ gridTemplateColumns: grid.gridTemplateColumns }}
        >
            <div
                className={`flex items-center px-2 ${density}`}
                onClick={(e) => e.stopPropagation()}
                role="cell"
            >
                <input
                    type="checkbox"
                    checked={isSelected}
                    onChange={() => onToggleSelect(msg)}
                    data-testid={`message-checkbox-${msg.sequenceNumber}`}
                />
            </div>
            {grid.activeColumnDefs.map((col) => {
                const value = col.render(msg);
                const isDelivery = col.key === "deliveryCount";
                const isDlqReason = col.key === "deadLetterReason";
                return (
                    <div
                        key={col.key}
                        title={value}
                        role="cell"
                        className={`flex min-w-0 items-center truncate px-2 ${density} ${col.className ?? ""} ${
                            isDelivery &&
                            grid.viewMode === "dlq" &&
                            msg.deliveryCount > 0
                                ? "text-destructive"
                                : ""
                        } ${isDlqReason ? "text-destructive" : ""}`}
                    >
                        {isDlqReason && msg.deadLetterReason && (
                            <AlertCircle className="mr-1 inline h-3 w-3 shrink-0" />
                        )}
                        <span className="truncate">{value}</span>
                    </div>
                );
            })}
            {grid.customColumns.map((col) => {
                const val = msg.applicationProperties[col];
                const display =
                    val === undefined || val === null ? "-" : String(val);
                return (
                    <div
                        key={col}
                        title={display}
                        role="cell"
                        className={`flex min-w-0 items-center truncate px-2 text-muted-foreground ${density}`}
                    >
                        <span className="truncate">{display}</span>
                    </div>
                );
            })}
            {grid.nsbMode &&
                NSB_COLUMN_DEFS.map((col) => {
                    const value = col.render(msg);
                    return (
                        <div
                            key={col.key}
                            title={value}
                            role="cell"
                            className={`flex min-w-0 items-center truncate px-2 text-muted-foreground ${density} ${col.className ?? ""}`}
                        >
                            <span className="truncate">{value}</span>
                        </div>
                    );
                })}
        </div>
    );
}

export interface MessageListFooterProps {
    filteredCount: number;
    totalAvailable: number | null;
    isLoadingMore: boolean;
    canLoadMore: boolean;
    onLoadMore: () => void;
    peekCount: number;
    onRefresh: () => void;
    isFetching: boolean;
    lastRefreshedAt: number | null;
    autoRefreshInterval: number;
}

export function MessageListFooter(p: MessageListFooterProps) {
    return (
        <div className="flex items-center justify-between border-t px-3 py-1 text-xs text-muted-foreground">
            <span data-testid="message-filter-count">
                {p.totalAvailable != null
                    ? `Showing ${p.filteredCount} of ${p.totalAvailable} message(s)`
                    : `Showing ${p.filteredCount} message(s)`}
                {p.isLoadingMore && (
                    <Loader2 className="ml-2 inline h-3 w-3 animate-spin" />
                )}
            </span>
            <button
                data-testid="load-more-button"
                onClick={p.onLoadMore}
                disabled={!p.canLoadMore || p.isLoadingMore}
                title={
                    p.isLoadingMore
                        ? "Loading…"
                        : !p.canLoadMore
                          ? "All messages are loaded"
                          : undefined
                }
                className="rounded border px-2 py-0.5 text-xs hover:bg-accent disabled:opacity-50"
            >
                {p.isLoadingMore
                    ? "Loading…"
                    : p.canLoadMore
                      ? `Load more (+${p.peekCount})`
                      : "All loaded"}
            </button>
            {/* Manual refresh + freshness indicator — previously only visible via the spinning
        auto-refresh indicator below, which doesn't exist at all when auto-refresh is off
        (the default), leaving no way to tell a stale view from a fresh one. */}
            <button
                onClick={p.onRefresh}
                disabled={p.isFetching}
                title="Refresh messages"
                data-testid="message-list-refresh"
                className="ml-2 flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-accent disabled:opacity-50"
            >
                <RefreshCw
                    className={`h-3 w-3 ${p.isFetching ? "animate-spin" : ""}`}
                />
            </button>
            <LastRefreshed
                at={p.lastRefreshedAt}
                isFetching={p.isFetching}
                testId="message-list-last-refreshed"
            />
            {p.autoRefreshInterval > 0 && (
                <span
                    className="ml-2 flex items-center gap-1 text-success"
                    data-testid="auto-refresh-indicator"
                >
                    <RotateCw className="h-3 w-3 animate-spin" />{" "}
                    {p.autoRefreshInterval}s
                </span>
            )}
        </div>
    );
}
