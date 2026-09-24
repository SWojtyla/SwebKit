import { useIsFetching, useIsMutating } from "@tanstack/react-query";
import { LoaderCircle } from "lucide-react";
import { useEffect, useState } from "react";

// Several hooks poll in the background (profile every 10s, agent/session queries every 5s,
// monitoring history every 15s, AKS auto-refresh) and each ~0.5s fetch used to flash this
// indicator — a near-constant "Working…" flicker in the top bar. Debouncing means only work
// that actually takes long enough to matter is surfaced; sub-second round trips stay silent.
const SHOW_AFTER_MS = 800;

export function ActivityIndicator() {
    const fetching = useIsFetching();
    const mutating = useIsMutating();
    const busy = fetching + mutating > 0;
    const [visible, setVisible] = useState(false);

    useEffect(() => {
        if (!busy) {
            setVisible(false);
            return;
        }
        const t = setTimeout(() => setVisible(true), SHOW_AFTER_MS);
        return () => clearTimeout(t);
    }, [busy]);

    if (!visible) return null;

    return (
        <div
            className="flex items-center gap-1.5 whitespace-nowrap text-xs text-muted-foreground"
            data-testid="global-activity-indicator"
            role="status"
            aria-live="polite"
        >
            <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
            {mutating > 0 ? "Saving…" : "Working…"}
        </div>
    );
}
