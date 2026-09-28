import { useState } from "react";
import { RefreshCw, Trash2 } from "lucide-react";
import {
    useAccessEntryRefresh,
    useAccessReport,
    useAccessReportRefresh,
    useAccessRequestArtifact,
    useAccessWebhook,
    useSaveAccessWebhook,
    useSendAccessRequest,
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
    AccessRequestArtifactInput,
    AccessRequestSendResult,
    AccessStatus,
} from "@/lib/types";
import { AccessRequestDialog } from "./AccessRequestDialog";
import { DraftInput } from "./DraftInput";

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
    const [artifact, setArtifact] = useState<{
        artifact: AccessRequestArtifact;
        request: AccessRequestArtifactInput;
    } | null>(null);

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
                                        {
                                            onSuccess: (a) =>
                                                setArtifact({
                                                    artifact: a,
                                                    request: {
                                                        featureArea: entry.featureArea,
                                                        connectionKey: entry.connectionKey,
                                                        capability,
                                                    },
                                                }),
                                        },
                                    )
                                }
                                requesting={requestArtifact.isPending}
                            />
                        ))}
                    </div>
                </>
            )}

            <WebhookConfigSection />

            {artifact && (
                <AccessRequestDialog
                    artifact={artifact.artifact}
                    request={artifact.request}
                    onClose={() => setArtifact(null)}
                />
            )}
        </div>
    );
}

/**
 * The Phase-4 request webhook card: where "Send request" on a denied row POSTs —
 * typically a Teams Power App / Power Automate HTTP trigger. The URL embeds a SAS
 * signature so it is written to the OS credential store, never profiles.json; the card
 * only ever shows whether one is stored, not the URL itself.
 */
