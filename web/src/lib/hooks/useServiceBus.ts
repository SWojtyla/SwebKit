import {
    useQuery,
    useMutation,
    useQueryClient,
    type QueryClient,
} from "@tanstack/react-query";
import { describeApiError, apiFetch, apiSend } from "../api";
import { useNotification } from "@/components/layout/notification-context";
import type {
    ConnectionTestResult,
    SbEntityInfo,
    SbEntityStats,
    SbMessage,
    SbNamespaceInfo,
    SbMessageTemplate,
    SbSessionSummary,
    ScheduledMessageEntry,
    ReachMessageRequest,
    ReachMessagePreview,
    SbOperationStatus,
    DlqRequeueByFilterRequest,
    DlqRequeueByFilterResult,
    ReplayToRequest,
    ReplayToPreview,
    SbEntityProperties,
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
            apiFetch<ConnectionTestResult>(
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
 * Session summaries for the entity's peek window — the "who holds sessions here" answer behind
 * the chip bar. The endpoint groups the peek window server-side (there is no session-listing
 * API in the SDK); callers only enable it on `requiresSession` entities, so the extra request
 * never fires on ordinary queues.
 */
export function useSbSessions(
    nsId: string | null,
    entityPath: string | null,
    options?: { enabled?: boolean },
) {
    return useQuery({
        queryKey: ["sb-sessions", nsId, entityPath],
        queryFn: ({ signal }) =>
            apiFetch<SbSessionSummary[]>(
                `/api/servicebus/${nsId}/entities/${entitySegment(entityPath!)}/sessions?count=250`,
                { signal },
            ),
        enabled: !!nsId && !!entityPath && (options?.enabled ?? true),
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
    qc.invalidateQueries({ queryKey: ["sb-sessions", nsId, entityPath] });

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
            notify("error", "Couldn't send message", describeApiError(error)),
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
            notify("error", "Couldn't schedule message", describeApiError(error)),
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
            notify("error", "Couldn't cancel scheduled message", describeApiError(error)),
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
            notify("error", "Couldn't save template", describeApiError(error)),
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
            notify("error", "Couldn't delete template", describeApiError(error)),
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
            notify("error", "Couldn't complete messages", describeApiError(error)),
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
            notify("error", "Couldn't purge messages", describeApiError(error)),
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
            notify("error", "Couldn't resubmit messages", describeApiError(error)),
    });
}

/**
 * The DLQ "Edit & Resubmit" path: sends the edited message to the target entity and — unlike a
 * plain send — settles the DLQ original by sequence number, so the pre-edit copy can't linger as
 * a duplicate next to the edited one. Same-namespace operation; `targetEntityPath` is a
 * destination override within it.
 */
export function useSbResubmitEditedDlq() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            sequenceNumber: number;
            message: SbMessage;
            targetEntityPath?: string | null;
        }) =>
            apiSend(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/dlq/resubmit-edited`,
                "POST",
                {
                    sequenceNumber: vars.sequenceNumber,
                    message: vars.message,
                    targetEntityPath: vars.targetEntityPath ?? null,
                },
            ),
        onSuccess: (_data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify(
                "error",
                "Couldn't resubmit edited message",
                String(error),
            ),
    });
}

// ── Power ops: reach-message + DLQ triage ────────────────────────────────────
//
// Reach-message is a background sidecar operation, not a request/response — the
// UI previews, confirms, starts, then POLLS the operation record. Parked
// messages carry a DLQ stamp so a sidecar restart can rediscover them; the list
// endpoint enriches interrupted/failed/cancelled ops with a live stamp scan
// (`parkedInDlq`), which is why the banner polls it rather than trusting a
// journal snapshot.

/**
 * Operations on one entity — backs the interrupted-operation banner. Refetches
 * while any op is non-terminal so the banner's live count stays honest.
 */
export function useSbEntityOperations(
    nsId: string | null,
    entityPath: string | null,
) {
    return useQuery({
        queryKey: ["sb-operations", nsId, entityPath],
        queryFn: ({ signal }) =>
            apiFetch<SbOperationStatus[]>(
                `/api/servicebus/${nsId}/operations?entity=${encodeURIComponent(entityPath!)}`,
                { signal },
            ),
        enabled: !!nsId && !!entityPath,
        refetchInterval: (query) => {
            const ops = query.state.data;
            return ops?.some(
                (op) =>
                    op.state === "Running" ||
                    op.state === "Interrupted" ||
                    op.state === "Failed" ||
                    op.state === "Cancelled",
            )
                ? 3_000
                : false;
        },
        retry: 1,
    });
}

/** Polls a single operation — the wizard's progress view. */
export function useSbOperation(
    nsId: string | null,
    operationId: string | null,
) {
    return useQuery({
        queryKey: ["sb-operation", nsId, operationId],
        queryFn: ({ signal }) =>
            apiFetch<SbOperationStatus>(
                `/api/servicebus/${nsId}/operations/${operationId}`,
                { signal },
            ),
        enabled: !!nsId && !!operationId,
        refetchInterval: (query) =>
            query.state.data?.state === "Running" ? 1_000 : false,
        retry: 1,
    });
}

/**
 * The preview call — deliberately a mutation, not a query: it peeks 1,000
 * messages server-side, which is too expensive to refire on focus/stale, and
 * the wizard only wants it on an explicit click. The response is rendered
 * verbatim (consequences + warnings are the product's honesty contract).
 */
export function useSbReachMessagePreview() {
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            request: ReachMessageRequest;
        }) =>
            apiSend<ReachMessagePreview>(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/reach-message/preview`,
                "POST",
                vars.request,
            ),
        onError: (error) =>
            notify("error", "Couldn't preview reach-message", describeApiError(error)),
    });
}

