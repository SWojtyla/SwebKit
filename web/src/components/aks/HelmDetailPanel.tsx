import { useState } from "react";
import {
    X,
    History,
    Settings2,
    RotateCcw,
    FileText,
    Package,
} from "lucide-react";
import {
    useAksHelmHistory,
    useAksHelmValues,
    useAksHelmNotes,
    useAksHelmManifest,
    useAksHelmRollback,
} from "@/lib/hooks";
import { highlightYaml } from "@/lib/yamlHighlight";
import { formatLocalDate } from "@/lib/datetime";

interface HelmDetailPanelProps {
    ns: string;
    release: string;
    /** Cluster the release lives in — history/values/rollback all route there. */
    context?: string;
    onClose: () => void;
    onRequestConfirm: (opts: {
        message: string;
        resourceName: string;
        onConfirm: () => void;
    }) => void;
    onError?: (message: string) => void;
}

type HelmTab = "history" | "values" | "notes" | "manifest";

export function HelmDetailPanel({
    ns,
    release,
    context,
    onClose,
    onRequestConfirm,
    onError,
}: HelmDetailPanelProps) {
    const [tab, setTab] = useState<HelmTab>("history");
    const { data: history, isLoading: historyLoading } = useAksHelmHistory(
        ns,
        release,
        context,
    );
    const { data: values, isLoading: valuesLoading } = useAksHelmValues(
        ns,
        release,
        context,
    );
    // Gated on their tabs: each of these spawns a `helm` process server-side (~0.5–2s of Helm startup
    // apiece), and the panel opens on History, so most releases never need either.
    const { data: notes, isLoading: notesLoading } = useAksHelmNotes(
        ns,
        release,
        context,
        { enabled: tab === "notes" },
    );
    const { data: manifest, isLoading: manifestLoading } = useAksHelmManifest(
        ns,
        release,
        context,
        { enabled: tab === "manifest" },
    );
    const [valuesTab, setValuesTab] = useState<"user" | "computed">("user");
    const rollback = useAksHelmRollback();

    const requestRollback = (targetRevision: number) => {
        onRequestConfirm({
            message: `Rollback "${release}" to revision ${targetRevision}${context ? ` in ${context}` : ""}? This will re-deploy that revision's chart and values.`,
            resourceName: release,
            onConfirm: () => {
                rollback.mutate(
                    { ns, release, targetRevision, context },
                    {
                        onError: (err) =>
                            onError?.(
                                err instanceof Error
                                    ? err.message
                                    : String(err),
                            ),
                    },
                );
            },
        });
    };

    const tabButton = (id: HelmTab, label: string, icon: React.ReactNode) => (
        <button
            key={id}
            onClick={() => setTab(id)}
            data-testid={`helm-tab-${id}`}
            className={`-mb-px flex items-center gap-1 border-b-2 px-4 py-2 text-sm font-medium ${
                tab === id
                    ? "border-primary text-fg"
                    : "border-transparent text-fg-muted hover:text-fg"
            }`}
        >
            {icon} {label}
        </button>
    );

    return (
        <div className="flex h-full flex-col" data-testid="helm-detail-panel">
            <div className="flex items-center gap-2 border-b px-4 py-2">
                <span className="truncate font-mono text-sm font-semibold">
                    {release}
                </span>
                <span className="text-xs text-fg-muted">Helm Release</span>
                <button
                    onClick={onClose}
                    className="ml-auto inline-flex h-7 w-7 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg"
                >
                    <X className="h-4 w-4" />
                </button>
            </div>

            <div className="flex border-b">
                {tabButton(
                    "history",
                    "History",
                    <History className="h-3.5 w-3.5" />,
                )}
                {tabButton(
                    "values",
                    "Values",
                    <Settings2 className="h-3.5 w-3.5" />,
                )}
                {tabButton(
                    "notes",
                    "Notes",
                    <FileText className="h-3.5 w-3.5" />,
                )}
                {tabButton(
                    "manifest",
                    "Manifest",
                    <Package className="h-3.5 w-3.5" />,
                )}
            </div>

            <div className="flex-1 overflow-auto">
                {tab === "history" && (
                    <div className="p-4">
                        {historyLoading ? (
                            <div className="text-sm text-fg-muted">
                                Loading history...
                            </div>
                        ) : !history || history.length === 0 ? (
                            <div className="text-sm text-fg-muted">
                                No history available
                            </div>
                        ) : (
                            <table
                                className="w-full text-sm"
                                data-testid="helm-history-table"
                            >
                                <thead>
                                    <tr className="h-8 border-b text-left text-xs font-medium uppercase tracking-wide text-fg-muted">
                                        <th className="py-2 pr-4">Revision</th>
                                        <th className="py-2 pr-4">Status</th>
                                        <th className="py-2 pr-4">Chart</th>
                                        <th className="py-2 pr-4">
                                            App Version
                                        </th>
                                        <th className="py-2 pr-4">
                                            Description
                                        </th>
                                        <th className="py-2 pr-4">Updated</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {history.map((h) => (
                                        <tr
                                            key={h.revision}
                                            className="h-9 border-b last:border-0"
                                        >
                                            <td className="py-2 pr-4 font-medium">
                                                {h.revision}
                                            </td>
                                            <td className="py-2 pr-4">
                                                <span
                                                    className={
                                                        h.status === "deployed"
                                                            ? "text-state-done-ink"
                                                            : "text-state-needs-ink"
                                                    }
                                                >
                                                    {h.status}
                                                </span>
                                            </td>
                                            <td className="py-2 pr-4 text-xs text-fg-muted">
                                                {h.chart}
                                            </td>
                                            <td className="py-2 pr-4 text-xs text-fg-muted">
                                                {h.appVersion}
                                            </td>
                                            <td className="py-2 pr-4 text-xs text-fg-muted">
                                                {h.description}
                                            </td>
                                            <td className="py-2 pr-4 text-xs text-fg-muted">
                                                {h.updated
                                                    ? formatLocalDate(h.updated)
                                                    : "—"}
                                            </td>
                                            <td className="py-1 pr-4">
                                                {h.status !== "deployed" && (
                                                    <button
                                                        onClick={() =>
                                                            requestRollback(
                                                                h.revision,
                                                            )
                                                        }
                                                        disabled={
                                                            rollback.isPending
                                                        }
                                                        title={`Rollback to revision ${h.revision}`}
                                                        className="flex h-7 items-center gap-1 rounded-md border bg-surface-1 px-2 text-xs text-fg-muted hover:bg-surface-2 hover:text-fg disabled:cursor-not-allowed disabled:opacity-50"
                                                        data-testid={`helm-rollback-rev-${h.revision}`}
                                                    >
                                                        <RotateCcw className="h-3 w-3" />
                                                        {rollback.isPending &&
                                                        rollback.variables
                                                            ?.targetRevision ===
                                                            h.revision
                                                            ? "Rolling back…"
                                                            : "Rollback"}
                                                    </button>
                                                )}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        )}
                    </div>
                )}
                {tab === "values" && (
                    <div className="p-4">
                        {valuesLoading ? (
                            <div className="text-sm text-fg-muted">
                                Loading values...
                            </div>
                        ) : !values ? (
                            <div className="text-sm text-fg-muted">
                                No values available
                            </div>
                        ) : (
                            <div>
                                <div className="mb-2 flex gap-2">
                                    <button
                                        onClick={() => setValuesTab("user")}
                                        className={`h-7 rounded-md border px-3 text-xs ${valuesTab === "user" ? "border-primary bg-primary text-primary-foreground" : "bg-surface-1 text-fg-muted hover:bg-surface-2 hover:text-fg"}`}
                                    >
                                        User Values
                                    </button>
                                    <button
                                        onClick={() => setValuesTab("computed")}
                                        className={`h-7 rounded-md border px-3 text-xs ${valuesTab === "computed" ? "border-primary bg-primary text-primary-foreground" : "bg-surface-1 text-fg-muted hover:bg-surface-2 hover:text-fg"}`}
                                    >
                                        Computed Values
                                    </button>
                                </div>
                                <pre
                                    className="overflow-auto rounded-md border bg-sunken p-3 text-xs font-mono"
                                    data-testid="helm-values-content"
                                    dangerouslySetInnerHTML={{
                                        __html: highlightYaml(
                                            valuesTab === "user"
                                                ? values.userValues
                                                : values.computedValues,
                                        ),
                                    }}
                                />
                            </div>
                        )}
                    </div>
                )}
                {tab === "notes" && (
                    <div className="p-4">
                        {notesLoading ? (
                            <div className="text-sm text-fg-muted">
                                Loading notes...
                            </div>
                        ) : !notes ? (
                            <div className="text-sm text-fg-muted">
                                No notes available
                            </div>
                        ) : (
                            <pre
                                className="overflow-auto rounded-md border bg-sunken p-3 text-xs font-mono whitespace-pre-wrap"
                                data-testid="helm-notes-content"
                            >
                                {notes.notes || "No release notes provided."}
                            </pre>
                        )}
                    </div>
                )}
                {tab === "manifest" && (
                    <div className="p-4">
                        {manifestLoading ? (
                            <div className="text-sm text-fg-muted">
                                Loading manifest...
                            </div>
                        ) : !manifest ? (
                            <div className="text-sm text-fg-muted">
                                No manifest available
                            </div>
                        ) : (
                            <pre
                                className="overflow-auto rounded-md border bg-sunken p-3 text-xs font-mono"
                                data-testid="helm-manifest-content"
                                dangerouslySetInnerHTML={{
                                    __html: highlightYaml(manifest.manifest),
                                }}
                            />
                        )}
                    </div>
                )}
            </div>
        </div>
    );
}
