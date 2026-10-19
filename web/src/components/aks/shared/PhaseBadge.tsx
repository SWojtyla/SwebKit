/**
 * Pod phase/status → SwebOS soft state pill (`badge b-sm` from the approved
 * skin mock): 20px rounded-full chip, soft state background + ink text +
 * 8px state dot. The class maps stay literal strings so Tailwind can see
 * every generated utility.
 */
export type StateKey =
    | "blocked"
    | "needs"
    | "working"
    | "waiting"
    | "queued"
    | "done";

export const STATE_SOFT_CLASS: Record<StateKey, string> = {
    blocked: "bg-state-blocked-soft text-state-blocked-ink",
    needs: "bg-state-needs-soft text-state-needs-ink",
    working: "bg-state-working-soft text-state-working-ink",
    waiting: "bg-state-waiting-soft text-state-waiting-ink",
    queued: "bg-state-queued-soft text-state-queued-ink",
    done: "bg-state-done-soft text-state-done-ink",
};

export const STATE_DOT_CLASS: Record<StateKey, string> = {
    blocked: "bg-state-blocked",
    needs: "bg-state-needs",
    working: "bg-state-working",
    waiting: "bg-state-waiting",
    queued: "bg-state-queued",
    done: "bg-state-done",
};

/** Kubernetes pod phase/reason → state bucket. Anything unrecognized falls to
 * `queued` (neutral) rather than guessing at a severity we can't back up. */
export function podPhaseState(status: string): StateKey {
    const s = status.toLowerCase();
    if (s === "running") return "done";
    if (s === "pending") return "waiting";
    if (s === "imagepullbackoff" || s === "errimagepull") return "needs";
    if (s === "terminating" || s === "containercreating") return "working";
    if (s === "completed" || s === "succeeded") return "queued";
    if (s === "unknown") return "queued";
    if (s === "failed" || s === "evicted") return "blocked";
    if (s.includes("backoff") || s.includes("error")) return "blocked";
    return "queued";
}

export function PhaseBadge({
    phase,
    className,
}: {
    phase: string;
    className?: string;
}) {
    const state = podPhaseState(phase);
    return (
        <span
            className={`inline-flex h-5 items-center gap-1.5 rounded-full px-2 text-xs font-semibold ${STATE_SOFT_CLASS[state]} ${className ?? ""}`}
        >
            <span
                className={`h-2 w-2 shrink-0 rounded-full ${STATE_DOT_CLASS[state]}`}
            />
            {phase}
        </span>
    );
}
