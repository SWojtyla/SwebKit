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
import { tryPrettifyXml } from "@/lib/pretty-xml";
import { tokenizeBody } from "@/lib/bodyHighlight";
import { HIGHLIGHT_MAX_BYTES } from "@/lib/response-body";
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
    // JSON first, XML second — share files are commonly minified exports in either.
    const detected = useMemo(() => {
        const json = tryPrettifyJson(rawContent);
        if (json !== null) return { language: "json" as const, pretty: json };
        const xml = tryPrettifyXml(rawContent);
        if (xml !== null) return { language: "xml" as const, pretty: xml };
        return null;
    }, [rawContent]);
    const displayedContent =
        prettyPrinted && detected !== null ? detected.pretty : rawContent;
    // Syntax highlight whenever the payload type was recognised — under the shared
    // cap where tokenizing stays cheap (preview content is server-capped below it).
    const contentTokens = useMemo(
        () =>
            detected !== null && displayedContent.length <= HIGHLIGHT_MAX_BYTES
                ? tokenizeBody(displayedContent, detected.language)
                : null,
        [detected, displayedContent],
    );

    const props = ctx.shareFileProps.data;
    const content = ctx.shareFileContent.data;

    return (
        <div
            className="flex h-full w-full flex-col overflow-hidden"
            data-testid="share-file-detail"
        >
            {!ctx.selectedShareFile ? (
                <div
                    className="flex h-full items-center justify-center text-muted-foreground"
                    data-testid="share-no-file-selected"
                >
                    Select a file to view details
                </div>
            ) : (
                <>
                    <div className="shrink-0 space-y-4 p-6 pb-4">
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
                            </>
                        )}
                    </div>

                    {/* Content preview — fills the remaining pane height; the viewer wraps
                        long lines so minified exports don't degenerate to one scroll line. */}
                    <div className="flex min-h-0 flex-1 flex-col border-t px-6 py-3">
                        <div className="mb-1 flex items-center justify-between">
                            <h4 className="text-xs font-semibold">Content</h4>
                            {content?.isBinary === false &&
                                detected !== null && (
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
                                className="min-h-0 flex-1 overflow-auto whitespace-pre-wrap break-words rounded border bg-muted/40 p-2 font-mono text-xs"
                                data-testid="share-file-content"
                            >
                                {contentTokens
                                    ? contentTokens.map((token, i) => (
                                          <span key={i} className={`tok-${token.cls}`}>
                                              {token.text}
                                          </span>
                                      ))
                                    : displayedContent}
                            </pre>
                        )}
                        {content?.wasTruncated && (
                            <div className="mt-1 shrink-0 text-xs text-muted-foreground">
                                Preview truncated — the file is{" "}
                                {formatBytes(content.totalSizeBytes)}.
                            </div>
                        )}
                    </div>
                </>
            )}
        </div>
    );
}
