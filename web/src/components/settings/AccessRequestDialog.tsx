import { useState } from "react";
import { Check, Copy, Send } from "lucide-react";
import { useNotification } from "@/components/layout/notification-context";
import { Dialog } from "@/components/shared/Dialog";
import { useSendAccessRequest } from "@/lib/hooks";
import {
    buildAccessRequestText,
    principalDisplay,
} from "@/lib/access-format";
import type {
    AccessRequestArtifact,
    AccessRequestArtifactInput,
    AccessRequestSendResult,
} from "@/lib/types";

interface AccessRequestDialogProps {
    artifact: AccessRequestArtifact;
    /** The denied-row triple the artifact was built from — the send endpoint rebuilds
     * the artifact from it, so the webhook always gets the current state. */
    request: AccessRequestArtifactInput;
    onClose: () => void;
}

/**
 * The access-request artifact for a denied capability row (access-awareness
 * Phase 3a/4). Shows exactly what will be sent — summary, role, scope, principal and the
 * az/grant fallback — before the user copies or sends it. "Send request" only appears
 * when a webhook is configured and enabled (`webhookConfigured`); copy stays the
 * primary fallback either way.
 */
export function AccessRequestDialog({ artifact, request, onClose }: AccessRequestDialogProps) {
    const { notify } = useNotification();
    const send = useSendAccessRequest();
    const [copied, setCopied] = useState(false);
    const [justification, setJustification] = useState("");
    const [sendResult, setSendResult] = useState<AccessRequestSendResult | null>(null);

    const copy = async () => {
        try {
            await navigator.clipboard.writeText(buildAccessRequestText(artifact));
            setCopied(true);
            notify("success", "Request copied", "Paste it into a ticket or message to the resource owner.");
        } catch {
            notify("error", "Couldn't copy", "Clipboard access failed — select and copy the text manually.");
        }
    };

    const sendRequest = () => {
        send.mutate(
            {
                ...request,
                justification: justification.trim() || undefined,
            },
            {
                onSuccess: (result) => {
                    setSendResult(result);
                    if (result.sent) {
                        notify(
                            "success",
                            "Request sent",
                            "The webhook accepted it — approval still happens in your organization's process.",
                        );
                    }
                    // Failures render inline (sendResult) so the full error + rendered
                    // body stay visible next to the request — no toast needed too.
                },
            },
        );
    };

    return (
        <Dialog onClose={onClose} label="Request access" testId="access-request-dialog" widthClassName="w-[560px]">
            <div className="space-y-4 p-5">
                <h3 className="text-base font-semibold">Request access</h3>

                <p className="text-sm" data-testid="access-request-summary">
                    {artifact.summaryText}
                </p>

                <dl className="space-y-1.5 text-sm">
                    <div className="flex gap-2">
                        <dt className="w-24 shrink-0 text-muted-foreground">Access needed</dt>
                        <dd data-testid="access-request-role">{artifact.role}</dd>
                    </div>
                    <div className="flex gap-2">
                        <dt className="w-24 shrink-0 text-muted-foreground">Resource</dt>
                        <dd>{artifact.resource}</dd>
                    </div>
                    <div className="flex gap-2">
                        <dt className="w-24 shrink-0 text-muted-foreground">Scope</dt>
                        <dd
                            className={artifact.scope ? "break-all font-mono text-xs" : "text-muted-foreground"}
                            data-testid="access-request-scope"
                        >
                            {artifact.scope ??
                                "Unknown — ask your admin for the resource's Azure resource ID."}
                        </dd>
                    </div>
                    <div className="flex gap-2">
                        <dt className="w-24 shrink-0 text-muted-foreground">Principal</dt>
                        <dd data-testid="access-request-principal">
                            {principalDisplay(artifact.principal)}
                        </dd>
                    </div>
                </dl>

                {artifact.grantStatement && (
                    <div>
                        <p className="mb-1 text-xs font-medium text-muted-foreground">
                            Grant statement (a DBA can run this)
                        </p>
                        <pre
                            className="overflow-x-auto rounded-md border bg-muted/40 p-2 text-xs"
                            data-testid="access-request-grant"
                        >
                            {artifact.grantStatement}
                        </pre>
                    </div>
                )}

                {artifact.azCommand && (
                    <div>
                        <p className="mb-1 text-xs font-medium text-muted-foreground">
                            Equivalent az CLI command (for whoever approves)
                        </p>
                        <pre
                            className="overflow-x-auto whitespace-pre-wrap break-all rounded-md border bg-muted/40 p-2 font-mono text-xs"
                            data-testid="access-request-az"
                        >
                            {artifact.azCommand}
                        </pre>
                    </div>
                )}

                {!artifact.azCommand && !artifact.grantStatement && (
                    <p className="text-xs text-muted-foreground" data-testid="access-request-no-command">
                        No ready-made command applies here — the summary above is the request
                        to send to the resource owner.
                    </p>
                )}

                {artifact.webhookConfigured && (
                    <div>
                        <label
                            htmlFor="access-request-justification"
                            className="mb-1 block text-xs font-medium text-muted-foreground"
                        >
                            Note for the approver (optional)
                        </label>
                        <textarea
                            id="access-request-justification"
                            value={justification}
                            onChange={(e) => setJustification(e.target.value)}
                            rows={2}
                            placeholder="Why you need this access — sent with the request."
                            className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
                            data-testid="access-request-justification"
                        />
                    </div>
                )}

                {sendResult && (
                    <div
                        className={`rounded-md border p-2.5 text-xs ${
                            sendResult.sent
                                ? "border-success/40 text-success"
                                : "border-destructive/40 text-destructive"
                        }`}
                        data-testid="access-request-send-result"
                    >
                        {sendResult.sent && (
                            <p data-testid="access-request-send-ok">
                                Sent — the webhook answered HTTP {sendResult.statusCode}.
                                The request is submitted; approval still happens in your
                                organization's process.
                            </p>
                        )}
                        {sendResult.demo && (
                            <p className="text-muted-foreground" data-testid="access-request-send-demo">
                                Demo mode — nothing was actually sent. The simulated HTTP{" "}
                                {sendResult.statusCode} shows what a rejected request looks like.
                            </p>
                        )}
                        {!sendResult.sent && !sendResult.demo && (
                            <p data-testid="access-request-send-error">
                                Send failed{sendResult.statusCode !== null && ` (HTTP ${sendResult.statusCode})`}
                                {sendResult.error ? ` — ${sendResult.error}` : ""} Copy the
                                request above and send it to the resource owner instead.
                            </p>
                        )}
                    </div>
                )}

                <div className="flex items-center justify-end gap-2 pt-1">
                    <button
                        onClick={copy}
                        className="flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
                        data-testid="access-request-copy"
                    >
                        {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
                        {copied ? "Copied" : "Copy request"}
                    </button>
                    {artifact.webhookConfigured && (
                        <button
                            onClick={sendRequest}
                            disabled={send.isPending || sendResult?.sent === true}
                            className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
                            data-testid="access-request-send"
                        >
                            <Send className="h-3.5 w-3.5" />
                            {send.isPending
                                ? "Sending…"
                                : sendResult?.sent
                                  ? "Sent"
                                  : "Send request"}
                        </button>
                    )}
                    <button
                        onClick={onClose}
                        className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent"
                        data-testid="access-request-close"
                    >
                        Close
                    </button>
                </div>
            </div>
        </Dialog>
    );
}
