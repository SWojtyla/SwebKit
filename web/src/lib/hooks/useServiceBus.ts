import {
    useQuery,
    useMutation,
    useQueryClient,
    type QueryClient,
} from "@tanstack/react-query";
import { apiFetch, apiSend } from "../api";
import { useNotification } from "@/components/layout/NotificationSystem";
import type {
    SbEntityInfo,
    SbEntityStats,
    SbMessage,
    SbNamespaceInfo,
    SbMessageTemplate,
    ScheduledMessageEntry,
} from "../types";

// ── Service Bus ──────────────────────────────────────────────────────────────

/**
 * How long entity *topology* stays fresh. Queues, topics and subscriptions are created by
 * deployments, not by using the app, so refetching them on a 30s cadence only replays the
 * namespace fan-out. Both the explicit Refresh action and every mutation path invalidate them
 * directly when they actually change.
 */
const TOPOLOGY_STALE_TIME = 5 * 60_000;

/** Message counts are the volatile half — those are worth re-reading often. */
const STATS_STALE_TIME = 10_000;

/**
 * A subscription's entity path is `topic/subscriptions/name`, and the sidecar route is a
 * single-segment `{entityPath}` that calls `Uri.UnescapeDataString`. Leaving the slashes raw
 * produces a URL that cannot match the route at all — every subscription peek, purge, complete and
 * resubmit 404'd, then got retried once by the global `retry: 1`.
 */
function entitySegment(entityPath: string): string {
    return encodeURIComponent(entityPath);
}

export function useSbTestConnection(
    nsId: string | null,
    options?: { enabled?: boolean },
) {
    return useQuery({
        queryKey: ["sb-test", nsId],
        queryFn: ({ signal }) =>
            apiFetch<{ connected: boolean; error?: string }>(
                `/api/servicebus/${nsId}/test`,
                { signal },
            ),
        enabled: !!nsId && (options?.enabled ?? true),
        // Runs from the global status bar and the dashboard on every mount, not just on this page.
        staleTime: TOPOLOGY_STALE_TIME,
    });
}

export function useSbNamespaceInfo(nsId: string | null) {
    return useQuery({
        queryKey: ["sb-info", nsId],
        queryFn: ({ signal }) =>
            apiFetch<SbNamespaceInfo>(`/api/servicebus/${nsId}/info`, {
                signal,
            }),
        enabled: !!nsId,
        staleTime: TOPOLOGY_STALE_TIME,
    });
}

export function useSbQueues(nsId: string | null) {
    return useQuery({
        queryKey: ["sb-queues", nsId],
        queryFn: ({ signal }) =>
            apiFetch<SbEntityInfo[]>(`/api/servicebus/${nsId}/queues`, {
                signal,
            }),
        enabled: !!nsId,
        staleTime: TOPOLOGY_STALE_TIME,
    });
}

export function useSbTopics(nsId: string | null) {
    return useQuery({
        queryKey: ["sb-topics", nsId],
        queryFn: ({ signal }) =>
            apiFetch<SbEntityInfo[]>(`/api/servicebus/${nsId}/topics`, {
                signal,
            }),
        enabled: !!nsId,
        staleTime: TOPOLOGY_STALE_TIME,
    });
}

export function useSbSubscriptions(
    nsId: string | null,
    topic: string | null,
    options?: { enabled?: boolean },
) {
    return useQuery({
        queryKey: ["sb-subs", nsId, topic],
        queryFn: ({ signal }) =>
            apiFetch<SbEntityInfo[]>(
                `/api/servicebus/${nsId}/topics/${encodeURIComponent(topic!)}/subscriptions`,
                { signal },
            ),
        enabled: !!nsId && !!topic && (options?.enabled ?? true),
        staleTime: TOPOLOGY_STALE_TIME,
    });
}

export function useSbPeekMessages(
    nsId: string | null,
    entityPath: string | null,
    count = 50,
) {
    return useQuery({
        queryKey: ["sb-peek", nsId, entityPath, count],
        queryFn: ({ signal }) =>
            apiFetch<SbMessage[]>(
                `/api/servicebus/${nsId}/entities/${entitySegment(entityPath!)}/peek?count=${count}`,
                { signal },
            ),
        enabled: !!nsId && !!entityPath,
        // A failing peek (dead namespace, throttled request) can take the SDK's full TryTimeout per
        // attempt — the default 3 retries kept "Loading messages..." up for minutes before the error
        // card ever appeared. One retry still rides out a transient blip without the long stall.
        retry: 1,
    });
}

