import { useState } from "react";
import { RefreshCw } from "lucide-react";
import {
    useAccessEntryRefresh,
    useAccessReport,
    useAccessReportRefresh,
    useAccessRequestArtifact,
} from "@/lib/hooks";
import {
    accessStatusLabel,
    capabilityDisplayName,
    featureAreaLabel,
} from "@/lib/access-format";
import type {
    AccessProbeResult,
    AccessReportEntry,
    AccessRequestArtifact,
    AccessStatus,
} from "@/lib/types";
import { AccessRequestDialog } from "./AccessRequestDialog";

/** testid-safe key — connection keys can be ARM ids containing `/`. */
function tid(value: string): string {
    return value.replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/-+/g, "-").replace(/^-|-$/g, "");
}

const STATUS_DOT: Record<AccessStatus, string> = {
    Ok: "bg-success",
    Denied: "bg-destructive",
    Unknown: "bg-muted-foreground/50",
};

const STATUS_TEXT: Record<AccessStatus, string> = {
    Ok: "text-success",
    Denied: "text-destructive",
    Unknown: "text-muted-foreground",
};

export function AccessSettings() {
    const report = useAccessReport();
    const refreshAll = useAccessReportRefresh();
    const refreshEntry = useAccessEntryRefresh();
    const requestArtifact = useAccessRequestArtifact();
    const [artifact, setArtifact] = useState<AccessRequestArtifact | null>(null);

    return (
        <div className="space-y-4" data-testid="access-settings">
            <div className="flex items-center justify-between">
                <h2 className="text-lg font-semibold">Access</h2>
                <button
                    onClick={() => refreshAll.mutate()}
                    disabled={refreshAll.isPending || report.isFetching}
                    className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
                    data-testid="access-refresh"
                >
                    <RefreshCw
                        className={`h-3.5 w-3.5 ${refreshAll.isPending || report.isFetching ? "animate-spin" : ""}`}
                    />
                    {refreshAll.isPending || report.isFetching ? "Probing…" : "Re-check all"}
                </button>
            </div>

            <p className="text-xs text-muted-foreground">
                What your signed-in identity can do on each configured connection.
                Denied rows carry the least-privilege fix; "Unknown" means the check
                couldn't run — it is never reported as denied.
            </p>

            {report.isLoading && (
                <p className="text-sm text-muted-foreground" data-testid="access-loading">
                    Probing connections…
                </p>
            )}
            {report.isError && (
                <p className="text-sm text-destructive" data-testid="access-error">
                    {report.error instanceof Error
                        ? report.error.message
                        : String(report.error)}
                </p>
            )}

            {report.data && (
                <>
                    <p className="text-xs text-muted-foreground" data-testid="access-generated-at">
                        Checked {new Date(report.data.generatedAt).toLocaleTimeString()} —
                        results are cached for 5 minutes.
                    </p>
                    {report.data.entries.length === 0 && (
                        <p className="text-sm text-muted-foreground" data-testid="access-empty">
                            No connections configured — add one on the other tabs and it shows
                            up here.
                        </p>
                    )}
                    <div className="space-y-3" data-testid="access-report">
                        {report.data.entries.map((entry) => (
                            <EntryCard
                                key={`${entry.featureArea}|${entry.connectionKey}`}
                                entry={entry}
                                onRefresh={() =>
                                    refreshEntry.mutate({
                                        featureArea: entry.featureArea,
                                        connectionKey: entry.connectionKey,
                                    })
                                }
                                refreshing={
                                    refreshEntry.isPending &&
                                    refreshEntry.variables?.connectionKey ===
                                        entry.connectionKey &&
                                    refreshEntry.variables?.featureArea ===
                                        entry.featureArea
                                }
                                onRequest={(capability) =>
                                    requestArtifact.mutate(
                                        {
                                            featureArea: entry.featureArea,
                                            connectionKey: entry.connectionKey,
                                            capability,
                                        },
                                        { onSuccess: setArtifact },
                                    )
                                }
                                requesting={requestArtifact.isPending}
                            />
                        ))}
                    </div>
                </>
            )}

            {artifact && (
                <AccessRequestDialog
                    artifact={artifact}
                    onClose={() => setArtifact(null)}
                />
            )}
        </div>
    );
}

