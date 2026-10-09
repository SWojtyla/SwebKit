import { useState, useEffect, useCallback, useMemo } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router";
import { ChevronLeft, Sparkles, Timer } from "lucide-react";
import { ContextualAssistant } from "@/components/agent/ContextualAssistant";
import {
    useProfile,
    useSbPeekMessages,
    useSbPeekDlq,
    useSbEntityStats,
    useSbPurgeMessages,
    useSbQueues,
    useSbSubscriptions,
    invalidateServiceBusQueries,
} from "@/lib/hooks";
import { useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import { useNotification } from "@/components/layout/notification-context";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { PinResourceButton } from "@/components/shared/PinResourceButton";
import { pinServiceBusEntity } from "@/lib/pinned-resources";
import { ResizablePanels } from "@/components/ui/ResizablePanels";
import { EntityTree } from "./EntityTree";
import { MessageList } from "./MessageList";
import { MessageDetail } from "./MessageDetail";
import { SidePanel } from "./SidePanel";
import { MessageComposer, type ComposerMode } from "./MessageComposer";
import { BatchSendPanel } from "./BatchSendPanel";
import { ScheduledMessages } from "./ScheduledMessages";
import {
    EntityCommandPalette,
    type EntityAction,
} from "./EntityCommandPalette";
import { BatchReplayPanel } from "./BatchReplayPanel";
import { ReachMessagePanel } from "./ReachMessagePanel";
import { DlqTriagePanel } from "./DlqTriagePanel";
import { ReplayToPanel } from "./ReplayToPanel";
import { EntityPropertiesPanel } from "./EntityPropertiesPanel";
import { SbOperationsBanner } from "./SbOperationsBanner";
import { TemplateManager } from "./TemplateManager";
import { NamespaceOverview } from "./NamespaceOverview";
import {
    loadLastNamespace,
    saveLastNamespace,
    loadLastEntity,
    saveLastEntity,
} from "@/lib/stores/sb-selection";
import {
    requiresSessions,
    SESSIONS_NOT_SUPPORTED_TOOLTIP,
} from "./sessionHelpers";
import { useScreenStateProvider } from "@/lib/stores/screen-state";
import { isScheduledMessage } from "./filterLogic";
import { SbRibbon } from "./SbRibbon";
import { useSbListControls } from "./useSbListControls";
import type {
    SbEntityInfo,
    SbMessage,
    SbMessageTemplate,
    SbViewMode,
} from "@/lib/types";

function maxSequenceNumber(messages: SbMessage[]): number | null {
    const values = messages
        .map((m) => m.sequenceNumber)
        .filter((n): n is number => n != null);
    if (values.length === 0) return null;
    return Math.max(...values);
}

function messageKey(m: SbMessage): string {
    return `${m.messageId}-${m.sequenceNumber ?? ""}`;
}

function composerTitle(mode: ComposerMode): string {
    if (mode === "schedule") return "Schedule Message";
    if (mode === "editResubmit")
        return "Edit & Resubmit (settles DLQ original)";
    if (mode === "replay" || mode === "edit") return "Replay Message";
    return "Compose Message";
}

/** `entity/subscriptions/name` → `entity`; everything else → null. */
function topicFromEntityPath(
    entityPath: string | null | undefined,
): string | null {
    if (!entityPath) return null;
    const marker = "/subscriptions/";
    const idx = entityPath.indexOf(marker);
    return idx > 0 ? entityPath.slice(0, idx) : null;
}

export function ServiceBusPage() {
    const { data: profile } = useProfile();
    const location = useLocation();
    const navigate = useNavigate();
    const [searchParams, setSearchParams] = useSearchParams();
    const [composerMode, setComposerMode] = useState<ComposerMode | null>(null);
    const [composerTemplate, setComposerTemplate] =
        useState<SbMessageTemplate | null>(null);
    const [askAiOpen, setAskAiOpen] = useState(false);
    const [showBatchSend, setShowBatchSend] = useState(false);
    const [showScheduled, setShowScheduled] = useState(false);
    const [showEntityPalette, setShowEntityPalette] = useState(false);
    const [showBatchReplay, setShowBatchReplay] = useState(false);
    const [showReachPanel, setShowReachPanel] = useState(false);
    const [showDlqTriage, setShowDlqTriage] = useState(false);
    const [showReplayTo, setShowReplayTo] = useState(false);
    const [showTemplates, setShowTemplates] = useState(false);
    const [showEntityTree, setShowEntityTree] = useState(true);
    const [showPurgeConfirm, setShowPurgeConfirm] = useState(false);
    // Commands the list owns (ZIP needs the selection set) — registered via onActionsReady.
    const [listApi, setListApi] = useState<{
        downloadZip: () => void;
        zipDisabled: boolean;
    } | null>(null);
    const queryClient = useQueryClient();
    const { notify } = useNotification();
    const purgeMutation = useSbPurgeMessages();

    const namespaces = useMemo(
        () => profile?.serviceBusNamespaces ?? [],
        [profile?.serviceBusNamespaces],
    );

    // Every composer open goes through here so the optional template prefill is
    // cleared unless a template is actually being applied.
    const openComposer = useCallback(
        (mode: ComposerMode, template?: SbMessageTemplate | null) => {
            setComposerTemplate(template ?? null);
            setComposerMode(mode);
        },
        [],
    );

    const updateParams = useCallback(
        (
            updates: Record<string, string | null | undefined>,
            options?: { replace?: boolean },
        ) => {
            const next = new URLSearchParams(searchParams);
            for (const [key, value] of Object.entries(updates)) {
                if (value === null || value === undefined || value === "")
                    next.delete(key);
                else next.set(key, value);
            }
            setSearchParams(next, {
                replace: options?.replace ?? false,
                preventScrollReset: true,
            });
        },
        [searchParams, setSearchParams],
    );

    // Drill-down state is read from the URL so back/forward and deep links work.
    const selectedNsId = searchParams.get("ns");
    const setSelectedNsId = useCallback(
        (id: string | null) => {
            updateParams({
                ns: id,
                entity: null,
                entityName: null,
                msg: null,
                seq: null,
                view: null,
            });
            if (id) saveLastNamespace(id);
        },
        [updateParams],
    );

    const urlEntity = useMemo<SbEntityInfo | null>(() => {
        const entityPath = searchParams.get("entity");
        if (!entityPath) return null;
        const name = searchParams.get("entityName") || entityPath;
        return { entityPath, name } as SbEntityInfo;
    }, [searchParams]);

    const entityStats = useSbEntityStats(
        selectedNsId,
        urlEntity?.entityPath ?? null,
    );

    // The selected entity is reconstructed from the URL (path + name only), so topology facts like
    // `requiresSession` would be lost on a restored/deep-linked selection. Look the entity up in the
    // already-loaded queue/subscription lists instead — these queries share cache keys with the
    // entity tree, so the lookup costs no extra request and revives as soon as topology lands.
    const { data: topologyQueues } = useSbQueues(selectedNsId);
    const { data: topologySubs } = useSbSubscriptions(
        selectedNsId,
        topicFromEntityPath(urlEntity?.entityPath),
    );
    const topologyEntity = useMemo(
        () =>
            urlEntity
                ? (topologyQueues?.find(
                      (e) => e.entityPath === urlEntity.entityPath,
                  ) ??
                  topologySubs?.find(
                      (e) => e.entityPath === urlEntity.entityPath,
                  ) ??
                  null)
                : null,
        [urlEntity, topologyQueues, topologySubs],
    );

    const selectedEntity = useMemo<SbEntityInfo | null>(() => {
        if (!urlEntity) return null;
        return {
            ...urlEntity,
            stats: entityStats.data ?? null,
            requiresSession: topologyEntity?.requiresSession ?? false,
        };
    }, [urlEntity, entityStats.data, topologyEntity]);
    const setSelectedEntity = useCallback(
        (entity: SbEntityInfo | null) => {
            updateParams({
                entity: entity?.entityPath ?? null,
                entityName: entity?.name ?? null,
                msg: null,
                seq: null,
            });
            if (selectedNsId && entity)
                saveLastEntity(selectedNsId, {
                    entityPath: entity.entityPath,
                    name: entity.name,
                });
        },
        [updateParams, selectedNsId],
    );

    // Restore the last-selected namespace once the namespace list loads, and
    // within it the last-selected entity — mirroring the AKS workspace's
    // per-cluster namespace restoration. Only when the URL carries no selection
    // yet, so a fresh pick or a deep link always wins over a stored one.
    useEffect(() => {
        if (searchParams.get("ns") || namespaces.length === 0) return;
        const lastNs = loadLastNamespace();
        if (!lastNs || !namespaces.some((ns) => ns.id === lastNs)) return;
        const lastEntity = loadLastEntity(lastNs);
        updateParams(
            {
                ns: lastNs,
                entity: lastEntity?.entityPath ?? null,
                entityName: lastEntity?.name ?? null,
            },
            { replace: true },
        );
    }, [searchParams, namespaces, updateParams]);

    useEffect(() => {
        if (!selectedNsId || searchParams.get("entity")) return;
        const lastEntity = loadLastEntity(selectedNsId);
        if (!lastEntity) return;
        updateParams(
            { entity: lastEntity.entityPath, entityName: lastEntity.name },
            { replace: true },
        );
    }, [selectedNsId, searchParams, updateParams]);

    const viewMode = useMemo<SbViewMode>(() => {
        const v = searchParams.get("view");
        return v === "dlq" || v === "scheduled" ? v : "active";
    }, [searchParams]);
    const setViewMode = useCallback(
        (mode: SbViewMode) => updateParams({ view: mode }),
        [updateParams],
    );

    // Filter/prefs state lives here so the ribbon's Messages/View tabs and the list share
    // one source — see useSbListControls for the full bundle.
    const listControls = useSbListControls(selectedNsId, selectedEntity);
    const peekCount = listControls.prefs.peekCount;

    // Scheduled messages live in the ordinary queue (they peek alongside active ones), so the
    // Scheduled view reuses the active peek — the DLQ is the only separate listing.
    const activeMessagesQuery = useSbPeekMessages(
        viewMode !== "dlq" ? selectedNsId : null,
        selectedEntity?.entityPath ?? null,
        peekCount,
    );
    const dlqMessagesQuery = useSbPeekDlq(
        viewMode === "dlq" ? selectedNsId : null,
        selectedEntity?.entityPath ?? null,
        peekCount,
    );
    const peekData =
        viewMode === "dlq" ? dlqMessagesQuery.data : activeMessagesQuery.data;

    const [messageWindow, setMessageWindow] = useState<SbMessage[]>([]);
    const [lastSeq, setLastSeq] = useState<number | null>(null);
    const [lastBatchLength, setLastBatchLength] = useState(0);
    const [isLoadingMore, setIsLoadingMore] = useState(false);
    const [lastRefreshedAt, setLastRefreshedAt] = useState<number | null>(null);

    useEffect(() => {
        if (peekData) {
            // eslint-disable-next-line react-hooks/set-state-in-effect -- mirrors fetched data into a locally-mutable window (loadMore appends to it); Date.now() can't run during render
            setMessageWindow(peekData);
            setLastSeq(maxSequenceNumber(peekData));
            setLastBatchLength(peekData.length);
            setLastRefreshedAt(Date.now());
        }
    }, [peekData]);

    const loadMore = useCallback(async () => {
        if (
            !selectedNsId ||
            !selectedEntity ||
            lastSeq == null ||
            isLoadingMore
        )
            return;
        setIsLoadingMore(true);
        try {
            const mode = viewMode === "dlq" ? "dlq" : "peek";
            const next = await apiFetch<SbMessage[]>(
                `/api/servicebus/${selectedNsId}/entities/${encodeURIComponent(selectedEntity.entityPath)}/${mode}?count=${peekCount}&fromSeq=${lastSeq + 1}`,
            );
            setMessageWindow((prev) => {
                const seen = new Set(prev.map(messageKey));
                return [
                    ...prev,
                    ...next.filter((m) => !seen.has(messageKey(m))),
                ];
            });
            setLastSeq((prev) =>
                Math.max(prev ?? 0, maxSequenceNumber(next) ?? 0),
            );
            setLastBatchLength(next.length);
        } finally {
            setIsLoadingMore(false);
        }
    }, [
        selectedNsId,
        selectedEntity,
        viewMode,
        lastSeq,
        peekCount,
        isLoadingMore,
    ]);

    const totalAvailable = selectedEntity?.stats
        ? viewMode === "dlq"
            ? selectedEntity.stats.deadLetterMessageCount
            : viewMode === "scheduled"
              ? selectedEntity.stats.scheduledMessageCount
              : selectedEntity.stats.activeMessageCount
        : null;

    // The window mixes states on the scheduled view — compare only the relevant slice
    // against its broker-side total so "Load more" stays honest.
    const loadedForView =
        viewMode === "scheduled"
            ? messageWindow.filter(isScheduledMessage).length
            : messageWindow.length;
    const canLoadMore =
        (totalAvailable != null && loadedForView < totalAvailable) ||
        lastBatchLength === peekCount;

    const selectedMessage = useMemo<SbMessage | null>(() => {
        const msgId = searchParams.get("msg");
        const seq = searchParams.get("seq");
        if (!msgId || seq === null || !messageWindow.length) return null;
        const seqNum = parseInt(seq, 10);
        return (
            messageWindow.find(
                (m) => m.messageId === msgId && m.sequenceNumber === seqNum,
            ) ?? null
        );
    }, [searchParams, messageWindow]);

    // Screen-state snapshot (agent-workspace-awareness M1) — bounded: no full message bodies,
    // just metadata + a short body preview so the model can reason about what's on screen.
    useScreenStateProvider(
        "service-bus-page",
        "ServiceBus",
        () => {
            const nsAlias =
                namespaces.find((n) => n.id === selectedNsId)?.alias ?? null;
            const summarize = (m: SbMessage) => ({
                messageId: m.messageId,
                sequenceNumber: m.sequenceNumber,
                subject: m.subject,
                enqueuedAt: m.enqueuedAt,
                deliveryCount: m.deliveryCount,
                deadLetterReason: m.deadLetterReason,
                bodyPreview: m.body?.slice(0, 300) ?? null,
            });
            return {
                namespace: nsAlias,
                entity: selectedEntity
                    ? {
                          path: selectedEntity.entityPath,
                          name: selectedEntity.name,
                          activeMessages:
                              selectedEntity.stats?.activeMessageCount ?? null,
                          deadLetterMessages:
                              selectedEntity.stats?.deadLetterMessageCount ??
                              null,
                      }
                    : null,
                viewMode,
                visibleMessageCount: messageWindow.length,
                messages: messageWindow.slice(0, 15).map(summarize),
                selectedMessage: selectedMessage
                    ? summarize(selectedMessage)
                    : null,
            };
        },
        [
            namespaces,
            selectedNsId,
            selectedEntity,
            viewMode,
            messageWindow,
            selectedMessage,
        ],
    );
    const selectMessage = useCallback(
        (message: SbMessage | null) =>
            updateParams({
                msg: message?.messageId ?? null,
                seq:
                    message?.sequenceNumber != null
                        ? String(message.sequenceNumber)
                        : null,
            }),
        [updateParams],
    );

    // Apply a namespace selected from the command palette.
    useEffect(() => {
        const state = location.state as { nsId?: string } | null;
        if (state?.nsId && namespaces.some((ns) => ns.id === state.nsId)) {
            const next = new URLSearchParams();
            next.set("ns", state.nsId);
            navigate(
                { pathname: location.pathname, search: next.toString() },
                { replace: true, state: null },
            );
        }
    }, [location, navigate, namespaces]);

    // "New Service Bus message" palette action: `state.compose` opens the
    // composer. An object form can target a specific namespace/entity
    // (`{ nsId, entityPath, entityName }`), written as canonical `?ns=&entity=`
    // params so the landing URL is shareable.
    useEffect(() => {
        const state = location.state as {
            compose?:
                | boolean
                | { nsId?: string; entityPath?: string; entityName?: string };
        } | null;
        if (!state?.compose) return;
        const target = typeof state.compose === "object" ? state.compose : null;
        const next = new URLSearchParams(location.search);
        if (target?.nsId && namespaces.some((ns) => ns.id === target.nsId)) {
            next.set("ns", target.nsId);
            if (target.entityPath) {
                next.set("entity", target.entityPath);
                next.set("entityName", target.entityName ?? target.entityPath);
            }
        }
        // eslint-disable-next-line react-hooks/set-state-in-effect -- one-shot location.state deep-link consumption; the paired navigate() must live in an effect anyway
        openComposer("compose");
        navigate(
            { pathname: location.pathname, search: next.toString() },
            { replace: true, state: null },
        );
    }, [location, navigate, namespaces, openComposer]);

    // "Replay to…" palette/deep-link action: `state.replayTo` opens the
    // cross-environment replay wizard for the currently selected entity. Object
    // form can target a specific namespace/entity, written as canonical
    // `?ns=&entity=` params — same convention as `state.compose`.
    useEffect(() => {
        const state = location.state as {
            replayTo?:
                | boolean
                | { nsId?: string; entityPath?: string; entityName?: string };
        } | null;
        if (!state?.replayTo) return;
        const target =
            typeof state.replayTo === "object" ? state.replayTo : null;
        const next = new URLSearchParams(location.search);
        if (target?.nsId && namespaces.some((ns) => ns.id === target.nsId)) {
            next.set("ns", target.nsId);
            if (target.entityPath) {
                next.set("entity", target.entityPath);
                next.set("entityName", target.entityName ?? target.entityPath);
            }
        }
        // eslint-disable-next-line react-hooks/set-state-in-effect -- one-shot location.state deep-link consumption; the paired navigate() must live in an effect anyway
        setShowReplayTo(true);
        navigate(
            { pathname: location.pathname, search: next.toString() },
            { replace: true, state: null },
        );
    }, [location, navigate, namespaces]);

    const handleEntityAction = useCallback(
        (entity: SbEntityInfo, action: EntityAction) => {
            setSelectedEntity(entity);
            if (action === "peek-active") setViewMode("active");
            if (action === "peek-dlq") setViewMode("dlq");
            if (action === "send") openComposer("compose");
            // `invalidateQueries({ queryKey: ["sb-"] })` matched nothing — TanStack Query compares key
            // elements, not string prefixes, and every real key here is ["sb-peek", nsId, entityPath, …]
            // and friends. Reuse the real key set instead of re-deriving it.
            if (action === "refresh" && selectedNsId) {
                // Refresh is the one path that should re-read topology too — a deployment may have added or
                // removed entities since the tree loaded. Mutations deliberately leave the tree alone.
                invalidateServiceBusQueries(
                    queryClient,
                    selectedNsId,
                    entity.entityPath,
                    { includeTopology: true },
                );
            }
            // Previously fell through every branch — presented as a working destructive action while
            // doing nothing. Routes through the same entity-level confirm as the toolbar's Purge All.
            // Session entities can't purge (receive-complete needs session receivers) — say so inline.
            if (action === "purge") {
                if (requiresSessions(entity)) {
                    notify(
                        "error",
                        "Couldn't purge messages",
                        SESSIONS_NOT_SUPPORTED_TOOLTIP,
                    );
                    return;
                }
                setShowPurgeConfirm(true);
            }
        },
        [
            queryClient,
            selectedNsId,
            setSelectedEntity,
            setViewMode,
            openComposer,
            notify,
        ],
    );

    const onPurgeAll = useCallback(() => {
        if (!selectedNsId || !selectedEntity) return;
        // Purge rides a plain receive-complete loop — session entities reject it; say so instead of
        // letting the broker error surface after the confirm bar already implied it would run.
        if (requiresSessions(selectedEntity)) {
            notify(
                "error",
                "Couldn't purge messages",
                SESSIONS_NOT_SUPPORTED_TOOLTIP,
            );
            setShowPurgeConfirm(false);
            return;
        }
        const scope = viewMode === "dlq" ? "dead-lettered" : "active";
        purgeMutation.mutate(
            {
                nsId: selectedNsId,
                entityPath: selectedEntity.entityPath,
                deadLetter: viewMode === "dlq",
            },
            {
                onSuccess: () =>
                    notify("success", `Purged all ${scope} messages`),
            },
        );
        setShowPurgeConfirm(false);
    }, [selectedNsId, selectedEntity, viewMode, purgeMutation, notify]);

    useEffect(() => {
        const handler = (e: KeyboardEvent) => {
            if (
                (e.ctrlKey || e.metaKey) &&
                e.shiftKey &&
                (e.key === "e" || e.key === "E")
            ) {
                e.preventDefault();
                setShowEntityPalette((prev) => !prev);
            }
        };
        window.addEventListener("keydown", handler);
        return () => window.removeEventListener("keydown", handler);
    }, []);

    const selectedNs = namespaces.find((ns) => ns.id === selectedNsId);

    // Message-list column — rendered inside the resizable pair when the entity tree is
    // shown, or as the sole column when it's collapsed. Kept as one fragment so both
    // layouts share the breadcrumb/view tabs/purge/message-list markup verbatim.
    const listColumn = (
        <>
            {selectedEntity && (
                <div
                    className="flex items-center gap-2 border-b px-3 py-1.5 text-xs"
                    data-testid="sb-breadcrumb"
                >
                    <button
                        type="button"
                        onClick={() => setSelectedEntity(null)}
                        className="flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        title="Return to entity overview"
                    >
                        <ChevronLeft className="h-3 w-3" /> Overview
                    </button>
                    <span className="text-muted-foreground">/</span>
                    <span
                        className="truncate text-muted-foreground"
                        title={selectedNs?.alias ?? selectedNsId ?? ""}
                    >
                        {selectedNs?.alias ?? selectedNsId}
                    </span>
                    <span className="text-muted-foreground">/</span>
                    <span
                        className="truncate font-medium"
                        title={selectedEntity.name}
                    >
                        {selectedEntity.name}
                    </span>
                    {selectedNsId && (
                        <PinResourceButton
                            resource={pinServiceBusEntity(
                                selectedNsId,
                                selectedNs?.alias ?? selectedNsId,
                                selectedEntity,
                            )}
                            testId="sb-pin-entity"
                        />
                    )}
                    <button
                        type="button"
                        onClick={() => setAskAiOpen(true)}
                        className="ml-auto flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        title="Ask AI about this entity"
                        data-testid="sb-ask-ai-btn"
                    >
                        <Sparkles className="h-3.5 w-3.5" />
                    </button>
                </div>
            )}
            {askAiOpen && selectedEntity && (
                <ContextualAssistant
                    featureArea="ServiceBus"
                    title={`entity ${selectedEntity.name}`}
                    selection={{
                        entityPath: selectedEntity.entityPath,
                        ...(selectedNsId ? { nsId: selectedNsId } : {}),
                    }}
                    onClose={() => setAskAiOpen(false)}
                />
            )}
            {selectedEntity && (
                <div className="flex items-center gap-1 border-b px-3 py-1.5">
                    {/* State is the primary axis of a Service Bus list — a peek returns
                        scheduled messages indistinguishable from active ones unless the view
                        splits on the stamp. */}
                    <div
                        className="inline-flex rounded-md border p-0.5"
                        role="tablist"
                        data-testid="sb-view-selector"
                    >
                        {[
                            {
                                id: "active" as const,
                                label: "Active",
                                count: selectedEntity.stats?.activeMessageCount,
                                testId: "sb-view-active",
                                icon: null,
                            },
                            {
                                id: "scheduled" as const,
                                label: "Scheduled",
                                count: selectedEntity.stats
                                    ?.scheduledMessageCount,
                                testId: "sb-view-scheduled",
                                icon: Timer,
                            },
                            {
                                id: "dlq" as const,
                                label: "DLQ",
                                count: selectedEntity.stats
                                    ?.deadLetterMessageCount,
                                testId: "sb-view-dlq",
                                icon: null,
                            },
                        ].map((s) => (
                            <button
                                key={s.id}
                                data-testid={s.testId}
                                role="tab"
                                aria-selected={viewMode === s.id}
                                onClick={() => setViewMode(s.id)}
                                className={`flex items-center gap-1 rounded px-2.5 py-1 text-xs font-medium ${
                                    viewMode === s.id
                                        ? "bg-primary/15 text-primary"
                                        : "text-muted-foreground hover:text-foreground"
                                }`}
                            >
                                {s.icon && <s.icon className="h-3 w-3" />}
                                {s.label}
                                {s.count != null && (
                                    <span className="text-[10px] opacity-80">
                                        {s.count}
                                    </span>
                                )}
                            </button>
                        ))}
                    </div>
                </div>
            )}
            {showPurgeConfirm && selectedEntity && (
                <ConfirmBar
                    message={
                        <>
                            Purge all{" "}
                            {viewMode === "dlq" ? "dead-lettered" : "active"}{" "}
                            messages from{" "}
                            <strong>{selectedEntity.entityPath}</strong>? This
                            cannot be undone.
                        </>
                    }
                    confirmLabel="Purge"
                    confirmDisabled={purgeMutation.isPending}
                    onConfirm={onPurgeAll}
                    onCancel={() => setShowPurgeConfirm(false)}
                    testId="purge-confirm"
                    confirmTestId="purge-confirm-yes"
                    cancelTestId="purge-confirm-cancel"
                />
            )}
            {/* Interrupted/running ops for this entity — the crash-recovery surface.
          Reach ops get a live DLQ stamp scan; replay ops carry the journaled
          processed-set. */}
            {selectedEntity && selectedNsId && (
                <SbOperationsBanner
                    nsId={selectedNsId}
                    entity={selectedEntity}
                />
            )}
            {/* Read-only broker configuration — max size, TTL, lock duration, delivery
          caps, partitioning/session flags. Collapsed by default; one fetch per open. */}
            {selectedEntity && selectedNsId && (
                <EntityPropertiesPanel
                    nsId={selectedNsId}
                    entityPath={selectedEntity.entityPath}
                />
            )}
            {selectedEntity ? (
                <MessageList
                    nsId={selectedNsId}
                    entity={selectedEntity}
                    viewMode={viewMode}
                    controls={listControls}
                    messages={messageWindow}
                    isLoading={
                        viewMode === "dlq"
                            ? dlqMessagesQuery.isLoading
                            : activeMessagesQuery.isLoading
                    }
                    isError={
                        viewMode === "dlq"
                            ? dlqMessagesQuery.isError
                            : activeMessagesQuery.isError
                    }
                    error={
                        viewMode === "dlq"
                            ? dlqMessagesQuery.error
                            : activeMessagesQuery.error
                    }
                    isFetching={
                        viewMode === "dlq"
                            ? dlqMessagesQuery.isFetching
                            : activeMessagesQuery.isFetching
                    }
                    onRefresh={() =>
                        viewMode === "dlq"
                            ? dlqMessagesQuery.refetch()
                            : activeMessagesQuery.refetch()
                    }
                    lastRefreshedAt={lastRefreshedAt}
                    isLoadingMore={isLoadingMore}
                    canLoadMore={canLoadMore}
                    totalAvailable={totalAvailable}
                    selectedMessage={selectedMessage}
                    onSelectMessage={selectMessage}
                    onLoadMore={loadMore}
                    onActionsReady={setListApi}
                />
            ) : (
                <NamespaceOverview
                    nsId={selectedNsId}
                    namespaces={namespaces}
                    onSelectEntity={(e, mode) => {
                        setSelectedEntity(e);
                        if (mode) setViewMode(mode);
                    }}
                />
            )}
        </>
    );

    return (
        <div className="flex h-full flex-col" data-testid="service-bus-page">
            <SbRibbon
                namespaces={namespaces}
                selectedNsId={selectedNsId}
                onSelectNamespace={setSelectedNsId}
                entity={selectedEntity}
                viewMode={viewMode}
                controls={listControls}
                onRefresh={() =>
                    viewMode === "dlq"
                        ? dlqMessagesQuery.refetch()
                        : activeMessagesQuery.refetch()
                }
                isFetching={
                    viewMode === "dlq"
                        ? dlqMessagesQuery.isFetching
                        : activeMessagesQuery.isFetching
                }
                onDownloadZip={() => listApi?.downloadZip()}
                zipDisabled={!listApi || listApi.zipDisabled}
                onCompose={() => openComposer("compose")}
                onBatchSend={() => setShowBatchSend(true)}
                onScheduled={() => setShowScheduled(true)}
                onTemplates={() => setShowTemplates(true)}
                onEntitySearch={() => setShowEntityPalette(true)}
                onAskAi={() => setAskAiOpen(true)}
                onBatchReplay={() => setShowBatchReplay(true)}
                onReachMessage={() => setShowReachPanel(true)}
                onDlqTriage={() => setShowDlqTriage(true)}
                onReplayTo={() => setShowReplayTo(true)}
                onPurge={() => setShowPurgeConfirm(true)}
                purgePending={purgeMutation.isPending}
                sessionBlocked={
                    !!selectedEntity && requiresSessions(selectedEntity)
                }
                showEntityTree={showEntityTree}
                onToggleEntityTree={() => setShowEntityTree((v) => !v)}
            />
            {namespaces.length === 0 && (
                <button
                    onClick={() =>
                        navigate("/settings", { state: { tab: "service-bus" } })
                    }
                    className="self-start px-4 py-1 text-xs text-primary underline"
                    data-testid="sb-goto-settings"
                >
                    Configure namespaces in Settings
                </button>
            )}

            {/* Main content: entity tree | message list | detail */}
            <div className="flex flex-1 overflow-hidden">
                {/* Entity tree — resizable against the message list (Storage/API Client share
            ResizablePanels); the collapsed "Entities" strip keeps the old hide affordance. */}
                {showEntityTree ? (
                    <ResizablePanels
                        initialWidths={[256, "1fr"]}
                        minWidths={[180, 320]}
                        storageKey="service-bus-panels"
                        panelLabels={["entities", "messages"]}
                        className="min-w-0 flex-1"
                    >
                        {[
                            <div
                                key="entities"
                                className="h-full overflow-auto border-r"
                            >
                                <EntityTree
                                    nsId={selectedNsId}
                                    selectedEntity={selectedEntity}
                                    onSelectEntity={(entity, mode) => {
                                        setSelectedEntity(entity);
                                        if (mode) setViewMode(mode);
                                    }}
                                />
                            </div>,
                            <div
                                key="messages"
                                className="flex h-full min-w-0 flex-col overflow-hidden border-r"
                            >
                                {listColumn}
                            </div>,
                        ]}
                    </ResizablePanels>
                ) : (
                    <>
                        <button
                            data-testid="show-entity-tree"
                            onClick={() => setShowEntityTree(true)}
                            className="flex items-center border-r bg-card px-1.5 py-2 text-xs text-muted-foreground hover:bg-accent hover:text-foreground"
                            title="Show entity tree"
                        >
                            Entities
                        </button>
                        <div className="flex min-w-0 flex-1 flex-col overflow-hidden border-r">
                            {listColumn}
                        </div>
                    </>
                )}

                {/* Detail pane */}
                {selectedMessage && (
                    <SidePanel
                        title="Message details"
                        onClose={() => selectMessage(null)}
                        defaultWidth={560}
                        minWidth={240}
                        // 600 was too narrow for the panel's own action row, so the last buttons
                        // (Replay, Schedule) were unreachable at any width the user could drag to.
                        maxWidth={1400}
                        storageKey="service-bus-message-detail"
                    >
                        <MessageDetail
                            message={selectedMessage}
                            nsId={selectedNsId}
                            entity={selectedEntity}
                            viewMode={viewMode}
                            onEditResubmit={(msg) => {
                                selectMessage(msg);
                                openComposer(
                                    viewMode === "dlq"
                                        ? "editResubmit"
                                        : "edit",
                                );
                            }}
                            onReplay={(msg) => {
                                selectMessage(msg);
                                openComposer("replay");
                            }}
                            onSchedule={(msg) => {
                                selectMessage(msg);
                                openComposer("schedule");
                            }}
                        />
                    </SidePanel>
                )}

                {/* Message composer — resizable side panel in the same flex row as the
            detail pane, not the old fixed modal, so the message list stays in
            view while composing/replaying. */}
                {composerMode && (
                    <SidePanel
                        title={composerTitle(composerMode)}
                        onClose={() => setComposerMode(null)}
                        defaultWidth={640}
                        minWidth={420}
                        maxWidth={1400}
                        storageKey="service-bus-composer"
                        data-testid="composer-panel"
                        closeTestId="composer-close"
                    >
                        <MessageComposer
                            mode={composerMode}
                            nsId={selectedNsId}
                            namespaces={namespaces}
                            entity={selectedEntity}
                            sourceMessage={
                                composerMode === "replay" ||
                                composerMode === "edit" ||
                                composerMode === "editResubmit"
                                    ? selectedMessage
                                    : null
                            }
                            initialTemplate={composerTemplate}
                            onClose={() => setComposerMode(null)}
                        />
                    </SidePanel>
                )}
            </div>

            {/* Batch send modal */}
            {showBatchSend && (
                <BatchSendPanel
                    nsId={selectedNsId}
                    namespaces={namespaces}
                    entity={selectedEntity}
                    onClose={() => setShowBatchSend(false)}
                />
            )}

            {/* Scheduled messages modal */}
            {showScheduled && selectedNsId && selectedEntity && (
                <ScheduledMessages
                    nsId={selectedNsId}
                    entityPath={selectedEntity.entityPath}
                    onClose={() => setShowScheduled(false)}
                />
            )}

            {/* Batch replay modal */}
            {showBatchReplay && selectedNsId && selectedEntity && (
                <BatchReplayPanel
                    nsId={selectedNsId}
                    entity={selectedEntity}
                    onClose={() => setShowBatchReplay(false)}
                />
            )}

            {/* Reach-message wizard — prefills the target from the selected message
          when the active view has one. */}
            {showReachPanel && selectedNsId && selectedEntity && (
                <ReachMessagePanel
                    nsId={selectedNsId}
                    entity={selectedEntity}
                    defaultTarget={
                        viewMode === "active"
                            ? selectedMessage?.sequenceNumber
                            : null
                    }
                    onClose={() => setShowReachPanel(false)}
                />
            )}

            {/* DLQ triage modal — reason/description groups over the peek window plus
          server-side requeue for groups bigger than the window. */}
            {showDlqTriage && selectedNsId && selectedEntity && (
                <DlqTriagePanel
                    nsId={selectedNsId}
                    entity={selectedEntity}
                    onClose={() => setShowDlqTriage(false)}
                />
            )}

            {/* Cross-environment replay wizard — sends the selected sequences to
          another namespace/entity as new stamped copies. Prefills the selected
          message's sequence and the current active/DLQ view. */}
            {showReplayTo && selectedNsId && selectedEntity && (
                <ReplayToPanel
                    nsId={selectedNsId}
                    entity={selectedEntity}
                    namespaces={namespaces}
                    defaultDeadLetter={viewMode === "dlq"}
                    defaultSequences={
                        selectedMessage?.sequenceNumber != null
                            ? [selectedMessage.sequenceNumber]
                            : []
                    }
                    onClose={() => setShowReplayTo(false)}
                />
            )}

            {/* Templates manager — "Use in composer" hands the template to a fresh
          compose panel. */}
            {showTemplates && (
                <TemplateManager
                    onUseInComposer={(t) => {
                        setShowTemplates(false);
                        openComposer("compose", t);
                    }}
                    onClose={() => setShowTemplates(false)}
                />
            )}

            {/* Entity command palette */}
            <EntityCommandPalette
                open={showEntityPalette}
                nsId={selectedNsId}
                onClose={() => setShowEntityPalette(false)}
                onSelectEntity={(entity) => {
                    setSelectedEntity(entity);
                }}
                onAction={handleEntityAction}
            />
        </div>
    );
}