export function useSbPeekDlq(
    nsId: string | null,
    entityPath: string | null,
    count = 50,
) {
    return useQuery({
        queryKey: ["sb-dlq", nsId, entityPath, count],
        queryFn: ({ signal }) =>
            apiFetch<SbMessage[]>(
                `/api/servicebus/${nsId}/entities/${entitySegment(entityPath!)}/dlq?count=${count}`,
                { signal },
            ),
        enabled: !!nsId && !!entityPath,
        retry: 1, // same reasoning as useSbPeekMessages
    });
}

/**
 * Finds an entity's already-loaded counts in the cached queue/topic/subscription lists.
 *
 * The tree fetches counts for every entity it lists, so selecting one and then fetching its `/stats`
 * means the number is on screen twice over — once from the list, once from a dedicated request that
 * (before pooling) also built its own client. This lets the dedicated query start from what is already
 * known and refresh in the background instead of showing a blank cell first.
 *
 * Exported for testing: it reaches into cache-key layout, which is exactly the kind of thing that
 * silently stops matching anything when a key shape changes.
 */
export function findCachedEntityStats(
    qc: QueryClient,
    nsId: string,
    entityPath: string,
): SbEntityStats | undefined {
    const lists = qc
        .getQueriesData<SbEntityInfo[]>({ queryKey: ["sb-queues", nsId] })
        .concat(
            qc.getQueriesData<SbEntityInfo[]>({
                queryKey: ["sb-topics", nsId],
            }),
        )
        .concat(
            qc.getQueriesData<SbEntityInfo[]>({ queryKey: ["sb-subs", nsId] }),
        );

    for (const [, entities] of lists) {
        const match = entities?.find(
            (entity) => entity.entityPath === entityPath,
        );
        if (match?.stats) return match.stats;
    }

    return undefined;
}

export function useSbEntityStats(
    nsId: string | null,
    entityPath: string | null,
) {
    const qc = useQueryClient();
    // A cache read, not a fetch — cheap enough to do per render, and it has to be read here rather than
    // in a `placeholderData` callback so the result types as `SbEntityStats` and not as the callback.
    const cached =
        nsId && entityPath
            ? findCachedEntityStats(qc, nsId, entityPath)
            : undefined;

    return useQuery<SbEntityStats>({
        queryKey: ["sb-entity-stats", nsId, entityPath],
        queryFn: ({ signal }) =>
            apiFetch<SbEntityStats>(
                `/api/servicebus/${nsId}/entities/${entitySegment(entityPath!)}/stats`,
                { signal },
            ),
        enabled: !!nsId && !!entityPath,
        staleTime: STATS_STALE_TIME,
        placeholderData: cached,
    });
}

/**
 * Invalidates every query for one entity (peek/DLQ/stats/scheduled) plus the namespace's
 * queue/topic/subscription lists. Exported so call sites outside this file — the Refresh
 * command in `ServiceBusPage.tsx` in particular — can reuse the real key set instead of a
 * hand-rolled `invalidateQueries({ queryKey: ["sb-"] })`, which matches nothing: TanStack Query
 * compares key *elements*, not string prefixes, and every real key here is `["sb-peek", nsId,
 * entityPath, count]` and friends (same class of bug as `aks-query-keys.ts`'s note on `"aks-"`).
 */
export function invalidateServiceBusQueries(
    qc: QueryClient,
    nsId: string,
    entityPath: string,
    options?: { includeTopology?: boolean },
) {
    qc.invalidateQueries({ queryKey: ["sb-peek", nsId, entityPath] });
    qc.invalidateQueries({ queryKey: ["sb-dlq", nsId, entityPath] });
    qc.invalidateQueries({ queryKey: ["sb-entity-stats", nsId, entityPath] });
    qc.invalidateQueries({ queryKey: ["sb-scheduled", nsId, entityPath] });

    // Message mutations change the counts the entity-tree badges render from the
    // queue/topic lists — one request each, cheap enough to refresh on every
    // mutation. Skipping them left the tree counts stale next to the refreshed
    // tab header. `sb-subs` is keyed per topic, so only the affected
    // subscription's topic is invalidated — a blanket `sb-subs` invalidate would
    // re-fire one request per topic in the tree after a single message action.
    qc.invalidateQueries({ queryKey: ["sb-queues", nsId] });
    qc.invalidateQueries({ queryKey: ["sb-topics", nsId] });
    const subMarker = entityPath.indexOf("/subscriptions/");
    if (subMarker > 0) {
        qc.invalidateQueries({
            queryKey: ["sb-subs", nsId, entityPath.slice(0, subMarker)],
        });
    }

    if (options?.includeTopology) {
        qc.invalidateQueries({ queryKey: ["sb-subs", nsId] });
    }
}

