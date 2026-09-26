import { useMemo, useState } from "react";
import { Check, Link as LinkIcon } from "lucide-react";
import {
    useStorageActions,
    useStorageQueries,
    useStorageShare,
} from "./storage-context";
import { formatBytes } from "@/lib/format-bytes";
import { formatLocalDateTime } from "@/lib/datetime";
import { tryPrettifyJson } from "@/lib/pretty-json";
import {
    loadViewPreference,
    saveViewPreference,
} from "@/lib/stores/panel-preferences";

const CONTENT_PRETTY_PREF_KEY = "storage-share-content-pretty";

/**
 * Right-pane detail for a file inside an Azure File Share — properties, a text
 * preview (binary files show a notice instead), and a read-only SAS URL action.
 * Mirrors BlobDetailPanel's shape minus blob-only features (versions, metadata
 * editing, recovery) that file shares don't support backend-side.
 */
export function ShareFileDetailPanel() {
    const share = useStorageShare();
    const queries = useStorageQueries();
    const actions = useStorageActions();
    const ctx = useMemo(
        () => ({ ...share, ...queries, ...actions }),
        [share, queries, actions],
    );
    const [prettyPrinted, setPrettyPrinted] = useState<boolean>(() =>
        loadViewPreference<boolean>(CONTENT_PRETTY_PREF_KEY, true),
    );

    const rawContent = ctx.shareFileContent.data?.content ?? "";
    const prettyContent = useMemo(() => tryPrettifyJson(rawContent), [rawContent]);
    const displayedContent =
        prettyPrinted && prettyContent !== null ? prettyContent : rawContent;

    const props = ctx.shareFileProps.data;
    const content = ctx.shareFileContent.data;

    return (
        <div className="h-full w-full overflow-auto" data-testid="share-file-detail">
            {!ctx.selectedShareFile ? (
                <div
                    className="flex h-full items-center justify-center text-muted-foreground"
                    data-testid="share-no-file-selected"
                >
                    Select a file to view details
                </div>
            ) : (
                <div className="space-y-4 p-6">
                    {ctx.shareFileProps.isLoading && (
                        <div className="text-sm text-muted-foreground">
                            Loading file properties...
                        </div>
                    )}
                    {ctx.shareFileProps.error && (
                        <div
                            className="text-sm text-destructive"
                            data-testid="share-file-error"
                        >
                            Error:{" "}
                            {ctx.shareFileProps.error instanceof Error
                                ? ctx.shareFileProps.error.message
                                : String(ctx.shareFileProps.error)}
                        </div>
                    )}
                    {props && (
                        <>
                            <div>
                                <h3
                                    className="break-all font-mono text-sm font-semibold"
                                    data-testid="share-file-name"
                                >
                                    {props.name}
                                </h3>
                            </div>

                            <div className="space-y-1 text-xs">
                                <div className="flex justify-between gap-4">
                                    <span className="text-muted-foreground">Size</span>
                                    <span data-testid="share-file-size">
                                        {formatBytes(props.sizeBytes)}
                                    </span>
                                </div>
                                <div className="flex justify-between gap-4">
                                    <span className="text-muted-foreground">
                                        Content type
                                    </span>
                                    <span className="font-mono">
                                        {props.contentType ?? "—"}
                                    </span>
                                </div>
                                <div className="flex justify-between gap-4">
                                    <span className="text-muted-foreground">
                                        Last modified (local time)
                                    </span>
                                    <span data-testid="share-file-modified">
                                        {formatLocalDateTime(props.lastModified) || "—"}
                                    </span>
                                </div>
                                <div className="flex justify-between gap-4">
                                    <span className="text-muted-foreground">ETag</span>
                                    <span className="truncate font-mono" title={props.eTag ?? undefined}>
                                        {props.eTag ?? "—"}
                                    </span>
                                </div>
                            </div>

                            <div className="flex gap-2">
                                <button
                                    onClick={ctx.handleCopyShareSasUrl}
                                    disabled={ctx.shareFileSasUrl.isLoading}
                                    title={
                                        ctx.shareFileSasUrl.error
                                            ? `Couldn't generate a SAS URL: ${String(ctx.shareFileSasUrl.error)}`
                                            : "Copy a read-only SAS URL for this file"
                                    }
                                    className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                                    data-testid="share-file-copy-sas"
                                >
                                    {ctx.copiedUrl ? (
                                        <Check className="h-3 w-3 text-success" />
                                    ) : (
                                        <LinkIcon className="h-3 w-3" />
                                    )}
                                    {ctx.copiedUrl ? "Copied" : "Copy SAS URL"}
                                </button>
                            </div>

                            {/* Content preview */}
                            <div>
                                <div className="mb-1 flex items-center justify-between">
                                    <h4 className="text-xs font-semibold">Content</h4>
                                    {content?.isBinary === false &&
                                        prettyContent !== null && (
                                            <button
                                                onClick={() => {
                                                    const next = !prettyPrinted;
                                                    setPrettyPrinted(next);
                                                    saveViewPreference(
                                                        CONTENT_PRETTY_PREF_KEY,
                                                        next,
                                                    );
                                                }}
                                                className="rounded border px-2 py-0.5 text-xs hover:bg-accent"
                                                data-testid="share-file-pretty-toggle"
                                            >
                                                {prettyPrinted
                                                    ? "Raw"
                                                    : "Pretty"}
                                            </button>
                                        )}
                                </div>
                                {ctx.shareFileContent.isLoading && (
                                    <div className="text-xs text-muted-foreground">
                                        Loading content...
                                    </div>
                                )}
                                {ctx.shareFileContent.error && (
                                    <div className="text-xs text-destructive">
                                        Couldn't load content:{" "}
                                        {ctx.shareFileContent.error instanceof Error
                                            ? ctx.shareFileContent.error.message
                                            : String(ctx.shareFileContent.error)}
                                    </div>
                                )}
                                {content?.isBinary && (
                                    <div
                                        className="text-xs text-muted-foreground"
                                        data-testid="share-file-binary"
                                    >
                                        Binary file — no text preview. Use the
                                        SAS URL to download it.
                                    </div>
                                )}
                                {content && !content.isBinary && (
                                    <pre
                                        className="max-h-96 overflow-auto rounded border bg-muted/40 p-2 font-mono text-xs"
                                        data-testid="share-file-content"
                                    >
                                        {displayedContent}
                                    </pre>
                                )}
                                {content?.wasTruncated && (
                                    <div className="mt-1 text-xs text-muted-foreground">
                                        Preview truncated — the file is{" "}
                                        {formatBytes(content.totalSizeBytes)}.
                                    </div>
                                )}
                            </div>
                        </>
                    )}
                </div>
            )}
        </div>
    );
}