/** Starts the background op after the preview's ConfirmBar — returns the op record to poll. */
export function useSbReachMessageStart() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            request: ReachMessageRequest;
        }) =>
            apiSend<SbOperationStatus>(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/reach-message/start`,
                "POST",
                vars.request,
            ),
        onSuccess: (_data, vars) => {
            qc.invalidateQueries({
                queryKey: ["sb-operations", vars.nsId, vars.entityPath],
            });
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't start reach-message", describeApiError(error)),
    });
}

/** Cancel is observed between receive batches — in-flight locks expire harmlessly. */
export function useSbCancelOperation() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: { nsId: string; operationId: string }) =>
            apiSend<SbOperationStatus>(
                `/api/servicebus/${vars.nsId}/operations/${vars.operationId}/cancel`,
                "POST",
            ),
        onSuccess: (data, vars) => {
            qc.invalidateQueries({
                queryKey: ["sb-operation", vars.nsId, vars.operationId],
            });
            qc.invalidateQueries({
                queryKey: ["sb-operations", vars.nsId, data.entityPath],
            });
            invalidateServiceBusQueries(qc, vars.nsId, data.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't cancel operation", describeApiError(error)),
    });
}

/**
 * Resume = re-run the restore phase against the entity's DLQ stamp set. Valid
 * for interrupted, failed and cancelled ops — the only states with parked
 * messages that may still need it.
 */
export function useSbResumeOperation() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: { nsId: string; operationId: string }) =>
            apiSend<SbOperationStatus>(
                `/api/servicebus/${vars.nsId}/operations/${vars.operationId}/resume`,
                "POST",
            ),
        onSuccess: (data, vars) => {
            qc.invalidateQueries({
                queryKey: ["sb-operation", vars.nsId, vars.operationId],
            });
            qc.invalidateQueries({
                queryKey: ["sb-operations", vars.nsId, data.entityPath],
            });
            invalidateServiceBusQueries(qc, vars.nsId, data.entityPath);
        },
        onError: (error) =>
            notify("error", "Couldn't resume operation", describeApiError(error)),
    });
}

/** "Leave in DLQ" — terminal; the parked copies keep their stamps as a record. */
export function useSbDismissOperation() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: { nsId: string; operationId: string }) =>
            apiSend<SbOperationStatus>(
                `/api/servicebus/${vars.nsId}/operations/${vars.operationId}/dismiss`,
                "POST",
            ),
        onSuccess: (data, vars) => {
            qc.invalidateQueries({
                queryKey: ["sb-operations", vars.nsId, data.entityPath],
            });
        },
        onError: (error) =>
            notify("error", "Couldn't dismiss operation", describeApiError(error)),
    });
}

/**
 * DLQ triage past the peek window: resubmit every DLQ message matching a
 * (reason, description) group server-side, capped by limit. For messages inside
 * the loaded window, the ordinary per-selection resubmit path is still the
 * right tool — this exists for groups bigger than what was peeked.
 */
export function useSbRequeueDlqByFilter() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            request: DlqRequeueByFilterRequest;
        }) =>
            apiSend<DlqRequeueByFilterResult>(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/dlq/requeue-by-filter`,
                "POST",
                vars.request,
            ),
        onSuccess: (data, vars) => {
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
            notify(
                "success",
                `Resubmitted ${data.resubmitted} dead-lettered message(s)`,
            );
        },
        onError: (error) =>
            notify("error", "Couldn't resubmit DLQ group", describeApiError(error)),
    });
}

