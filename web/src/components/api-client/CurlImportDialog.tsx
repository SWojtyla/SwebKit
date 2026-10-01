import { useEffect, useMemo, useRef, useState } from "react";
import { Terminal, X, AlertCircle, CheckCircle } from "lucide-react";
import { useDemoMode } from "@/lib/hooks";
import { useNotification } from "@/components/layout/notification-context";
import { importCurlRequest } from "@/lib/api/apiClient";
import { collectFolderPaths } from "@/lib/collection-tree-utils";
import type { ApiCollection, HttpRequestEntry } from "@/lib/types";

const NEW_COLLECTION = "__new__";

interface CurlImportDialogProps {
    collections: ApiCollection[];
    /** Pre-selects the collection the user had selected in the tree. */
    defaultCollectionId?: string | null;
    /** `collectionIdOrName` is an existing collection's id or the name of a new
     *  one to create; `folderPath` segments that don't exist are created. */
    onImport: (
        request: HttpRequestEntry,
        collectionIdOrName: string,
        folderPath: string | null,
    ) => Promise<void> | void;
    onClose: () => void;
}

export function CurlImportDialog({
    collections,
    defaultCollectionId,
    onImport,
    onClose,
}: CurlImportDialogProps) {
    const { notify } = useNotification();
    const { data: demoMode } = useDemoMode();
    const isDemo = demoMode?.isDemoMode ?? false;

    const [command, setCommand] = useState("");
    const [parsed, setParsed] = useState<HttpRequestEntry | null>(null);
    const [parseError, setParseError] = useState<string | null>(null);
    const [parsing, setParsing] = useState(false);
    const [importing, setImporting] = useState(false);
    const [collectionChoice, setCollectionChoice] = useState<string>(
        () => defaultCollectionId ?? collections[0]?.id ?? NEW_COLLECTION,
    );
    const [newCollectionName, setNewCollectionName] = useState("");
    const [folderPath, setFolderPath] = useState("");
    const parseRun = useRef(0);
    const textareaRef = useRef<HTMLTextAreaElement | null>(null);

    const folderOptions = useMemo(() => {
        const collection = collections.find((c) => c.id === collectionChoice);
        return collection ? collectFolderPaths(collection) : [];
    }, [collections, collectionChoice]);

    useEffect(() => {
        textareaRef.current?.focus();
    }, []);

    useEffect(() => {
        const close = (e: KeyboardEvent) => {
            if (e.key === "Escape") onClose();
        };
        document.addEventListener("keydown", close);
        return () => document.removeEventListener("keydown", close);
    }, [onClose]);

    // Parse live (debounced) so the user sees what the paste resolves to — or why
    // it doesn't — before committing. The endpoint is the local sidecar and
    // parse-only, so a call per keystroke pause is cheap. The parsed/error/parsing
    // resets live in the textarea's onChange, not here.
    useEffect(() => {
        const run = ++parseRun.current;
        const trimmed = command.trim();
        if (!trimmed || isDemo) return;
        const timer = setTimeout(async () => {
            try {
                const request = await importCurlRequest(trimmed);
                if (parseRun.current === run) setParsed(request);
            } catch (err) {
                if (parseRun.current === run) {
                    setParseError(
                        err instanceof Error
                            ? err.message
                            : "Could not parse the cURL command.",
                    );
                }
            } finally {
                if (parseRun.current === run) setParsing(false);
            }
        }, 350);
        return () => clearTimeout(timer);
    }, [command, isDemo]);

    const resolvedCollectionRef =
        collectionChoice === NEW_COLLECTION
            ? newCollectionName.trim()
            : collectionChoice;
    const canImport =
        Boolean(parsed) &&
        !parsing &&
        resolvedCollectionRef.length > 0 &&
        !importing &&
        !isDemo;

    const handleImport = async () => {
        if (!parsed || !canImport) return;
        setImporting(true);
        try {
            await onImport(
                parsed,
                resolvedCollectionRef,
                folderPath.trim() || null,
            );
            notify("success", "cURL imported", `Created "${parsed.name}".`);
            onClose();
        } catch (err) {
            notify(
                "error",
                "cURL import failed",
                err instanceof Error ? err.message : String(err),
            );
            setImporting(false);
        }
    };

    return (
        <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
            data-testid="curl-import-overlay"
        >
            <div
                className="w-[520px] rounded-lg border bg-popover shadow-lg"
                role="dialog"
                aria-modal="true"
                aria-label="Import from cURL"
                data-testid="curl-import-dialog"
            >
                <div className="flex items-center justify-between border-b px-4 py-3">
                    <h2 className="flex items-center gap-2 text-sm font-semibold">
                        <Terminal className="h-4 w-4" /> Import from cURL
                    </h2>
                    <button
                        onClick={onClose}
                        className="text-muted-foreground hover:text-foreground"
                        data-testid="curl-import-close"
                        aria-label="Close"
                    >
                        <X className="h-4 w-4" />
                    </button>
                </div>

                <div className="space-y-3 p-4">
                    {isDemo && (
                        <div className="rounded border border-warning bg-warning/10 px-3 py-2 text-xs text-warning">
                            Import is disabled in demo mode.
                        </div>
                    )}

                    <div className="space-y-1">
                        <label
                            htmlFor="curl-import-input"
                            className="text-xs font-medium text-muted-foreground"
                        >
                            cURL command
                        </label>
                        <textarea
                            id="curl-import-input"
                            ref={textareaRef}
                            value={command}
                            onChange={(e) => {
                                setCommand(e.target.value);
                                setParsed(null);
                                setParseError(null);
                                setParsing(Boolean(e.target.value.trim()));
                            }}
                            placeholder={
                                "curl -X POST 'https://api.example.com/orders' \\\n  -H 'Content-Type: application/json' \\\n  --data-raw '{\"a\":1}'"
                            }
                            rows={6}
                            spellCheck={false}
                            disabled={isDemo}
                            className="w-full resize-y rounded border bg-background px-2 py-1.5 font-mono text-xs disabled:opacity-50"
                            data-testid="curl-import-input"
                        />
                        {parsing && (
                            <p
                                className="text-xs text-muted-foreground"
                                data-testid="curl-import-parsing"
                            >
                                Parsing…
                            </p>
                        )}
                        {parseError && (
                            <div
                                className="flex items-start gap-2 rounded border border-destructive bg-destructive/10 px-3 py-2 text-xs text-destructive"
                                data-testid="curl-import-error"
                            >
                                <AlertCircle className="h-4 w-4 shrink-0" />
                                <span>{parseError}</span>
                            </div>
                        )}
                        {parsed && (
                            <div
                                className="flex items-center gap-2 rounded border bg-muted/50 px-3 py-2 text-xs"
                                data-testid="curl-import-preview"
                            >
                                <CheckCircle className="h-4 w-4 shrink-0 text-success" />
                                <span className="font-mono font-medium">
                                    {parsed.method}
                                </span>
                                <span className="truncate font-mono text-muted-foreground">
                                    {parsed.url}
                                </span>
                            </div>
                        )}
                    </div>

                    <div className="grid grid-cols-2 gap-3">
                        <div className="space-y-1">
                            <label
                                htmlFor="curl-import-collection"
                                className="text-xs font-medium text-muted-foreground"
                            >
                                Collection
                            </label>
                            <select
                                id="curl-import-collection"
                                value={collectionChoice}
                                onChange={(e) =>
                                    setCollectionChoice(e.target.value)
                                }
                                className="w-full rounded border bg-background px-2 py-1.5 text-xs"
                                data-testid="curl-import-collection"
                            >
                                {collections.map((c) => (
                                    <option key={c.id} value={c.id}>
                                        {c.name}
                                    </option>
                                ))}
                                <option value={NEW_COLLECTION}>
                                    + New collection…
                                </option>
                            </select>
                            {collectionChoice === NEW_COLLECTION && (
                                <input
                                    type="text"
                                    value={newCollectionName}
                                    onChange={(e) =>
                                        setNewCollectionName(e.target.value)
                                    }
                                    placeholder="New collection name"
                                    className="w-full rounded border bg-background px-2 py-1.5 text-xs"
                                    data-testid="curl-import-new-collection"
                                />
                            )}
                        </div>
                        <div className="space-y-1">
                            <label
                                htmlFor="curl-import-folder"
                                className="text-xs font-medium text-muted-foreground"
                            >
                                Folder path{" "}
                                <span className="font-normal">(optional)</span>
                            </label>
                            <input
                                id="curl-import-folder"
                                type="text"
                                value={folderPath}
                                onChange={(e) => setFolderPath(e.target.value)}
                                placeholder="e.g. Auth/OAuth2"
                                list="curl-import-folder-options"
                                className="w-full rounded border bg-background px-2 py-1.5 text-xs"
                                data-testid="curl-import-folder"
                            />
                            <datalist id="curl-import-folder-options">
                                {folderOptions.map((path) => (
                                    <option key={path} value={path} />
                                ))}
                            </datalist>
                        </div>
                    </div>
                </div>

                <div className="flex justify-end gap-2 border-t px-4 py-3">
                    <button
                        onClick={onClose}
                        className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
                        data-testid="curl-import-cancel"
                    >
                        Cancel
                    </button>
                    <button
                        onClick={handleImport}
                        disabled={!canImport}
                        title={
                            isDemo
                                ? "Import is not available in demo mode"
                                : !parsed
                                  ? "Paste a valid cURL command first"
                                  : undefined
                        }
                        className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
                        data-testid="curl-import-submit"
                    >
                        {importing ? "Importing…" : "Import"}
                    </button>
                </div>
            </div>
        </div>
    );
}
