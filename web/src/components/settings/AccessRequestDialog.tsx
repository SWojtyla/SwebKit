import { useState } from "react";
import { Check, Copy } from "lucide-react";
import { useNotification } from "@/components/layout/notification-context";
import { Dialog } from "@/components/shared/Dialog";
import {
    buildAccessRequestText,
    principalDisplay,
} from "@/lib/access-format";
import type { AccessRequestArtifact } from "@/lib/types";

interface AccessRequestDialogProps {
    artifact: AccessRequestArtifact;
    onClose: () => void;
}

/**
 * The copyable access-request artifact for a denied capability row (access-awareness
 * Phase 3a). Shows exactly what will be sent — summary, role, scope, principal and the
 * az/grant fallback — before the user copies it. Send requires the Phase 4 webhook;
 * `webhookConfigured` keeps the button hidden until then.
 */
export function AccessRequestDialog({ artifact, onClose }: AccessRequestDialogProps) {
    const { notify } = useNotification();
    const [copied, setCopied] = useState(false);

    const copy = async () => {
        try {
            await navigator.clipboard.writeText(buildAccessRequestText(artifact));
            setCopied(true);
            notify("success", "Request copied", "Paste it into a ticket or message to the resource owner.");
        } catch {
            notify("error", "Couldn't copy", "Clipboard access failed — select and copy the text manually.");
        }
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
                        // Phase 4 wires the webhook sender — hidden until then.
                        <button
                            className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent"
                            data-testid="access-request-send"
                        >
                            Send request
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
