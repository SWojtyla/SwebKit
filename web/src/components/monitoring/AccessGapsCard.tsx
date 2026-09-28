import { Copy, ShieldAlert } from "lucide-react";
import type { AccessGap } from "../../lib/api";
import { useNotification } from "../layout/notification-context";

/**
 * Access gaps an investigation hit (agent-colleague item 2): tool calls the agent
 * couldn't complete because the signed-in identity lacks rights. Each gap names the
 * capability, the least-privilege access to request, and — when the denied call's
 * args identified one — the resource. "Copy access request" carries only the
 * access text and resource so it can be pasted straight into a ticket; no scopes
 * or principals are invented beyond what the denial reported.
 */
export function AccessGapsCard({ gaps }: { gaps: AccessGap[] }) {
    const { notify } = useNotification();
    if (gaps.length === 0) return null;

    const copyRequest = (gap: AccessGap) => {
        const lines = [`Access needed: ${gap.requiredAccess}`];
        if (gap.resource) lines.push(`Resource: ${gap.resource}`);
        navigator.clipboard
            .writeText(lines.join("\n"))
            .then(() =>
                notify(
                    "success",
                    "Copied",
                    "Access request copied to clipboard.",
                ),
            )
            .catch(() =>
                notify(
                    "error",
                    "Copy failed",
                    "Couldn't write to the clipboard.",
                ),
            );
    };

    return (
        <section data-testid="ai-report-access-gaps">
            <h4 className="flex items-center gap-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                <ShieldAlert className="h-3 w-3" /> Access gaps
            </h4>
            <p className="mt-1 text-xs text-muted-foreground">
                {gaps.length === 1
                    ? "One check was blocked by missing permissions — evidence may be partial."
                    : `${gaps.length} checks were blocked by missing permissions — evidence may be partial.`}
            </p>
            <div className="mt-2 space-y-2">
                {gaps.map((gap, i) => (
                    <div
                        key={`${gap.capability}-${gap.resource ?? gap.featureArea}-${i}`}
                        className="rounded-md border border-warning/40 bg-warning/10 px-3 py-2"
                        data-testid={`access-gap-${i}`}
                    >
                        <div className="flex items-start justify-between gap-2">
                            <div className="min-w-0">
                                <div className="text-sm font-medium">
                                    {gap.requiredAccess}
                                </div>
                                <div className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-[11px] text-muted-foreground">
                                    {gap.featureArea && (
                                        <span>{gap.featureArea}</span>
                                    )}
                                    {gap.tool && (
                                        <span className="font-mono">
                                            {gap.tool}
                                        </span>
                                    )}
                                    {gap.resource && (
                                        <span
                                            className="font-mono"
                                            data-testid={`access-gap-resource-${i}`}
                                        >
                                            {gap.resource}
                                        </span>
                                    )}
                                </div>
                                {gap.guidance && (
                                    <p className="mt-1 text-xs text-muted-foreground">
                                        {gap.guidance}
                                    </p>
                                )}
                                {gap.detail && (
                                    <p className="mt-0.5 line-clamp-2 text-[11px] text-muted-foreground/70">
                                        {gap.detail}
                                    </p>
                                )}
                            </div>
                            <button
                                onClick={() => copyRequest(gap)}
                                className="flex shrink-0 items-center gap-1 rounded-md border px-2 py-1 text-[11px] text-muted-foreground hover:bg-accent hover:text-foreground"
                                title="Copy the access needed and resource — paste into your access request"
                                data-testid={`access-gap-copy-${i}`}
                            >
                                <Copy className="h-3 w-3" /> Copy access request
                            </button>
                        </div>
                    </div>
                ))}
            </div>
        </section>
    );
}
