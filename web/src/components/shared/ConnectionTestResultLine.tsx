import type { ConnectionTestResult } from "@/lib/types";

/**
 * Settings-page "Test connection" result. The summary span keeps the historical
 * `Failed: <error>` / `Connected` text contract (e2e asserts on it); the
 * classified `kind`/`detail`/`hint` the sidecar now sends renders as an
 * expandable "why?" so a failure shows its cause — auth vs timeout vs
 * unreachable — instead of the bare summary.
 */
export function ConnectionTestResultLine({
    result,
    testId,
}: {
    result: ConnectionTestResult;
    testId?: string;
}) {
    return (
        <span className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
            <span
                className={
                    result.connected ? "text-success" : "text-destructive"
                }
                data-testid={testId}
                title={result.detail}
            >
                {result.connected
                    ? "Connected"
                    : `Failed: ${result.error ?? "unknown error"}`}
            </span>
            {!result.connected && (result.detail || result.hint) && (
                <details
                    className="w-full text-xs text-muted-foreground"
                    data-testid={testId ? `${testId}-details` : undefined}
                >
                    <summary className="inline cursor-pointer hover:text-foreground">
                        {result.kind ? `${result.kind} — details` : "details"}
                    </summary>
                    {result.detail && (
                        <pre className="mt-1 whitespace-pre-wrap break-all font-mono">
                            {result.detail}
                        </pre>
                    )}
                    {result.hint && <p className="mt-1">Hint: {result.hint}</p>}
                </details>
            )}
        </span>
    );
}
