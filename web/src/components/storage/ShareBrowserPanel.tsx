import { useMemo } from "react";
import { File, Folder } from "lucide-react";
import {
    useStorageQueries,
    useStorageShare,
} from "./storage-context";
import { formatBytes } from "@/lib/format-bytes";
import { formatLocalDateTime } from "@/lib/datetime";
import { LastRefreshed } from "@/components/shared/LastRefreshed";

/**
 * Middle-pane browser for Azure File Shares — a deliberately lighter sibling of
 * BlobBrowserPanel: directories and files, breadcrumbs, filter. No upload/copy/
 * recovery for now (file-share mutations aren't implemented backend-side).
 * Share listings are small enough to skip the virtualizer.
 */
export function ShareBrowserPanel() {
    const share = useStorageShare();
    const queries = useStorageQueries();
    const ctx = useMemo(() => ({ ...share, ...queries }), [share, queries]);

    const dirPrefix = ctx.shareDir ? `${ctx.shareDir}/` : "";
    const dirSegments = ctx.shareDir.split("/").filter(Boolean);
    const currentSegment = dirSegments[dirSegments.length - 1] ?? null;

    return (
        <div
            className="flex h-full w-full flex-col overflow-hidden"
            data-testid="storage-share-browser"
        >
            {!ctx.selectedShare ? (
                <div
                    className="flex h-full items-center justify-center text-muted-foreground"
                    data-testid="storage-no-share"
                >
                    Select a file share
                </div>
            ) : (
                <>
                    {/* Breadcrumbs + filter */}
                    <div className="border-b px-3 py-2">
                        <div className="flex items-center gap-1 text-sm">
                            <button
                                data-testid="share-breadcrumb-0"
                                onClick={() => ctx.handleShareBreadcrumb(0)}
                                className="text-primary hover:underline"
                            >
                                {ctx.selectedShare}
                            </button>
                            {dirSegments.map((segment, i) => (
                                <span
                                    key={i}
                                    className="flex items-center gap-1"
                                >
                                    <span className="text-muted-foreground">
                                        /
                                    </span>
                                    {i === dirSegments.length - 1 ? (
                                        <span className="text-muted-foreground">
                                            {segment}
                                        </span>
                                    ) : (
                                        <button
                                            data-testid={`share-breadcrumb-${i + 1}`}
                                            onClick={() =>
                                                ctx.handleShareBreadcrumb(i + 1)
                                            }
                                            className="text-primary hover:underline"
                                        >
                                            {segment}
                                        </button>
                                    )}
                                </span>
                            ))}
                            {currentSegment === null && null}
                        </div>
                        <div className="mt-2 flex items-center gap-2">
                            <input
                                type="text"
                                placeholder="Filter files..."
                                value={ctx.shareFilter}
                                onChange={(e) =>
                                    ctx.setShareFilter(e.target.value)
                                }
                                className="flex-1 rounded border bg-card px-2 py-1 text-xs"
                                data-testid="share-entry-filter"
                            />
                            <LastRefreshed
                                at={ctx.shareEntries.dataUpdatedAt || null}
                                isFetching={ctx.shareEntries.isFetching}
                                testId="share-last-refreshed"
                            />
                        </div>
                    </div>

                    {/* Entry list */}
                    <div
                        className="flex-1 overflow-auto"
                        data-testid="share-entry-list"
                    >
                        {ctx.shareEntries.isLoading && (
                            <div className="px-3 py-2 text-sm text-muted-foreground">
                                Loading files...
                            </div>
                        )}
                        {ctx.shareEntries.error && (
                            <div
                                className="px-3 py-2 text-sm text-destructive"
                                data-testid="share-entry-error"
                            >
                                Error:{" "}
                                {ctx.shareEntries.error instanceof Error
                                    ? ctx.shareEntries.error.message
                                    : String(ctx.shareEntries.error)}
                            </div>
                        )}
                        {!ctx.shareEntries.isLoading &&
                            !ctx.shareEntries.error &&
                            ctx.filteredShareEntries.length === 0 && (
                                <div className="px-3 py-2 text-sm text-muted-foreground">
                                    {ctx.shareFilter
                                        ? "No matching files"
                                        : "This directory is empty"}
                                </div>
                            )}
                        {ctx.filteredShareEntries.map((entry) => (
                            <div
                                key={entry.name}
                                data-testid={`share-item-${entry.name}`}
                                onClick={() =>
                                    entry.isDirectory
                                        ? ctx.handleNavigateShareDir(entry.name)
                                        : ctx.handleSelectShareFile(entry.name)
                                }
                                className={`flex w-full cursor-pointer items-start gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-accent ${
                                    !entry.isDirectory &&
                                    ctx.selectedShareFile === entry.name
                                        ? "bg-accent"
                                        : ""
                                }`}
                            >
                                {entry.isDirectory ? (
                                    <Folder className="mt-0.5 h-3.5 w-3.5 shrink-0 text-blue-400" />
                                ) : (
                                    <File className="mt-0.5 h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                                )}
                                <span
                                    className="min-w-0 flex-1 break-all font-mono"
                                    title={entry.name}
                                >
                                    {entry.name.startsWith(dirPrefix)
                                        ? entry.name.slice(dirPrefix.length)
                                        : entry.name}
                                </span>
                                {!entry.isDirectory && entry.lastModified && (
                                    <span
                                        className="w-32 shrink-0 self-start text-right text-xs text-muted-foreground"
                                        data-testid={`share-item-modified-${entry.name}`}
                                    >
                                        {formatLocalDateTime(entry.lastModified)}
                                    </span>
                                )}
                                {!entry.isDirectory && (
                                    <span className="shrink-0 self-start text-xs text-muted-foreground">
                                        {formatBytes(entry.sizeBytes)}
                                    </span>
                                )}
                            </div>
                        ))}
                    </div>
                </>
            )}
        </div>
    );
}
