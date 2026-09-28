import { useQuery } from "@tanstack/react-query";
import {
    checkForUpdate,
    getCurrentVersion,
    readCachedResult,
    type UpdateCheckResult,
} from "../update-check";

/**
 * Notify-only update check (see `lib/update-check.ts` — no installer, ever). The
 * last fetched result is rendered instantly from localStorage while the real
 * check re-runs, and a failed check degrades to that cache rather than erroring —
 * an offline machine must never show a broken Updates section.
 */
export function useUpdateCheck() {
    return useQuery<UpdateCheckResult>({
        queryKey: ["update-check"],
        queryFn: async ({ signal }) => checkForUpdate(await getCurrentVersion(), signal),
        staleTime: 1000 * 60 * 60 * 6, // release cadence doesn't need sub-hour freshness
        retry: false,
        refetchOnWindowFocus: false,
        placeholderData: readCachedResult() ?? undefined,
    });
}