export function useSbSendMessage() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            message: SbMessage;
        }) =>
            apiSend(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/send`,
                "POST",
                vars.message,
            ),
        onSuccess: (_data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't send message", String(error)),
    });
}

export function useSbScheduleMessage() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            message: SbMessage;
            scheduledEnqueueTime: string;
        }) =>
            apiSend<{ sequenceNumber: number }>(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/schedule`,
                "POST",
                {
                    message: vars.message,
                    scheduledEnqueueTime: vars.scheduledEnqueueTime,
                },
            ),
        onSuccess: (_data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't schedule message", String(error)),
    });
}

export function useSbBatchSend() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            messages: SbMessage[];
        }) =>
            apiSend<{ sent: number }>(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/batch-send`,
                "POST",
                vars.messages,
            ),
        onSuccess: (_data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't send batch", String(error)),
    });
}

/**
 * Resend-to-origin: the sidecar forwards each selected message to the queue it failed
 * in — resolved server-side from the `NServiceBus.FailedQ` application property,
 * falling back to the viewed entity — with a fresh MessageId, then removes the
 * original. Move semantics, so a resend never leaves a duplicate behind. `deadLetter`
 * reads from the entity's DLQ instead of its active list, mirroring which list the
 * selection came from.
 */
export function useSbResendMessages() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            sequenceNumbers: string[];
            deadLetter: boolean;
        }) =>
            apiSend<{ resent: number }>(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/resend`,
                "POST",
                {
                    sequenceNumbers: vars.sequenceNumbers,
                    deadLetter: vars.deadLetter,
                },
            ),
        onSuccess: (_data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't resend messages", String(error)),
    });
}

export function useSbScheduledMessages(
    nsId: string | null,
    entityPath: string | null,
) {
    return useQuery({
        queryKey: ["sb-scheduled", nsId, entityPath],
        queryFn: ({ signal }) =>
            apiFetch<ScheduledMessageEntry[]>(
                `/api/servicebus/${nsId}/entities/${entitySegment(entityPath!)}/scheduled`,
                { signal },
            ),
        enabled: !!nsId && !!entityPath,
    });
}

export function useSbCancelScheduled() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            sequenceNumber: number;
        }) =>
            apiSend(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/scheduled/${vars.sequenceNumber}`,
                "DELETE",
            ),
        onSuccess: (_data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't cancel scheduled message", String(error)),
    });
}

export function useSbTemplates() {
    return useQuery({
        queryKey: ["sb-templates"],
        queryFn: ({ signal }) =>
            apiFetch<SbMessageTemplate[]>("/api/servicebus/templates", {
                signal,
            }),
    });
}

export function useSbSaveTemplate() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (template: SbMessageTemplate) =>
            apiSend<SbMessageTemplate>(
                "/api/servicebus/templates",
                "POST",
                template,
            ),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["sb-templates"] });
        },
        onError: (error) =>
            notify("error", "Couldn't save template", String(error)),
    });
}

export function useSbDeleteTemplate() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (id: string) =>
            apiSend(`/api/servicebus/templates/${id}`, "DELETE"),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["sb-templates"] });
        },
        onError: (error) =>
            notify("error", "Couldn't delete template", String(error)),
    });
}

export function useSbCompleteMessages() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            sequenceNumbers: number[];
        }) =>
            apiSend(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/complete`,
                "POST",
                vars.sequenceNumbers,
            ),
        onSuccess: (_data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't complete messages", String(error)),
    });
}

/**
 * Move-to-dead-letter: the sidecar dead-letters each selected active message on the
 * broker — a real broker dead-letter (PeekLock `DeadLetterMessageAsync`), not a
 * send-and-delete, so the message lands in the entity's DLQ with a recorded reason.
 */
export function useSbDeadLetterMessages() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            sequenceNumbers: number[];
        }) =>
            apiSend<{ deadLettered: number }>(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/deadletter`,
                "POST",
                vars.sequenceNumbers,
            ),
        onSuccess: (_data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't dead-letter messages", String(error)),
    });
}

export function useSbPurgeMessages() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            deadLetter: boolean;
        }) =>
            apiSend(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/purge`,
                "POST",
                { deadLetter: vars.deadLetter },
            ),
        onSuccess: (_data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't purge messages", String(error)),
    });
}

export function useSbCompleteDlq() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            sequenceNumbers: string[];
        }) =>
            apiSend(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/dlq/complete`,
                "POST",
                vars.sequenceNumbers,
            ),
        onSuccess: (_data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify(
                "error",
                "Couldn't complete dead-letter messages",
                String(error),
            ),
    });
}

export function useSbResubmitDlq() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            sequenceNumbers: string[];
            targetEntityPath?: string | null;
        }) =>
            apiSend(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/resubmit`,
                "POST",
                {
                    sequenceNumbers: vars.sequenceNumbers,
                    targetEntityPath: vars.targetEntityPath ?? null,
                    remapRules: null,
                },
            ),
        onSuccess: (_data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't resubmit messages", String(error)),
    });
}