interface EntryCardProps {
    entry: AccessReportEntry;
    onRefresh: () => void;
    refreshing: boolean;
    onRequest: (capability: string) => void;
    requesting: boolean;
}

function EntryCard({ entry, onRefresh, refreshing, onRequest, requesting }: EntryCardProps) {
    const entryTid = `access-entry-${entry.featureArea}-${tid(entry.connectionKey)}`;
    return (
        <section className="rounded-lg border" data-testid={entryTid}>
            <header className="flex items-center gap-2 border-b px-4 py-2.5">
                <span
                    className="rounded bg-muted px-1.5 py-0.5 text-[11px] font-medium text-muted-foreground"
                    data-testid={`${entryTid}-area`}
                >
                    {featureAreaLabel(entry.featureArea)}
                </span>
                <span className="text-sm font-medium" data-testid={`${entryTid}-label`}>
                    {entry.label}
                </span>
                <button
                    onClick={onRefresh}
                    disabled={refreshing}
                    title="Re-check this connection"
                    className="ml-auto rounded p-1 text-muted-foreground hover:bg-accent disabled:opacity-50"
                    data-testid={`${entryTid}-refresh`}
                >
                    <RefreshCw className={`h-3.5 w-3.5 ${refreshing ? "animate-spin" : ""}`} />
                </button>
            </header>
            {entry.scopeResourceId && (
                <p
                    className="border-b px-4 py-1.5 font-mono text-[11px] text-muted-foreground break-all"
                    data-testid={`${entryTid}-scope`}
                >
                    {entry.scopeResourceId}
                </p>
            )}
            <ul>
                {entry.capabilities.map((cap) => (
                    <CapabilityRow
                        key={cap.capability}
                        row={cap}
                        entryTid={entryTid}
                        onRequest={onRequest}
                        requesting={requesting}
                    />
                ))}
            </ul>
        </section>
    );
}

interface CapabilityRowProps {
    row: AccessProbeResult;
    entryTid: string;
    onRequest: (capability: string) => void;
    requesting: boolean;
}

function CapabilityRow({ row, entryTid, onRequest, requesting }: CapabilityRowProps) {
    const rowTid = `${entryTid}-cap-${tid(row.capability)}`;
    const connectionString = row.authMode === "connectionString";

    return (
        <li
            className="flex items-start gap-3 px-4 py-2 [&:not(:last-child)]:border-b"
            data-testid={rowTid}
        >
            <span
                className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${STATUS_DOT[row.status]}`}
                data-testid={`${rowTid}-status-dot`}
                title={accessStatusLabel(row.status)}
            />
            <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                    <span className="text-sm">{capabilityDisplayName(row.capability)}</span>
                    <span
                        className={`text-xs font-medium ${STATUS_TEXT[row.status]}`}
                        data-testid={`${rowTid}-status`}
                    >
                        {accessStatusLabel(row.status)}
                    </span>
                    {connectionString && (
                        <span
                            className="rounded bg-muted px-1.5 py-0.5 text-[11px] text-muted-foreground"
                            data-testid={`${rowTid}-auth-mode`}
                            title="This connection uses a connection string / key — access is granted on the credential, not your Entra identity."
                        >
                            connection string
                        </span>
                    )}
                </div>
                {row.status === "Denied" && row.denial && (
                    <p className="mt-0.5 text-xs text-muted-foreground" data-testid={`${rowTid}-remedy`}>
                        Needs <span className="font-medium">{row.denial.requiredAccess}</span>
                        {" — "}
                        {row.denial.guidance}
                    </p>
                )}
                {row.status === "Unknown" && row.errorSummary && (
                    <p
                        className="mt-0.5 text-xs italic text-muted-foreground"
                        data-testid={`${rowTid}-unknown-detail`}
                    >
                        {row.errorSummary}
                    </p>
                )}
            </div>
            {row.status === "Denied" && !connectionString && (
                <button
                    onClick={() => onRequest(row.capability)}
                    disabled={requesting}
                    className="shrink-0 rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                    data-testid={`${rowTid}-request`}
                >
                    Request access
                </button>
            )}
        </li>
    );
}