function WebhookConfigSection() {
    const webhook = useAccessWebhook();
    const save = useSaveAccessWebhook();
    const testSend = useSendAccessRequest();
    const [urlDraft, setUrlDraft] = useState("");
    const [templateDraft, setTemplateDraft] = useState<string | null>(null);
    const [testResult, setTestResult] = useState<AccessRequestSendResult | null>(null);

    const config = webhook.data;
    // templateDraft !== null means "don't clobber my typing with the saved value";
    // it goes back to null on "Reset to default" so the editor follows the default again.
    const saveFields = (patch: { url?: string; clearUrl?: boolean; bodyTemplate?: string | null }) =>
        config &&
        save.mutate(
            {
                enabled: config.enabled,
                bodyTemplate: "bodyTemplate" in patch ? patch.bodyTemplate : config.bodyTemplate,
                url: patch.url,
                clearUrl: patch.clearUrl,
            },
            // A saved config can change what a test render produces — drop the stale one.
            { onSuccess: () => setTestResult(null) },
        );

    const toggleEnabled = (enabled: boolean) =>
        config &&
        save.mutate({
            enabled,
            bodyTemplate: templateDraft ?? config.bodyTemplate,
        });

    const runTest = () =>
        testSend.mutate(
            {
                featureArea: "test",
                connectionKey: "test",
                capability: "test",
                dryRun: true,
            },
            { onSuccess: setTestResult },
        );

    return (
        <section className="rounded-lg border p-4" data-testid="access-webhook-section">
            <div className="flex items-center justify-between gap-2">
                <h3 className="text-sm font-semibold">Request webhook</h3>
                <label className="flex items-center gap-2 text-sm" data-testid="access-webhook-enabled-label">
                    <input
                        type="checkbox"
                        checked={config?.enabled ?? false}
                        onChange={(e) => toggleEnabled(e.target.checked)}
                        disabled={!config || save.isPending}
                        data-testid="access-webhook-enabled"
                    />
                    Enabled
                </label>
            </div>

            <p className="mt-1 text-xs text-muted-foreground">
                Optional — sends access requests to an HTTP trigger (e.g. a Power Automate
                flow feeding a Teams Power App). The trigger URL carries its SAS signature,
                so it is stored in the OS credential store, never in profiles.json.
            </p>

            {webhook.isLoading && (
                <p className="mt-3 text-xs text-muted-foreground">Loading webhook settings…</p>
            )}

            {config && (
                <div className="mt-3 space-y-3">
                    <div>
                        <p
                            className="text-xs text-muted-foreground"
                            data-testid="access-webhook-url-status"
                        >
                            {config.hasUrl
                                ? `A trigger URL is stored (credential key ${config.urlCredentialKey ?? "?"}). Paste a new one to replace it.`
                                : "No trigger URL stored — requests can only be copied, not sent."}
                        </p>
                        <div className="mt-1.5 flex gap-2">
                            <DraftInput
                                value={urlDraft}
                                onCommit={setUrlDraft}
                                onDraftChange={setUrlDraft}
                                placeholder="https://… Power Automate trigger URL"
                                className="min-w-0 flex-1 rounded-md border bg-background px-2 py-1.5 font-mono text-xs"
                                data-testid="access-webhook-url"
                            />
                            <button
                                onClick={() => {
                                    saveFields({ url: urlDraft.trim() });
                                    setUrlDraft("");
                                }}
                                disabled={!urlDraft.trim() || save.isPending}
                                className="shrink-0 rounded-md border px-2.5 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
                                data-testid="access-webhook-url-save"
                            >
                                Save URL
                            </button>
                            {config.hasUrl && (
                                <button
                                    onClick={() => saveFields({ clearUrl: true })}
                                    disabled={save.isPending}
                                    title="Remove the stored URL"
                                    className="shrink-0 rounded-md border px-2 py-1.5 text-xs text-destructive hover:bg-accent disabled:opacity-50"
                                    data-testid="access-webhook-url-remove"
                                >
                                    <Trash2 className="h-3.5 w-3.5" />
                                </button>
                            )}
                        </div>
                    </div>

                    <div>
                        <div className="flex items-center justify-between">
                            <label
                                htmlFor="access-webhook-template"
                                className="text-xs font-medium text-muted-foreground"
                            >
                                Body template (JSON)
                            </label>
                            <button
                                onClick={() => {
                                    setTemplateDraft(null);
                                    saveFields({ bodyTemplate: null });
                                }}
                                disabled={save.isPending || config.bodyTemplate === null}
                                className="text-xs text-muted-foreground underline hover:text-foreground disabled:opacity-50"
                                data-testid="access-webhook-template-reset"
                            >
                                Reset to default
                            </button>
                        </div>
                        <textarea
                            id="access-webhook-template"
                            value={templateDraft ?? config.effectiveBodyTemplate}
                            onChange={(e) => setTemplateDraft(e.target.value)}
                            onBlur={() => {
                                const next = templateDraft?.trim();
                                if (next !== undefined && next !== (config.bodyTemplate ?? config.effectiveBodyTemplate)) {
                                    saveFields({ bodyTemplate: next || null });
                                }
                            }}
                            rows={10}
                            spellCheck={false}
                            className="mt-1 w-full rounded-md border bg-background px-2 py-1.5 font-mono text-xs"
                            data-testid="access-webhook-template"
                        />
                        <p className="mt-1 text-xs text-muted-foreground" data-testid="access-webhook-placeholders">
                            Placeholders: {"{role} {scope} {principal} {upn} {objectId} {resource} {featureArea} {capability} {justification} {summary} {timestamp}"}.
                            Inside quotes they fill escaped string content; unquoted they emit
                            a JSON value (<code>null</code> when unknown). Unknown tokens are
                            left as written.
                        </p>
                    </div>

                    <div className="flex items-center gap-2">
                        <button
                            onClick={runTest}
                            disabled={testSend.isPending}
                            className="rounded-md border px-2.5 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
                            data-testid="access-webhook-test"
                        >
                            {testSend.isPending ? "Rendering…" : "Preview a test request (dry run)"}
                        </button>
                        <span className="text-xs text-muted-foreground">
                            Renders the saved template against sample values — nothing is sent.
                        </span>
                    </div>

                    {testResult && (
                        <div data-testid="access-webhook-test-result">
                            {testResult.renderedBody && (
                                <pre
                                    className="overflow-x-auto rounded-md border bg-muted/40 p-2 text-xs"
                                    data-testid="access-webhook-test-body"
                                >
                                    {testResult.renderedBody}
                                </pre>
                            )}
                            {testResult.error && (
                                <p className="mt-1 text-xs text-destructive" data-testid="access-webhook-test-error">
                                    {testResult.error}
                                </p>
                            )}
                        </div>
                    )}
                </div>
            )}
        </section>
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