// ── Cross-environment replay + entity properties ───────────────────────────
//
// Replay-to is the same background-op contract as reach-message — preview,
// confirm, start, then poll `GET .../operations/{id}` — but its target can be
// a different namespace entirely. Every replayed message is a NEW tail-appended
// copy on the target (fresh sequence, reset delivery count) stamped
// SwebKit.ReplayedFrom; nothing is parked in the source DLQ.

/**
 * Read-only management-plane properties for one entity (queue, topic or
 * subscription): max size, TTL, lock duration, delivery caps, partitioning and
 * session flags, as grouped name/value rows. Editing is deliberately not
 * surfaced — this is the "what is this entity configured as" answer.
 */
export function useSbEntityProperties(
    nsId: string | null,
    entityPath: string | null,
    options?: { enabled?: boolean },
) {
    return useQuery({
        queryKey: ["sb-entity-properties", nsId, entityPath],
        queryFn: ({ signal }) =>
            apiFetch<SbEntityProperties>(
                `/api/servicebus/${nsId}/entities/${entitySegment(entityPath!)}/properties`,
                { signal },
            ),
        enabled: !!nsId && !!entityPath && (options?.enabled ?? true),
        // Entity config changes with deployments, not with use — same cadence as topology.
        staleTime: TOPOLOGY_STALE_TIME,
        retry: 1,
    });
}

/**
 * The replay preview call — deliberately a mutation, not a query: it peeks the
 * source window server-side to count matches, and the wizard only wants it on
 * an explicit click. Consequences and warnings are rendered verbatim — they
 * are the honesty contract ("new tail-appended copies", never "like nothing
 * happened").
 */
export function useSbReplayToPreview() {
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            request: ReplayToRequest;
        }) =>
            apiSend<ReplayToPreview>(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/replay-to/preview`,
                "POST",
                vars.request,
            ),
        onError: (error) =>
            notify("error", "Couldn't preview replay", describeApiError(error)),
    });
}

/**
 * Starts the journaled replay op — returns the op record to poll. On success
 * both the SOURCE entity's lists AND the target namespace's are invalidated:
 * move semantics drain the source, and copies landing on the target change its
 * counts even when it's another namespace entirely.
 */
export function useSbReplayToStart() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (vars: {
            nsId: string;
            entityPath: string;
            request: ReplayToRequest;
        }) =>
            apiSend<SbOperationStatus>(
                `/api/servicebus/${vars.nsId}/entities/${entitySegment(vars.entityPath)}/replay-to/start`,
                "POST",
                vars.request,
            ),
        onSuccess: (_data, vars) => {
            qc.invalidateQueries({
                queryKey: ["sb-operations", vars.nsId, vars.entityPath],
            });
            invalidateServiceBusQueries(qc, vars.nsId, vars.entityPath);
            invalidateServiceBusQueries(
                qc,
                vars.request.targetNsId,
                vars.request.targetEntityPath,
            );
        },
        onError: (error) =>
            notify("error", "Couldn't start replay", describeApiError(error)),
    });
}
