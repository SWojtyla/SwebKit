import {
    useState,
    useMemo,
    useCallback,
    useEffect,
    useRef,
    type ReactNode,
    type JSX,
} from "react";
import { useLocation, useNavigate } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import {
    useCollections,
    useUpdateCollections,
    useExecuteRequest,
    useEnvironments,
    useUpdateEnvironments,
    useLinkedRoots,
    useLinkedRootActions,
    useDemoMode,
    useApiRun,
    type ApiRunCallbacks,
} from "@/lib/hooks";
import type { ResponseHistoryEntry } from "./ResponseViewer";
import type { RequestTab } from "./RequestTabStrip";
import {
    ApiClientPageContext,
    ApiClientTabsContext,
    type ApiClientPageContextValue,
    type ApiClientTabsContextValue,
    type ConflictState,
    type ConfirmDialogState,
    type GitInitialRepo,
    type NameDialogState,
    type TabState,
} from "./api-client-context";
import { buildVariableScope } from "@/lib/variable-utils";
import { getSecret, pickDirectory, revealInExplorer } from "@/lib/tauri-bridge";
import { buildResponseExample } from "@/lib/response-example";
import { runRequestActions } from "@/lib/request-action-runner";
import { useNotification } from "@/components/layout/notification-context";
import { useScreenStateProvider } from "@/lib/stores/screen-state";
import {
    moveNode,
    moveCollection,
    findRequestNode,
    findParentFolderId,
    childIdsOf,
    describeNodeForDelete,
    formatDeleteMessage,
    insertRequestAtFolderPath,
    type MoveNodeTarget,
    type MoveCollectionTarget,
} from "@/lib/collection-tree-utils";
import {
    mergeCollections,
    mergeEnvironments,
    stripEnvironmentOrigin,
    requestFileState,
} from "@/lib/linked-root-utils";
import { pickNeighborTabId } from "@/lib/request-tab-utils";
import { describeApiError } from "@/lib/api";
import {
    DEFAULT_RUN_OPTIONS,
    lastResponseOf,
    type ApiRunOptions,
    type ApiRunState,
} from "@/lib/api-run-utils";
import type {
    ApiCollection,
    ApiCollectionNode,
    HttpRequestEntry,
    ApiClientExecutionResponse,
    ApiRequestMethod,
    ApiEnvironment,
    CollectionVariable,
    AuthConfig,
    CollectionsStoreResponse,
    ApiRunRequest,
} from "@/lib/types";

function newId() {
    return crypto.randomUUID();
}

function now() {
    return new Date().toISOString();
}

function deepClone<T>(obj: T): T {
    return typeof structuredClone === "function"
        ? structuredClone(obj)
        : JSON.parse(JSON.stringify(obj));
}

function emptyRequest(): HttpRequestEntry {
    return {
        id: newId(),
        name: "New Request",
        method: "Get" as ApiRequestMethod,
        url: "",
        headers: [],
        queryParams: [],
        body: {
            mode: "None",
            rawContent: null,
            contentType: null,
            formData: [],
            filePath: null,
        },
        auth: null,
        captureRules: [],
        graphQlQuery: null,
        graphQlVariables: null,
        graphQlSelectedOperation: null,
        savedMessages: [],
        wsSubProtocol: null,
        responseExamples: [],
        createdAt: now(),
        updatedAt: now(),
        preRequestActions: [],
        postRequestActions: [],
    };
}

const CREDENTIAL_KEY_PREFIX = "sw-secret:";

function isLegacyCredentialKey(auth: AuthConfig | null | undefined): boolean {
    return (
        !!auth?.credentialKey &&
        !auth.credentialKey.startsWith(CREDENTIAL_KEY_PREFIX)
    );
}

function countLegacySecrets(collections: ApiCollection[]): number {
    let count = 0;
    for (const collection of collections) {
        if (isLegacyCredentialKey(collection.defaultAuth)) count++;
        function walk(nodes: ApiCollectionNode[]) {
            for (const node of nodes) {
                if (node.type === "Folder") {
                    if (isLegacyCredentialKey(node.defaultAuth)) count++;
                    walk(node.children);
                } else if (node.request) {
                    if (isLegacyCredentialKey(node.request.auth)) count++;
                }
            }
        }
        walk(collection.nodes);
    }
    return count;
}

function removeNode(
    collections: ApiCollection[],
    nodeId: string,
): ApiCollection[] {
    return collections
        .filter((c) => c.id !== nodeId)
        .map((collection) => ({
            ...collection,
            nodes: removeFromNodes(collection.nodes, nodeId),
        }));
}

function removeFromNodes(
    nodes: ApiCollectionNode[],
    nodeId: string,
): ApiCollectionNode[] {
    return nodes
        .filter((n) => n.id !== nodeId)
        .map((n) =>
            n.type === "Folder"
                ? { ...n, children: removeFromNodes(n.children, nodeId) }
                : n,
        );
}

function insertIntoCollection(
    collections: ApiCollection[],
    collectionId: string,
    node: ApiCollectionNode,
    parentId?: string,
): ApiCollection[] {
    if (!parentId) {
        return collections.map((c) =>
            c.id === collectionId ? { ...c, nodes: [...c.nodes, node] } : c,
        );
    }
    return collections.map((c) =>
        c.id === collectionId
            ? { ...c, nodes: insertIntoNodes(c.nodes, parentId, node) }
            : c,
    );
}

function insertIntoNodes(
    nodes: ApiCollectionNode[],
    parentId: string,
    node: ApiCollectionNode,
): ApiCollectionNode[] {
    return nodes.map((n) => {
        if (n.id === parentId && n.type === "Folder") {
            return { ...n, children: [...n.children, node] };
        }
        if (n.type === "Folder") {
            return {
                ...n,
                children: insertIntoNodes(n.children, parentId, node),
            };
        }
        return n;
    });
}

function updateRequestInCollections(
    collections: ApiCollection[],
    nodeId: string,
    request: HttpRequestEntry,
): ApiCollection[] {
    return collections.map((collection) => ({
        ...collection,
        nodes: updateRequestInNodes(collection.nodes, nodeId, request),
    }));
}

function updateRequestInNodes(
    nodes: ApiCollectionNode[],
    nodeId: string,
    request: HttpRequestEntry,
): ApiCollectionNode[] {
    return nodes.map((n) => {
        if (n.id === nodeId) {
            return { ...n, name: request.name, request };
        }
        if (n.type === "Folder") {
            return {
                ...n,
                children: updateRequestInNodes(n.children, nodeId, request),
            };
        }
        return n;
    });
}

function renameNodeInCollections(
    collections: ApiCollection[],
    nodeId: string,
    newName: string,
): ApiCollection[] {
    return collections.map((c) => {
        if (c.id === nodeId) return { ...c, name: newName };
        return { ...c, nodes: renameNodeInNodes(c.nodes, nodeId, newName) };
    });
}

function renameNodeInNodes(
    nodes: ApiCollectionNode[],
    nodeId: string,
    newName: string,
): ApiCollectionNode[] {
    return nodes.map((n) => {
        if (n.id === nodeId) return { ...n, name: newName };
        if (n.type === "Folder")
            return {
                ...n,
                children: renameNodeInNodes(n.children, nodeId, newName),
            };
        return n;
    });
}

/** The folder named `name` directly under `parentId` (null = collection root). */
function findFolderByName(
    collection: ApiCollection,
    parentId: string | null,
    name: string,
): ApiCollectionNode | null {
    const siblings =
        parentId === null
            ? collection.nodes
            : (findRequestNode(collection.nodes, parentId)?.children ?? []);
    return siblings.find((n) => n.type === "Folder" && n.name === name) ?? null;
}

/** Newest-first cap on per-tab response history. Session-only, as documented. */
const HISTORY_LIMIT = 20;

function emptyTabState(draft: HttpRequestEntry): TabState {
    return { draft, response: null, sending: false, dirty: false, history: [] };
}

/** Prepends a response to a tab's history, newest first, capped. */
function appendHistory(
    state: TabState | undefined,
    response: ApiClientExecutionResponse,
): ResponseHistoryEntry[] {
    const existing = state?.history ?? [];
    const nextId = (existing[0]?.id ?? 0) + 1;
    return [{ id: nextId, response, timestamp: Date.now() }, ...existing].slice(
        0,
        HISTORY_LIMIT,
    );
}

export function ApiClientPageProvider({
    children,
}: {
    children: ReactNode;
}): JSX.Element {
    const { notify } = useNotification();
    const { data: internalCollections = [], isLoading } = useCollections();
    // Linked roots ride on the same ["collections"] query — the store response
    // carries them alongside the internal collections.
    const { data: linkedRoots = [] } = useLinkedRoots();
    const linkedActions = useLinkedRootActions();
    const { data: demoMode } = useDemoMode();
    const updateCollections = useUpdateCollections();
    const executeRequest = useExecuteRequest();
    const { data: envData } = useEnvironments();
    const updateEnvironments = useUpdateEnvironments();
    // Mutation objects are fresh each render; their mutate functions are stable.
    const {
        mutate: updateCollectionsMutate,
        mutateAsync: updateCollectionsMutateAsync,
    } = updateCollections;
    const { mutateAsync: executeRequestMutateAsync } = executeRequest;
    const { mutate: updateEnvironmentsMutate } = updateEnvironments;
    const location = useLocation();
    const navigate = useNavigate();
    const qc = useQueryClient();

    const isDemo = demoMode?.isDemoMode ?? false;

    // The workspace collection list flattens internal collections and every
    // enabled linked root's collections, each tagged with its `origin` so the
    // mutation handlers below can route to the right backend.
    const collections = useMemo(
        () => mergeCollections(internalCollections, linkedRoots, isDemo),
        [internalCollections, linkedRoots, isDemo],
    );

    // Same merge for environments: internal ones plus every linked root's
    // environments (tagged with their rootId/filePath). Linked envs never go
    // to the internal environments PUT — they route to the linked endpoints.
    const environments = useMemo(
        () =>
            mergeEnvironments(envData?.environments ?? [], linkedRoots, isDemo),
        [envData, linkedRoots, isDemo],
    );
    /** What the internal environments PUT is allowed to see — origin stripped. */
    const internalEnvironments = useMemo(
        () =>
            environments
                .filter((e) => e.origin?.kind !== "linked")
                .map(stripEnvironmentOrigin),
        [environments],
    );
    const uiState = envData?.uiState;
    const activeEnvironmentId = uiState?.activeEnvironmentId ?? null;

    /// Resolves the two environment layers that apply to a collection: a global one
    /// and a collection-scoped one, both active at once with the scoped one winning.
    /// Both slots already existed in the stored UI state
    /// (`activeEnvironmentId` and `activeEnvironmentIdByCollection`) but only the
    /// first was ever read, so a value shared by every `DEV (via …)` environment had
    /// to be duplicated into each of them.
    ///
    /// Used by both the preview scope and the send payload. Resolving it twice is how
    /// the preview would start describing something other than what is sent.
    const resolveEnvironmentLayers = useCallback(
        (collectionId: string | null | undefined) => {
            const selected =
                environments.find((e) => e.id === activeEnvironmentId) ?? null;

            // The global slot can still hold a collection-scoped environment picked before
            // the two layers existed. Honour it as that collection's project selection
            // rather than applying an environment scoped to somewhere else.
            const global =
                selected && selected.collectionId === null ? selected : null;

            const scopedId = collectionId
                ? (uiState?.activeEnvironmentIdByCollection?.[collectionId] ??
                  null)
                : null;
            const scoped =
                (scopedId
                    ? (environments.find((e) => e.id === scopedId) ?? null)
                    : null) ??
                (selected &&
                collectionId &&
                selected.collectionId === collectionId
                    ? selected
                    : null);

            return { global, scoped };
        },
        [
            environments,
            activeEnvironmentId,
            uiState?.activeEnvironmentIdByCollection,
        ],
    );

    const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
    const [selectedCollectionId, setSelectedCollectionId] = useState<
        string | null
    >(null);
    const [tabs, setTabs] = useState<RequestTab[]>([]);
    const [activeTabId, setActiveTabId] = useState<string | null>(null);
    const [tabStates, setTabStates] = useState<Record<string, TabState>>({});
    const [showEnvManager, setShowEnvManager] = useState(false);
    const [showColVarEditor, setShowColVarEditor] = useState(false);
    const [exportCollectionId, setExportCollectionId] = useState<string | null>(
        null,
    );
    const [showGitPanel, setShowGitPanel] = useState(false);
    const [gitInitialRepo, setGitInitialRepo] = useState<GitInitialRepo | null>(
        null,
    );
    const [conflict, setConflict] = useState<ConflictState | null>(null);
    const [legacyNoticeDismissed, setLegacyNoticeDismissed] = useState(
        () =>
            typeof window !== "undefined" &&
            localStorage.getItem("swokit-legacy-secret-notice") === "dismissed",
    );
    const legacySecretCount = useMemo(
        () => countLegacySecrets(collections),
        [collections],
    );
    const [nameDialog, setNameDialog] = useState<NameDialogState | null>(null);
    const [confirmDialog, setConfirmDialog] =
        useState<ConfirmDialogState | null>(null);

    // ── Request runs ────────────────────────────────────────────────
    // One run at a time — `useApiRun.start` aborts whatever was in flight. The
    // drawer's open flag lives here (not inside the hook's state) so closing it
    // never implies the run stopped.
    // Destructured rather than kept as one object — `{state,start,abort}` is a
    // fresh identity every render, and it's in the page-context memo's deps:
    // keeping it whole would re-render every context consumer on each keystroke.
    const { state: runState, start: startRun, abort: abortRun } = useApiRun();
    const [runDrawerOpen, setRunDrawerOpen] = useState(false);
    const [runOptions, setRunOptions] =
        useState<ApiRunOptions>(DEFAULT_RUN_OPTIONS);

    // Refs mirror the per-keystroke tab state so handlers living in the *page*
    // context (delete-node, conflict resolution, select-node) can read it without
    // depending on it — otherwise their identity, and the whole page-context
    // value, would churn on every editor keystroke.
    const tabsRef = useRef(tabs);
    const tabStatesRef = useRef(tabStates);
    const activeTabIdRef = useRef(activeTabId);
    // Origin lookups in event handlers must see the freshest merge (a handler's
    // render snapshot can lag the store the mutation just invalidated), so the
    // merged collections/environments/roots get mirror refs too.
    const collectionsRef = useRef(collections);
    const environmentsRef = useRef(environments);
    const linkedRootsRef = useRef(linkedRoots);
    const selectedNodeIdRef = useRef(selectedNodeId);
    const runOptionsRef = useRef(runOptions);
    useEffect(() => {
        tabsRef.current = tabs;
        tabStatesRef.current = tabStates;
        activeTabIdRef.current = activeTabId;
        collectionsRef.current = collections;
        environmentsRef.current = environments;
        linkedRootsRef.current = linkedRoots;
        selectedNodeIdRef.current = selectedNodeId;
        runOptionsRef.current = runOptions;
    });

    /**
     * `preview: true` (single-click tree navigation) reuses the one preview tab
     * instead of opening a new permanent one — browsing a collection should not
     * accumulate a tab per row clicked. `preview: false`/omitted (Add Request,
     * deep link, double-click) always opens or promotes to a permanent tab.
     */
    const openTab = useCallback(
        (
            node: ApiCollectionNode,
            collectionId: string,
            opts?: { preview?: boolean },
        ) => {
            if (node.type !== "Request" || !node.request) return;
            const preview = opts?.preview ?? false;

            const existingTab = tabsRef.current.find(
                (t) => t.nodeId === node.id,
            );
            if (existingTab) {
                // Deliberately reopening an already-open tab (not from a preview click)
                // is a revisit, not a throwaway peek — promote it if it was a preview.
                if (!preview && existingTab.isPreview) {
                    setTabs((prev) =>
                        prev.map((t) =>
                            t.id === existingTab.id
                                ? { ...t, isPreview: false }
                                : t,
                        ),
                    );
                }
                setActiveTabId(existingTab.id);
                return;
            }

            const existingPreviewTab = preview
                ? tabsRef.current.find(
                      (t) => t.isPreview && !tabStatesRef.current[t.id]?.dirty,
                  )
                : undefined;
            if (existingPreviewTab) {
                setTabs((prev) =>
                    prev.map((t) =>
                        t.id === existingPreviewTab.id
                            ? {
                                  ...t,
                                  nodeId: node.id,
                                  collectionId,
                                  name: node.name,
                                  method: node.request!.method,
                                  dirty: false,
                                  isPreview: true,
                              }
                            : t,
                    ),
                );
                setTabStates((prev) => ({
                    ...prev,
                    [existingPreviewTab.id]: emptyTabState(
                        deepClone(node.request!),
                    ),
                }));
                setActiveTabId(existingPreviewTab.id);
                return;
            }

            const tabId = newId();
            const tab: RequestTab = {
                id: tabId,
                nodeId: node.id,
                collectionId,
                name: node.name,
                method: node.request.method,
                dirty: false,
                isPreview: preview,
            };
            setTabs((prev) => [...prev, tab]);
            setTabStates((prev) => ({
                ...prev,
                [tabId]: emptyTabState(deepClone(node.request!)),
            }));
            setActiveTabId(tabId);
        },
        [],
    );

    useEffect(() => {
        const state = location.state as {
            collectionId?: string;
            nodeId?: string;
        } | null;
        if (!state?.collectionId || !state?.nodeId) return;
        const collection = collections.find((c) => c.id === state.collectionId);
        const node = collection
            ? findRequestNode(collection.nodes, state.nodeId)
            : null;
        if (node?.type === "Request" && node.request) {
            // eslint-disable-next-line react-hooks/set-state-in-effect -- one-shot location.state deep-link consumption; the paired navigate() must live in an effect anyway
            setSelectedCollectionId(state.collectionId);
            setSelectedNodeId(state.nodeId);
            openTab(node, state.collectionId);
        }
        navigate(location.pathname, { replace: true, state: null });
    }, [location, collections, navigate, openTab]);

    /** Closing the active tab activates a neighbor instead of falling back to
     *  blank — only closing the last remaining tab actually clears the editor. */
    const closeTab = useCallback(
        (tabId: string) => {
            const performClose = () => {
                setTabs((prev) => prev.filter((t) => t.id !== tabId));
                setTabStates((prev) => {
                    const next = { ...prev };
                    delete next[tabId];
                    return next;
                });
                if (activeTabId === tabId)
                    setActiveTabId(pickNeighborTabId(tabs, tabId));
            };

            const tabState = tabStates[tabId];
            if (tabState?.dirty) {
                setConfirmDialog({
                    message: `Close "${tabs.find((t) => t.id === tabId)?.name}" with unsaved changes?`,
                    confirmText: "Close",
                    onConfirm: () => {
                        performClose();
                        setConfirmDialog(null);
                    },
                });
                return;
            }
            performClose();
        },
        [tabStates, tabs, activeTabId],
    );

    /** Closes every tab except `keepTabId`, confirming first only when it would
     *  discard unsaved changes elsewhere. */
    const closeOtherTabs = useCallback(
        (keepTabId: string) => {
            const performClose = () => {
                setTabs((prev) => prev.filter((t) => t.id === keepTabId));
                setTabStates((prev) =>
                    prev[keepTabId] ? { [keepTabId]: prev[keepTabId] } : {},
                );
                setActiveTabId(keepTabId);
            };

            const others = tabs.filter((t) => t.id !== keepTabId);
            const hasDirtyOther = others.some((t) => tabStates[t.id]?.dirty);
            if (hasDirtyOther) {
                setConfirmDialog({
                    message: `Close ${others.length} other tab${others.length === 1 ? "" : "s"}? Unsaved changes will be lost.`,
                    confirmText: "Close",
                    onConfirm: () => {
                        performClose();
                        setConfirmDialog(null);
                    },
                });
                return;
            }
            performClose();
        },
        [tabs, tabStates],
    );

    /** Closes every open tab, confirming first only when any holds unsaved changes. */
    const closeAllTabs = useCallback(() => {
        const performClose = () => {
            setTabs([]);
            setTabStates({});
            setActiveTabId(null);
        };

        const hasDirty = tabs.some((t) => tabStates[t.id]?.dirty);
        if (hasDirty) {
            setConfirmDialog({
                message: `Close all ${tabs.length} tabs? Unsaved changes will be lost.`,
                confirmText: "Close",
                onConfirm: () => {
                    performClose();
                    setConfirmDialog(null);
                },
            });
            return;
        }
        performClose();
    }, [tabs, tabStates]);

    /** Promotes a preview tab to permanent (e.g. double-clicking it in the strip). */
    const promoteTab = useCallback((tabId: string) => {
        setTabs((prev) =>
            prev.map((t) => (t.id === tabId ? { ...t, isPreview: false } : t)),
        );
    }, []);

    const updateTabDraft = useCallback(
        (tabId: string, draft: HttpRequestEntry) => {
            setTabStates((prev) => ({
                ...prev,
                [tabId]: { ...prev[tabId], draft, dirty: true },
            }));
            // Editing a preview tab is a deliberate change, not a throwaway peek —
            // promote it so the next single-click preview does not replace it.
            setTabs((prev) =>
                prev.map((t) =>
                    t.id === tabId
                        ? {
                              ...t,
                              name: draft.name,
                              method: draft.method,
                              dirty: true,
                              isPreview: false,
                          }
                        : t,
                ),
            );
        },
        [],
    );

    const handleSelectNode = useCallback(
        (node: ApiCollectionNode, collectionId: string) => {
            setSelectedNodeId(node.id);
            setSelectedCollectionId(collectionId);
            if (node.type === "Request" && node.request) {
                openTab(node, collectionId, { preview: true });
            }
        },
        [openTab],
    );

    const handleAddCollection = useCallback(
        (linkedRootId?: string) => {
            setNameDialog({
                title: "New Collection",
                label: "Collection name",
                defaultValue: "",
                confirmText: "Create",
                onConfirm: (name) => {
                    setNameDialog(null);
                    if (linkedRootId) {
                        // A linked collection is a directory on disk — created via
                        // the root endpoint, never the whole-store PUT.
                        void linkedActions
                            .createCollection(linkedRootId, name)
                            .then((res) => {
                                if (res)
                                    setSelectedCollectionId(res.collectionId);
                            });
                        return;
                    }
                    const collection: ApiCollection = {
                        id: newId(),
                        name,
                        nodes: [],
                        variables: [],
                        defaultAuth: null,
                        createdAt: now(),
                        updatedAt: now(),
                    };
                    updateCollectionsMutate((prev) => [...prev, collection]);
                },
            });
        },
        [updateCollectionsMutate, linkedActions],
    );

    const handleAddRequest = useCallback(
        (collectionId: string, parentId?: string) => {
            const request = emptyRequest();
            setNameDialog({
                title: "New Request",
                label: "Request name",
                defaultValue: request.name,
                confirmText: "Create",
                onConfirm: (name) => {
                    request.name = name;
                    setNameDialog(null);
                    const rootId = collectionsRef.current.find(
                        (c) => c.id === collectionId,
                    )?.origin?.rootId;
                    if (rootId) {
                        void (async () => {
                            const res = await linkedActions.createRequest(
                                rootId,
                                collectionId,
                                {
                                    name,
                                    parentFolderId: parentId ?? null,
                                    request,
                                },
                            );
                            if (!res) return;
                            setSelectedNodeId(res.requestId);
                            setSelectedCollectionId(collectionId);
                            const tabId = newId();
                            setTabs((prev) => [
                                ...prev,
                                {
                                    id: tabId,
                                    nodeId: res.requestId,
                                    collectionId,
                                    name,
                                    method: request.method,
                                    dirty: false,
                                },
                            ]);
                            setTabStates((prev) => ({
                                ...prev,
                                [tabId]: emptyTabState(deepClone(request)),
                            }));
                            setActiveTabId(tabId);
                        })();
                        return;
                    }
                    const node: ApiCollectionNode = {
                        id: request.id,
                        type: "Request",
                        name,
                        isExpanded: true,
                        children: [],
                        defaultAuth: null,
                        request,
                    };
                    updateCollectionsMutate(
                        (prev) =>
                            insertIntoCollection(
                                prev,
                                collectionId,
                                node,
                                parentId,
                            ),
                        {
                            onSuccess: () => {
                                setSelectedNodeId(node.id);
                                setSelectedCollectionId(collectionId);
                                // Open tab for the new request
                                const tabId = newId();
                                const tab: RequestTab = {
                                    id: tabId,
                                    nodeId: node.id,
                                    collectionId,
                                    name,
                                    method: request.method,
                                    dirty: false,
                                };
                                setTabs((prev) => [...prev, tab]);
                                setTabStates((prev) => ({
                                    ...prev,
                                    [tabId]: emptyTabState(deepClone(request)),
                                }));
                                setActiveTabId(tabId);
                            },
                        },
                    );
                },
            });
        },
        [updateCollectionsMutate, linkedActions],
    );

    // "New API request" palette action: `state.newRequest` opens the create-request
    // dialog against the selected (or first) collection — the same dialog the
    // tree's "+" button opens.
    useEffect(() => {
        const state = location.state as { newRequest?: boolean } | null;
        if (!state?.newRequest) return;
        const target = selectedCollectionId ?? collections[0]?.id;
        if (target) {
            handleAddRequest(target);
        } else {
            notify(
                "info",
                "No API collections",
                "Create a collection first, then add requests to it.",
            );
        }
        navigate(location.pathname, { replace: true, state: null });
    }, [
        location,
        collections,
        selectedCollectionId,
        handleAddRequest,
        navigate,
        notify,
    ]);

    const handleAddFolder = useCallback(
        (collectionId: string, parentId?: string) => {
            setNameDialog({
                title: "New Folder",
                label: "Folder name",
                defaultValue: "",
                confirmText: "Create",
                onConfirm: (name) => {
                    setNameDialog(null);
                    const rootId = collectionsRef.current.find(
                        (c) => c.id === collectionId,
                    )?.origin?.rootId;
                    if (rootId) {
                        void linkedActions.createFolder(
                            rootId,
                            collectionId,
                            name,
                            parentId ?? null,
                        );
                        return;
                    }
                    const node: ApiCollectionNode = {
                        id: newId(),
                        type: "Folder",
                        name,
                        // Collapsed by default (unit 4.2) — an ever-expanding tree of
                        // "expanded forever" folders was the reported clutter bug.
                        isExpanded: false,
                        children: [],
                        defaultAuth: null,
                        request: null,
                    };
                    updateCollectionsMutate((prev) =>
                        insertIntoCollection(
                            prev,
                            collectionId,
                            node,
                            parentId,
                        ),
                    );
                },
            });
        },
        [updateCollectionsMutate, linkedActions],
    );

    const handleDeleteNode = useCallback(
        (nodeId: string, collectionId: string) => {
            const info = describeNodeForDelete(
                collections,
                nodeId,
                collectionId,
            );
            // Shared post-delete bookkeeping for both backends: drop the
            // selection, close the request's tab the same way a manual tab close
            // would (unit 4.1), and unselect a deleted collection.
            const cleanupAfterDelete = () => {
                if (selectedNodeIdRef.current !== nodeId) return;
                setSelectedNodeId(null);
                const tabToClose = tabsRef.current.find(
                    (t) => t.nodeId === nodeId,
                );
                if (tabToClose) {
                    setTabs((prev) =>
                        prev.filter((t) => t.id !== tabToClose.id),
                    );
                    setTabStates((prev) => {
                        const next = { ...prev };
                        delete next[tabToClose.id];
                        return next;
                    });
                    if (activeTabIdRef.current === tabToClose.id)
                        setActiveTabId(
                            pickNeighborTabId(tabsRef.current, tabToClose.id),
                        );
                }
                setSelectedCollectionId(
                    collectionId === nodeId ? null : collectionId,
                );
            };
            setConfirmDialog({
                message: formatDeleteMessage(info),
                confirmText: "Delete",
                onConfirm: () => {
                    setConfirmDialog(null);
                    const collection = collectionsRef.current.find(
                        (c) => c.id === collectionId,
                    );
                    const rootId = collection?.origin?.rootId;
                    if (rootId && collection) {
                        void (async () => {
                            let ok: boolean;
                            if (nodeId === collectionId) {
                                ok = !!(await linkedActions.deleteCollection(
                                    rootId,
                                    collectionId,
                                ));
                            } else {
                                const node = findRequestNode(
                                    collection.nodes,
                                    nodeId,
                                );
                                ok =
                                    node?.type === "Folder"
                                        ? !!(await linkedActions.deleteFolder(
                                              rootId,
                                              collectionId,
                                              nodeId,
                                          ))
                                        : !!(await linkedActions.deleteRequest(
                                              rootId,
                                              collectionId,
                                              nodeId,
                                          ));
                            }
                            if (ok) cleanupAfterDelete();
                        })();
                        return;
                    }
                    updateCollectionsMutate(
                        (prev) => removeNode(prev, nodeId),
                        { onSuccess: cleanupAfterDelete },
                    );
                },
            });
        },
        [collections, updateCollectionsMutate, linkedActions],
    );

    /**
     * Linked request rename: the request's name *is* its file name on a linked
     * root, and the save payload carries no name field, so rename = write the
     * request to a new file name + delete the old file. `id` must be cleared —
     * the create endpoint resolves an existing file by `Request.Id` and would
     * overwrite the file we're about to delete instead of writing a new one.
     * The recreated file gets a fresh node id (stable ids derive from the file
     * path), which is why the open tab and selection remap onto
     * `created.requestId`.
     */
    const renameLinkedRequest = useCallback(
        async (
            rootId: string,
            collection: ApiCollection,
            node: ApiCollectionNode,
            newName: string,
        ) => {
            const parentId = findParentFolderId(collection.nodes, node.id);
            const created = await linkedActions.createRequest(
                rootId,
                collection.id,
                {
                    name: newName,
                    parentFolderId: parentId,
                    request: { ...node.request!, id: "", name: newName },
                },
            );
            if (!created) return;
            await linkedActions.deleteRequest(rootId, collection.id, node.id);
            setTabs((prev) =>
                prev.map((t) =>
                    t.nodeId === node.id
                        ? { ...t, nodeId: created.requestId, name: newName }
                        : t,
                ),
            );
            if (selectedNodeIdRef.current === node.id)
                setSelectedNodeId(created.requestId);
        },
        [linkedActions],
    );

    const handleRenameNode = useCallback(
        (_nodeId: string, _collectionId: string, newName: string) => {
            const collection = collectionsRef.current.find(
                (c) => c.id === _collectionId,
            );
            const rootId = collection?.origin?.rootId;
            if (rootId && collection) {
                if (_nodeId === _collectionId) {
                    void linkedActions.renameCollection(
                        rootId,
                        _collectionId,
                        newName,
                    );
                    return;
                }
                const node = findRequestNode(collection.nodes, _nodeId);
                if (node?.type === "Folder") {
                    void linkedActions.renameFolder(
                        rootId,
                        _collectionId,
                        _nodeId,
                        newName,
                    );
                    return;
                }
                if (node?.type === "Request" && node.request) {
                    void renameLinkedRequest(rootId, collection, node, newName);
                }
                return;
            }
            updateCollectionsMutate((prev) =>
                renameNodeInCollections(prev, _nodeId, newName),
            );
            // Update tab name if open
            setTabs((prev) =>
                prev.map((t) =>
                    t.nodeId === _nodeId ? { ...t, name: newName } : t,
                ),
            );
        },
        [updateCollectionsMutate, linkedActions, renameLinkedRequest],
    );

    /**
     * Linked move: the move endpoint relocates a file/folder to a parent
     * (appending last), and the order endpoint writes the sibling ordering
     * manifest — a before/after drop needs both, a plain "inside" drop needs
     * only the move. Moves are confined to the dragged node's own collection.
     */
    const handleLinkedMoveNode = useCallback(
        async (
            nodeId: string,
            source: ApiCollection,
            target: MoveNodeTarget,
        ) => {
            const rootId = source.origin?.rootId;
            if (!rootId || nodeId === target.targetNodeId) return;
            if (target.targetCollectionId !== source.id) {
                notify(
                    "info",
                    "Can't move across collections",
                    "Linked items stay inside their own collection.",
                );
                return;
            }
            const sourceParent = findParentFolderId(source.nodes, nodeId);
            const targetParent =
                target.placement === "inside"
                    ? (target.targetNodeId ?? null)
                    : target.targetNodeId
                      ? findParentFolderId(source.nodes, target.targetNodeId)
                      : null;
            const sameParent = sourceParent === targetParent;

            // Dropping a node "inside" the folder it already sits in changes nothing.
            if (sameParent && target.placement === "inside") return;

            let root = linkedRootsRef.current.find((r) => r.id === rootId);
            if (!sameParent) {
                const moved = await linkedActions.moveNode(
                    rootId,
                    source.id,
                    nodeId,
                    targetParent,
                );
                if (!moved) return;
                root = moved;
            }
            if (target.placement === "inside") {
                // The move endpoint appends last inside the folder — that is the
                // whole "inside" semantics, no order write needed.
                notify("success", "Moved", "Item moved.");
                return;
            }

            // before/after needs the explicit sibling order — recompute it from
            // the refreshed root so a cross-parent move sees its new siblings.
            const refreshed = root?.collections.find((c) => c.id === source.id);
            if (!refreshed) return;
            const siblings = childIdsOf(refreshed, targetParent);
            let movedId = nodeId;
            if (!sameParent) {
                // A cross-parent move gives the node a new file and therefore a
                // new id — find it as the sibling that was not there before.
                const previous = new Set(childIdsOf(source, targetParent));
                movedId = siblings.find((id) => !previous.has(id)) ?? nodeId;
            }
            const without = siblings.filter((id) => id !== movedId);
            let index = without.length;
            if (target.targetNodeId) {
                const ti = without.indexOf(target.targetNodeId);
                if (ti !== -1)
                    index = ti + (target.placement === "before" ? 0 : 1);
            } else if (target.placement === "before") {
                index = 0;
            }
            const ordered = [
                ...without.slice(0, index),
                movedId,
                ...without.slice(index),
            ];
            const res = await linkedActions.setChildOrder(
                rootId,
                source.id,
                targetParent,
                ordered,
            );
            if (res) notify("success", "Moved", "Item moved.");
        },
        [linkedActions, notify],
    );

    const handleMoveNode = useCallback(
        (
            nodeId: string,
            sourceCollectionId: string,
            target: MoveNodeTarget,
        ) => {
            const source = collectionsRef.current.find(
                (c) => c.id === sourceCollectionId,
            );
            if (source?.origin?.kind === "linked") {
                void handleLinkedMoveNode(nodeId, source, target);
                return;
            }
            // Defensive counterpart to the tree's drop rules — an internal node
            // must never land inside a linked collection.
            const targetCol = collectionsRef.current.find(
                (c) => c.id === target.targetCollectionId,
            );
            if (targetCol?.origin?.kind === "linked") {
                notify(
                    "info",
                    "Can't move into a linked collection",
                    "Move items inside the same linked collection instead.",
                );
                return;
            }
            // No snapshot-based no-op guard here on purpose: a node created moments ago
            // may not be in this render's `collections` yet, `moveNode` returns its input
            // unchanged when it cannot find the source, and bailing on that swallowed the
            // move entirely. `moveNode` runs against the freshest store inside the updater
            // instead, where an impossible move is already a safe no-op.
            if (target.targetCollectionId !== sourceCollectionId) {
                setTabs((prev) =>
                    prev.map((t) =>
                        t.nodeId === nodeId
                            ? { ...t, collectionId: target.targetCollectionId }
                            : t,
                    ),
                );
                if (selectedNodeId === nodeId) {
                    setSelectedCollectionId(target.targetCollectionId);
                }
            }
            updateCollectionsMutate((prev) => moveNode(prev, nodeId, target), {
                onSuccess: () => notify("success", "Moved", "Request moved."),
                onError: (err) =>
                    notify("error", "Move failed", describeApiError(err)),
            });
        },
        [selectedNodeId, updateCollectionsMutate, notify, handleLinkedMoveNode],
    );

    const handleMoveCollection = useCallback(
        (collectionId: string, target: MoveCollectionTarget) => {
            // Collection-root reorder is internal-store-only — linked
            // collections live in their root's directory ordering, not
            // collections.json.
            if (
                collectionsRef.current.find((c) => c.id === collectionId)
                    ?.origin?.kind === "linked"
            )
                return;
            updateCollectionsMutate(
                (prev) => moveCollection(prev, collectionId, target),
                {
                    onSuccess: () =>
                        notify("success", "Moved", "Collection moved."),
                    onError: (err) =>
                        notify("error", "Move failed", describeApiError(err)),
                },
            );
        },
        [updateCollectionsMutate, notify],
    );

    /** cURL import target — inserts the parsed request (creating the collection
     *  and/or folder path when the ref doesn't resolve), then opens it in a
     *  permanent tab. */
    const handleImportCurlRequest = useCallback(
        async (
            request: HttpRequestEntry,
            collectionIdOrName: string,
            folderPath: string | null,
        ) => {
            // A linked target imports through the root endpoints — folder-path
            // segments materialize as directories by name, then the request file
            // is created at the end of the path.
            const match = collectionsRef.current.find(
                (c) =>
                    c.id === collectionIdOrName ||
                    c.name.toLowerCase() === collectionIdOrName.toLowerCase(),
            );
            if (match?.origin?.kind === "linked" && match.origin.rootId) {
                const rootId = match.origin.rootId;
                const collectionId = match.id;
                const segments = (folderPath ?? "")
                    .split("/")
                    .map((s) => s.trim())
                    .filter(Boolean);
                let parentId: string | null = null;
                for (const seg of segments) {
                    let col = linkedRootsRef.current
                        .find((r) => r.id === rootId)
                        ?.collections.find((c) => c.id === collectionId);
                    let folder: ApiCollectionNode | null = col
                        ? findFolderByName(col, parentId, seg)
                        : null;
                    if (!folder) {
                        const refreshed = await linkedActions.createFolder(
                            rootId,
                            collectionId,
                            seg,
                            parentId,
                        );
                        col = refreshed?.collections.find(
                            (c) => c.id === collectionId,
                        );
                        folder = col
                            ? findFolderByName(col, parentId, seg)
                            : null;
                        if (!folder) return;
                    }
                    parentId = folder.id;
                }
                const res = await linkedActions.createRequest(
                    rootId,
                    collectionId,
                    {
                        name: request.name,
                        parentFolderId: parentId,
                        request,
                    },
                );
                if (!res) return;
                setSelectedNodeId(res.requestId);
                setSelectedCollectionId(collectionId);
                const tabId = newId();
                setTabs((prev) => [
                    ...prev,
                    {
                        id: tabId,
                        nodeId: res.requestId,
                        collectionId,
                        name: request.name,
                        method: request.method,
                        dirty: false,
                    },
                ]);
                setTabStates((prev) => ({
                    ...prev,
                    [tabId]: emptyTabState(deepClone(request)),
                }));
                setActiveTabId(tabId);
                return;
            }
            const node: ApiCollectionNode = {
                id: request.id || newId(),
                type: "Request",
                name: request.name,
                isExpanded: true,
                children: [],
                defaultAuth: null,
                request,
            };
            let resolvedCollectionId = "";
            await updateCollectionsMutateAsync((prev) => {
                const result = insertRequestAtFolderPath(
                    prev,
                    collectionIdOrName,
                    folderPath,
                    node,
                );
                resolvedCollectionId = result.collectionId;
                return result.collections;
            });
            setSelectedNodeId(node.id);
            setSelectedCollectionId(resolvedCollectionId);
            const tabId = newId();
            setTabs((prev) => [
                ...prev,
                {
                    id: tabId,
                    nodeId: node.id,
                    collectionId: resolvedCollectionId,
                    name: node.name,
                    method: request.method,
                    dirty: false,
                },
            ]);
            setTabStates((prev) => ({
                ...prev,
                [tabId]: emptyTabState(deepClone(request)),
            }));
            setActiveTabId(tabId);
        },
        [updateCollectionsMutateAsync, linkedActions],
    );

    /**
     * Persist a linked tab through its root's save endpoint, sending the file's
     * last-known content stamp. A 409 returns the fresh stamp in `conflict.linked`
     * so Overwrite can retry without re-reading the file.
     */
    const saveLinkedTab = useCallback(
        async (
            tab: RequestTab,
            draftForSave: HttpRequestEntry,
            collection: ApiCollection,
            overrideStamp?: string | null,
        ): Promise<boolean> => {
            const rootId = collection.origin?.rootId;
            if (!rootId) return false;
            const stamp =
                overrideStamp !== undefined
                    ? overrideStamp
                    : (requestFileState(
                          linkedRootsRef.current.find((r) => r.id === rootId),
                          tab.nodeId,
                      )?.contentStamp ?? null);
            const outcome = await linkedActions.saveRequest(
                rootId,
                collection.id,
                tab.nodeId,
                draftForSave,
                stamp,
            );
            if (outcome.kind === "saved") {
                setConflict(null);
                setTabStates((prev) => ({
                    ...prev,
                    [tab.id]: { ...prev[tab.id], dirty: false },
                }));
                setTabs((prev) =>
                    prev.map((t) =>
                        t.id === tab.id ? { ...t, dirty: false } : t,
                    ),
                );
                return true;
            }
            if (outcome.kind === "conflict") {
                setConflict({
                    message: `The request file${outcome.conflict.requestFilePath ? ` "${outcome.conflict.requestFilePath}"` : ""} changed on disk. Reload the latest version or overwrite it with your changes.`,
                    linked: {
                        rootId,
                        collectionId: collection.id,
                        requestId: tab.nodeId,
                        currentContentStamp:
                            outcome.conflict.currentContentStamp,
                        requestFilePath: outcome.conflict.requestFilePath,
                    },
                });
            }
            return false;
        },
        [linkedActions],
    );

    const saveActiveTab = useCallback(
        async (baseCollections?: ApiCollection[]): Promise<boolean> => {
            const activeTabId = activeTabIdRef.current;
            if (!activeTabId) return false;
            const tabState = tabStatesRef.current[activeTabId];
            if (!tabState) return false;
            const tab = tabsRef.current.find((t) => t.id === activeTabId);
            if (!tab) return false;
            // The transient credentialSecret must never be written to collections.json.
            const draftForSave = deepClone(tabState.draft);
            if (draftForSave.auth) {
                draftForSave.auth = {
                    ...draftForSave.auth,
                    credentialSecret: null,
                };
            }
            const tabCollection = collectionsRef.current.find(
                (c) => c.id === tab.collectionId,
            );
            if (tabCollection?.origin?.kind === "linked") {
                return saveLinkedTab(tab, draftForSave, tabCollection);
            }
            try {
                // An explicit base is an overwrite-after-conflict, which must send exactly
                // what the caller resolved; otherwise derive from the freshest store.
                await updateCollectionsMutateAsync(
                    baseCollections
                        ? updateRequestInCollections(
                              baseCollections,
                              tab.nodeId,
                              draftForSave,
                          )
                        : (prev) =>
                              updateRequestInCollections(
                                  prev,
                                  tab.nodeId,
                                  draftForSave,
                              ),
                );
                setConflict(null);
                setTabStates((prev) => ({
                    ...prev,
                    [activeTabId]: { ...prev[activeTabId], dirty: false },
                }));
                setTabs((prev) =>
                    prev.map((t) =>
                        t.id === activeTabId ? { ...t, dirty: false } : t,
                    ),
                );
                return true;
            } catch (err) {
                const message =
                    err instanceof Error ? err.message : String(err);
                if (
                    message.includes("409") ||
                    message.toLowerCase().includes("conflict")
                ) {
                    setConflict({
                        message:
                            "The collections file changed on disk. Reload the latest version, overwrite with your changes, or save your request as a copy.",
                    });
                } else {
                    console.error("Failed to save collections", err);
                }
                return false;
            }
        },
        [updateCollectionsMutateAsync, saveLinkedTab],
    );

    const handleSave = useCallback(
        async () => saveActiveTab(),
        [saveActiveTab],
    );

    const handleSend = useCallback(async () => {
        const activeTabId = activeTabIdRef.current;
        if (!activeTabId) return;
        const tabState = tabStatesRef.current[activeTabId];
        if (!tabState) return;
        const tab = tabsRef.current.find((t) => t.id === activeTabId);
        if (!tab) return;
        const saved = await handleSave();
        if (!saved) return;
        setTabStates((prev) => ({
            ...prev,
            [activeTabId]: {
                ...prev[activeTabId],
                sending: true,
                response: null,
            },
        }));
        let request: HttpRequestEntry | undefined;
        try {
            // Resolve the secret from the persisted store if the editor has not already loaded it.
            request = deepClone(tabState.draft);
            if (request.auth?.credentialKey && !request.auth.credentialSecret) {
                const secret = await getSecret(request.auth.credentialKey);
                if (secret) {
                    request.auth = {
                        ...request.auth,
                        credentialSecret: secret,
                    };
                }
            }

            await runRequestActions(
                request.preRequestActions ?? [],
                { request },
                (type, title, message) => notify(type, title, message),
            );

            const layers = resolveEnvironmentLayers(tab.collectionId);
            const result = await executeRequestMutateAsync({
                request,
                collectionId: tab.collectionId ?? undefined,
                // Both layers travel to the backend, which applies the same precedence.
                globalEnvironmentId: layers.global?.id ?? undefined,
                environmentId: layers.scoped?.id ?? undefined,
            });

            // Commit the response immediately so the UI is not blocked by post-request actions (e.g. Delay).
            setTabStates((prev) => ({
                ...prev,
                [activeTabId]: {
                    ...prev[activeTabId],
                    response: result,
                    sending: false,
                    history: appendHistory(prev[activeTabId], result),
                },
            }));

            try {
                await runRequestActions(
                    request.postRequestActions ?? [],
                    { request, response: result },
                    (type, title, message) => notify(type, title, message),
                );
            } catch (postErr) {
                const message =
                    postErr instanceof Error
                        ? postErr.message
                        : "Unknown error";
                notify("error", "Post-request action failed", message);
            }
        } catch (err) {
            const failure: ApiClientExecutionResponse = {
                resolvedUrl: tabState.draft.url,
                method: tabState.draft.method,
                statusCode: 0,
                statusText: "Request Failed",
                errorMessage:
                    err instanceof Error ? err.message : "Unknown error",
                elapsedMs: 0,
                contentLength: -1,
                contentType: null,
                responseBody: null,
                responseBodyTruncated: false,
                headers: [],
                captureWarnings: [],
                graphQlErrors: null,
            };

            await runRequestActions(
                request?.postRequestActions ??
                    tabState.draft.postRequestActions ??
                    [],
                { request: request ?? tabState.draft, response: failure },
                (type, title, message) => notify(type, title, message),
            );

            setTabStates((prev) => ({
                ...prev,
                [activeTabId]: {
                    ...prev[activeTabId],
                    sending: false,
                    response: failure,
                    history: appendHistory(prev[activeTabId], failure),
                },
            }));
        }
    }, [
        handleSave,
        executeRequestMutateAsync,
        notify,
        resolveEnvironmentLayers,
    ]);

    // ── Request runs ────────────────────────────────────────────────────────

    /** Mirror a step's response into the active tab's viewer while the run
     *  streams — the response pane tracks the run live, not just at the end. */
    const pushRunResponseToActiveTab = useCallback(
        (response: ApiClientExecutionResponse | null | undefined) => {
            if (!response) return;
            const tabId = activeTabIdRef.current;
            if (!tabId) return;
            setTabStates((prev) =>
                prev[tabId]
                    ? { ...prev, [tabId]: { ...prev[tabId], response } }
                    : prev,
            );
        },
        [],
    );

    /** The SSE callbacks every run entry point shares: live response mirroring,
     *  a history entry for the final step, and a done/aborted notification.
     *  `extra` runs after the shared finish work (e.g. clearing `sending`). */
    const runCallbacks = useCallback(
        (extra?: (s: ApiRunState) => void): ApiRunCallbacks => ({
            onEvent: (e) => {
                if (
                    (e.type === "stepCompleted" || e.type === "stepFailed") &&
                    e.response
                ) {
                    pushRunResponseToActiveTab(e.response);
                }
            },
            onFinished: (s) => {
                const last = lastResponseOf(s.steps);
                if (last) {
                    const tabId = activeTabIdRef.current;
                    if (tabId)
                        setTabStates((prev) =>
                            prev[tabId]
                                ? {
                                      ...prev,
                                      [tabId]: {
                                          ...prev[tabId],
                                          response: last,
                                          history: appendHistory(
                                              prev[tabId],
                                              last,
                                          ),
                                      },
                                  }
                                : prev,
                        );
                }
                if (s.status === "done") {
                    const completed = s.summary?.completedSteps ?? 0;
                    const failed = s.summary?.failedSteps ?? 0;
                    notify(
                        failed > 0 ? "info" : "success",
                        "Run finished",
                        `${completed} step${completed === 1 ? "" : "s"} completed${failed > 0 ? `, ${failed} failed` : ""}.`,
                    );
                } else if (s.status === "aborted") {
                    notify(
                        "info",
                        "Run aborted",
                        s.abortReason === "stopOnError"
                            ? "Stopped on the first failed step."
                            : "Cancelled.",
                    );
                }
                extra?.(s);
            },
        }),
        [pushRunResponseToActiveTab, notify],
    );

    /** Shared ApiRunRequest fields for a collection: env layers, linked root,
     *  current run options. Null (plus a notification) when the collection
     *  vanished between menu open and click. */
    const buildRunRequest = useCallback(
        (
            collectionId: string,
            extras: Pick<
                ApiRunRequest,
                "mode" | "requestId" | "nodeId" | "requestIds"
            >,
        ): ApiRunRequest | null => {
            const collection = collectionsRef.current.find(
                (c) => c.id === collectionId,
            );
            if (!collection) {
                notify(
                    "error",
                    "Couldn't start run",
                    "The collection no longer exists.",
                );
                return null;
            }
            const layers = resolveEnvironmentLayers(collectionId);
            return {
                collectionId,
                linkedRootId:
                    collection.origin?.kind === "linked"
                        ? collection.origin.rootId
                        : null,
                activeEnvironmentId: layers.scoped?.id ?? null,
                globalEnvironmentId: layers.global?.id ?? null,
                stopOnError: runOptionsRef.current.stopOnError,
                delayMs: runOptionsRef.current.delayMs,
                ...extras,
            };
        },
        [notify, resolveEnvironmentLayers],
    );

    const handleRunSubtree = useCallback(
        (collectionId: string, nodeId: string) => {
            const req = buildRunRequest(collectionId, {
                mode: "subtree",
                nodeId,
            });
            if (!req) return;
            setRunDrawerOpen(true);
            startRun(req, runCallbacks());
        },
        [buildRunRequest, startRun, runCallbacks],
    );

    const handleRunSelection = useCallback(
        (collectionId: string, requestIds: string[]) => {
            // The selection carries tree node ids; the run endpoint indexes
            // requests by entry id (node.request.id) — translate here so an
            // imported collection whose ids differ still resolves.
            const collection = collectionsRef.current.find(
                (c) => c.id === collectionId,
            );
            const entryIds = requestIds.map(
                (id) =>
                    (collection &&
                        findRequestNode(collection.nodes, id)?.request?.id) ||
                    id,
            );
            const req = buildRunRequest(collectionId, {
                mode: "explicit",
                requestIds: entryIds,
            });
            if (!req) return;
            setRunDrawerOpen(true);
            startRun(req, runCallbacks());
        },
        [buildRunRequest, startRun, runCallbacks],
    );

    /**
     * "Send with dependencies" — the split-button sibling of handleSend. Saves
     * the draft first (the chain resolves deps from the persisted request),
     * runs the target's pre-request actions, then streams the requestWithDeps
     * run. `sending` stays set for the whole run so plain Send stays disabled
     * while the chain executes; the final step's response lands where a plain
     * Send's would.
     */
    const handleSendWithDeps = useCallback(() => {
        const activeTabId = activeTabIdRef.current;
        if (!activeTabId) return;
        const tab = tabsRef.current.find((t) => t.id === activeTabId);
        if (!tab) return;
        void (async () => {
            const saved = await handleSave();
            if (!saved) return;
            const draft = tabStatesRef.current[activeTabId]?.draft;
            if (!draft) return;
            // The endpoint's plan index is keyed by request *entry* id —
            // node.id equals it for app-created data but imports may diverge.
            const collection = collectionsRef.current.find(
                (c) => c.id === tab.collectionId,
            );
            const requestId =
                (collection &&
                    findRequestNode(collection.nodes, tab.nodeId)?.request
                        ?.id) ||
                tab.nodeId;
            const req = buildRunRequest(tab.collectionId, {
                mode: "requestWithDeps",
                requestId,
            });
            if (!req) return;
            // Same secret resolution as handleSend — the run endpoint executes
            // server-side but the target's credential still travels resolved.
            const request = deepClone(draft);
            if (request.auth?.credentialKey && !request.auth.credentialSecret) {
                const secret = await getSecret(request.auth.credentialKey);
                if (secret)
                    request.auth = {
                        ...request.auth,
                        credentialSecret: secret,
                    };
            }
            setTabStates((prev) => ({
                ...prev,
                [activeTabId]: {
                    ...prev[activeTabId],
                    sending: true,
                    response: null,
                },
            }));
            await runRequestActions(
                request.preRequestActions ?? [],
                { request },
                (type, title, message) => notify(type, title, message),
            );
            setRunDrawerOpen(true);
            startRun(
                req,
                runCallbacks((s) => {
                    const last = lastResponseOf(s.steps);
                    setTabStates((prev) =>
                        prev[activeTabId]
                            ? {
                                  ...prev,
                                  [activeTabId]: {
                                      ...prev[activeTabId],
                                      sending: false,
                                  },
                              }
                            : prev,
                    );
                    if (last) {
                        void runRequestActions(
                            request.postRequestActions ?? [],
                            { request, response: last },
                            (type, title, message) =>
                                notify(type, title, message),
                        ).catch((err: unknown) =>
                            notify(
                                "error",
                                "Post-request action failed",
                                err instanceof Error
                                    ? err.message
                                    : "Unknown error",
                            ),
                        );
                    }
                }),
            );
        })();
    }, [handleSave, buildRunRequest, startRun, notify, runCallbacks]);

    /** Saves a scrubbed example onto the active request and persists it. */
    const handleSaveExample = useCallback(
        async (name: string, response: ApiClientExecutionResponse) => {
            const activeTabId = activeTabIdRef.current;
            if (!activeTabId) return;
            const tabState = tabStatesRef.current[activeTabId];
            const tab = tabsRef.current.find((t) => t.id === activeTabId);
            if (!tabState || !tab) return;

            const example = buildResponseExample(
                newId(),
                name,
                response,
                now(),
            );
            const draft: HttpRequestEntry = {
                ...tabState.draft,
                responseExamples: [...tabState.draft.responseExamples, example],
            };

            setTabStates((prev) => ({
                ...prev,
                [activeTabId]: { ...prev[activeTabId], draft },
            }));

            // Same rule as saveActiveTab: the transient credentialSecret must never reach
            // collections.json.
            const draftForSave = deepClone(draft);
            if (draftForSave.auth) {
                draftForSave.auth = {
                    ...draftForSave.auth,
                    credentialSecret: null,
                };
            }
            const tabCollection = collectionsRef.current.find(
                (c) => c.id === tab.collectionId,
            );
            if (tabCollection?.origin?.kind === "linked") {
                await saveLinkedTab(tab, draftForSave, tabCollection);
                return;
            }
            try {
                await updateCollectionsMutateAsync((prev) =>
                    updateRequestInCollections(prev, tab.nodeId, draftForSave),
                );
            } catch (err) {
                console.error("Failed to save response example", err);
            }
        },
        [updateCollectionsMutateAsync, saveLinkedTab],
    );

    const getLatestCollections = useCallback(
        () =>
            qc.getQueryData<CollectionsStoreResponse>(["collections"])
                ?.collections ?? collections,
        [qc, collections],
    );

    const handleReloadConflict = useCallback(async () => {
        const linkedConflict = conflict?.linked;
        setConflict(null);
        if (linkedConflict) {
            // Re-scan the root, then replace the active draft with whatever the
            // file now holds — the on-disk version wins. The refetch must land
            // before the draft swap: the editor auto-saves ~2s after any draft
            // change, and a save with the stale cached stamp would re-conflict.
            const root = await linkedActions.reloadRoot(linkedConflict.rootId);
            await qc.refetchQueries({ queryKey: ["collections"] });
            const activeTabId = activeTabIdRef.current;
            if (!root || !activeTabId) return;
            const col = root.collections.find(
                (c) => c.id === linkedConflict.collectionId,
            );
            const node = col
                ? findRequestNode(col.nodes, linkedConflict.requestId)
                : null;
            if (!node?.request) return;
            setTabStates((prev) => ({
                ...prev,
                [activeTabId]: {
                    ...prev[activeTabId],
                    draft: deepClone(node.request!),
                    dirty: false,
                },
            }));
            setTabs((prev) =>
                prev.map((t) =>
                    t.id === activeTabId
                        ? {
                              ...t,
                              name: node.request!.name,
                              method: node.request!.method,
                              dirty: false,
                          }
                        : t,
                ),
            );
            return;
        }
        await qc.refetchQueries({ queryKey: ["collections"] });
        const latest = getLatestCollections();
        const activeTabId = activeTabIdRef.current;
        if (!activeTabId) return;
        const tab = tabsRef.current.find((t) => t.id === activeTabId);
        if (!tab) return;
        for (const col of latest) {
            const node = findRequestNode(col.nodes, tab.nodeId);
            if (node?.type === "Request" && node.request) {
                setTabStates((prev) => ({
                    ...prev,
                    [activeTabId]: {
                        ...prev[activeTabId],
                        draft: deepClone(node.request!),
                        dirty: false,
                    },
                }));
                setTabs((prev) =>
                    prev.map((t) =>
                        t.id === activeTabId
                            ? {
                                  ...t,
                                  name: node.request!.name,
                                  method: node.request!.method,
                                  dirty: false,
                              }
                            : t,
                    ),
                );
                break;
            }
        }
    }, [qc, getLatestCollections, conflict, linkedActions]);

    const handleOverwriteConflict = useCallback(async () => {
        const linkedConflict = conflict?.linked;
        setConflict(null);
        if (linkedConflict) {
            // Retry the same save with the stamp the 409 handed back — the new
            // stamp matches the file that was edited on disk, so the write wins.
            const activeTabId = activeTabIdRef.current;
            const tab = activeTabId
                ? tabsRef.current.find((t) => t.id === activeTabId)
                : undefined;
            const tabState = activeTabId
                ? tabStatesRef.current[activeTabId]
                : undefined;
            const collection = collectionsRef.current.find(
                (c) => c.id === linkedConflict.collectionId,
            );
            if (!tab || !tabState || !collection) return;
            const draftForSave = deepClone(tabState.draft);
            if (draftForSave.auth) {
                draftForSave.auth = {
                    ...draftForSave.auth,
                    credentialSecret: null,
                };
            }
            await saveLinkedTab(
                tab,
                draftForSave,
                collection,
                linkedConflict.currentContentStamp,
            );
            return;
        }
        await qc.refetchQueries({ queryKey: ["collections"] });
        const latest = getLatestCollections();
        await saveActiveTab(latest);
    }, [qc, getLatestCollections, saveActiveTab, saveLinkedTab, conflict]);

    const handleSaveAsCopy = useCallback(async () => {
        const linkedConflict = conflict?.linked;
        setConflict(null);
        if (linkedConflict) {
            // The disk-edited file is left alone — the draft lands as a brand-new
            // `<name> (copy).swebreq.json` next to it.
            const activeTabId = activeTabIdRef.current;
            const tab = activeTabId
                ? tabsRef.current.find((t) => t.id === activeTabId)
                : undefined;
            const tabState = activeTabId
                ? tabStatesRef.current[activeTabId]
                : undefined;
            const collection = collectionsRef.current.find(
                (c) => c.id === linkedConflict.collectionId,
            );
            if (!tab || !tabState || !collection) return;
            const copy = deepClone(tabState.draft);
            copy.id = newId();
            copy.name = `${copy.name} (copy)`;
            copy.createdAt = now();
            copy.updatedAt = now();
            if (copy.auth) copy.auth = { ...copy.auth, credentialSecret: null };
            const parentId = findParentFolderId(
                collection.nodes,
                linkedConflict.requestId,
            );
            const res = await linkedActions.createRequest(
                linkedConflict.rootId,
                collection.id,
                { name: copy.name, parentFolderId: parentId, request: copy },
            );
            if (!res) return;
            const tabId = newId();
            setTabs((prev) => [
                ...prev,
                {
                    id: tabId,
                    nodeId: res.requestId,
                    collectionId: collection.id,
                    name: copy.name,
                    method: copy.method,
                    dirty: false,
                },
            ]);
            setTabStates((prev) => ({
                ...prev,
                [tabId]: emptyTabState(deepClone(copy)),
            }));
            setActiveTabId(tabId);
            setSelectedNodeId(res.requestId);
            setSelectedCollectionId(collection.id);
            return;
        }
        await qc.refetchQueries({ queryKey: ["collections"] });
        const latest = getLatestCollections();
        const activeTabId = activeTabIdRef.current;
        const tab = tabsRef.current.find((t) => t.id === activeTabId);
        const tabState = activeTabId ? tabStatesRef.current[activeTabId] : null;
        if (!tab || !tabState) return;
        const collection = latest.find((c) => c.id === tab.collectionId);
        if (!collection) return;
        const copy = deepClone(tabState.draft);
        copy.id = newId();
        copy.name = `${copy.name} (copy)`;
        copy.createdAt = now();
        copy.updatedAt = now();
        const node: ApiCollectionNode = {
            id: copy.id,
            type: "Request",
            name: copy.name,
            isExpanded: true,
            children: [],
            defaultAuth: null,
            request: copy,
        };
        try {
            await updateCollectionsMutateAsync((prev) =>
                insertIntoCollection(prev, collection.id, node),
            );
            const tabId = newId();
            setTabs((prev) => [
                ...prev,
                {
                    id: tabId,
                    nodeId: node.id,
                    collectionId: collection.id,
                    name: node.name,
                    method: copy.method,
                    dirty: false,
                },
            ]);
            setTabStates((prev) => ({
                ...prev,
                [tabId]: emptyTabState(deepClone(copy)),
            }));
            setActiveTabId(tabId);
            setSelectedNodeId(node.id);
            setSelectedCollectionId(collection.id);
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            if (
                message.includes("409") ||
                message.toLowerCase().includes("conflict")
            ) {
                setConflict({
                    message:
                        "The collections file changed on disk. Reload the latest version, overwrite with your changes, or save your request as a copy.",
                });
            } else {
                console.error("Failed to save as copy", err);
            }
        }
    }, [
        qc,
        getLatestCollections,
        updateCollectionsMutateAsync,
        conflict,
        linkedActions,
    ]);

    /**
     * Environment manager save. The manager hands back the *merged* env list, so
     * this splits it: envs that were (or became) linked route to their root's
     * environment endpoints; only the remaining internal envs go to the
     * internal PUT. Re-scoping an env between stores moves it (delete + create)
     * since an env's home is part of its identity.
     */
    const handleSaveEnvironments = useCallback(
        (envs: ApiEnvironment[], activeId: string | null) => {
            const prevById = new Map(
                environmentsRef.current.map((e) => [e.id, e]),
            );
            const nextIds = new Set(envs.map((e) => e.id));
            const linkedRootOfCollection = (collectionId: string | null) =>
                collectionId
                    ? linkedRootsRef.current.find((r) =>
                          r.collections.some((c) => c.id === collectionId),
                      )
                    : undefined;
            const internalNext: ApiEnvironment[] = [];
            const linkedOps: (() => Promise<unknown>)[] = [];

            for (const e of envs) {
                const prev = prevById.get(e.id);
                const stripped = stripEnvironmentOrigin(e);
                const scopeRoot = linkedRootOfCollection(e.collectionId);
                const prevRootId =
                    prev?.origin?.kind === "linked"
                        ? prev.origin.rootId
                        : undefined;

                if (prevRootId) {
                    if (e.collectionId === prev?.collectionId) {
                        // Same scope → write the .swebenv.json back in place.
                        linkedOps.push(() =>
                            linkedActions.updateEnvironment(
                                prevRootId,
                                e.id,
                                stripped,
                            ),
                        );
                    } else if (scopeRoot) {
                        // Re-scoped onto another linked collection → recreate the
                        // file under that root.
                        linkedOps.push(async () => {
                            await linkedActions.deleteEnvironment(
                                prevRootId,
                                e.id,
                            );
                            await linkedActions.createEnvironment(
                                scopeRoot.id,
                                stripped,
                                e.collectionId,
                            );
                        });
                    } else if (e.collectionId === null) {
                        // Moved to global scope — keep it linked as a root-level
                        // file in the same root.
                        linkedOps.push(async () => {
                            await linkedActions.deleteEnvironment(
                                prevRootId,
                                e.id,
                            );
                            await linkedActions.createEnvironment(
                                prevRootId,
                                stripped,
                            );
                        });
                    } else {
                        // Re-scoped onto an internal collection → migrate it into
                        // the internal store.
                        linkedOps.push(() =>
                            linkedActions.deleteEnvironment(prevRootId, e.id),
                        );
                        internalNext.push(stripped);
                    }
                    continue;
                }

                if (scopeRoot) {
                    // A previously-internal (or new) env scoped onto a linked
                    // collection moves out of the internal store.
                    linkedOps.push(() =>
                        linkedActions.createEnvironment(
                            scopeRoot.id,
                            stripped,
                            e.collectionId,
                        ),
                    );
                    continue;
                }

                internalNext.push(stripped);
            }

            // Linked envs deleted in the manager.
            for (const p of environmentsRef.current) {
                if (
                    p.origin?.kind === "linked" &&
                    p.origin.rootId &&
                    !nextIds.has(p.id)
                ) {
                    const rootId = p.origin.rootId;
                    linkedOps.push(() =>
                        linkedActions.deleteEnvironment(rootId, p.id),
                    );
                }
            }

            updateEnvironmentsMutate({
                schemaVersion: 1,
                environments: internalNext,
                uiState: {
                    activeEnvironmentId: activeId,
                    activeEnvironmentIdByCollection:
                        uiState?.activeEnvironmentIdByCollection ?? {},
                    lastSelectedRequestIdByCollection:
                        uiState?.lastSelectedRequestIdByCollection ?? {},
                },
            });
            void (async () => {
                for (const op of linkedOps) await op();
            })();
        },
        [uiState, updateEnvironmentsMutate, linkedActions],
    );

    const handleSetActiveEnvironment = useCallback(
        (envId: string | null) => {
            updateEnvironmentsMutate({
                schemaVersion: 1,
                environments: internalEnvironments,
                uiState: {
                    activeEnvironmentId: envId,
                    activeEnvironmentIdByCollection:
                        uiState?.activeEnvironmentIdByCollection ?? {},
                    lastSelectedRequestIdByCollection:
                        uiState?.lastSelectedRequestIdByCollection ?? {},
                },
            });
        },
        [internalEnvironments, uiState, updateEnvironmentsMutate],
    );

    /// Sets the collection-scoped layer. Kept separate from the global slot so
    /// switching a project's target does not disturb the shared environment, which is
    /// the whole point of having two layers.
    const handleSetScopedEnvironment = useCallback(
        (collectionId: string, envId: string | null) => {
            const byCollection = {
                ...(uiState?.activeEnvironmentIdByCollection ?? {}),
            };
            if (envId === null) delete byCollection[collectionId];
            else byCollection[collectionId] = envId;

            updateEnvironmentsMutate({
                schemaVersion: 1,
                environments: internalEnvironments,
                uiState: {
                    // A pre-existing global selection that is really collection-scoped would
                    // keep overriding this one through the compatibility path, so clear it.
                    activeEnvironmentId:
                        activeEnvironmentId &&
                        environments.find((e) => e.id === activeEnvironmentId)
                            ?.collectionId === collectionId
                            ? null
                            : activeEnvironmentId,
                    activeEnvironmentIdByCollection: byCollection,
                    lastSelectedRequestIdByCollection:
                        uiState?.lastSelectedRequestIdByCollection ?? {},
                },
            });
        },
        [
            environments,
            internalEnvironments,
            uiState,
            activeEnvironmentId,
            updateEnvironmentsMutate,
        ],
    );

    const handleSaveCollectionVariables = useCallback(
        (variables: CollectionVariable[]) => {
            if (!selectedCollectionId) return;
            // The linked file model has no collection-variables slot — the
            // editor is disabled for linked collections, so a save should never
            // reach the internal PUT with linked metadata.
            if (
                collectionsRef.current.find(
                    (c) => c.id === selectedCollectionId,
                )?.origin?.kind === "linked"
            )
                return;
            updateCollectionsMutate(
                (prev) =>
                    prev.map((c) =>
                        c.id === selectedCollectionId ? { ...c, variables } : c,
                    ),
                {
                    // Without this a rejected save (a stale concurrency token, say) was
                    // swallowed, and the editor simply reopened empty — indistinguishable
                    // from never having typed the variable.
                    onError: (err) =>
                        notify(
                            "error",
                            "Saving collection variables failed",
                            err.message,
                        ),
                },
            );
        },
        [selectedCollectionId, updateCollectionsMutate, notify],
    );

    // ── Linked collection roots ────────────────────────────────────────────────

    /// `isTauri` in tauri-bridge is module-private; the same probe is duplicated
    /// in transport.ts/CollectionTree.tsx, so a local copy is the established pattern.
    const handleLinkFolder = useCallback(() => {
        void (async () => {
            const dir = await pickDirectory(
                "Select a folder to link as a collection root",
            );
            if (!dir) {
                if (
                    typeof window !== "undefined" &&
                    !("__TAURI_INTERNALS__" in window)
                ) {
                    notify(
                        "info",
                        "Linking needs the desktop app",
                        "Run SwebKit through Tauri to link a folder from disk.",
                    );
                }
                return;
            }
            await linkedActions.createRoot({ path: dir });
        })();
    }, [linkedActions, notify]);

    const handleReloadRoot = useCallback(
        (rootId: string) => {
            void (async () => {
                const root = await linkedActions.reloadRoot(rootId);
                if (root)
                    notify(
                        "success",
                        "Reloaded",
                        `${root.name} re-scanned from disk.`,
                    );
            })();
        },
        [linkedActions, notify],
    );

    const handleRemoveRoot = useCallback(
        (rootId: string) => {
            void (async () => {
                const name = linkedRootsRef.current.find(
                    (r) => r.id === rootId,
                )?.name;
                const ok = await linkedActions.removeRoot(rootId);
                if (ok)
                    notify(
                        "success",
                        "Unlinked",
                        `${name ?? "Collection root"} removed — its files stay on disk.`,
                    );
            })();
        },
        [linkedActions, notify],
    );

    const handleRevealPath = useCallback(
        (path: string) => {
            void (async () => {
                const revealed = await revealInExplorer(path);
                if (!revealed)
                    notify(
                        "info",
                        "Reveal unavailable",
                        "Revealing files needs the desktop app.",
                    );
            })();
        },
        [notify],
    );

    const handleOpenGit = useCallback((repo: GitInitialRepo) => {
        setGitInitialRepo(repo);
        setShowGitPanel(true);
    }, []);

    const selectedCollection = useMemo(
        () => collections.find((c) => c.id === selectedCollectionId) ?? null,
        [collections, selectedCollectionId],
    );

    const exportCollection = useMemo(
        () => collections.find((c) => c.id === exportCollectionId) ?? null,
        [collections, exportCollectionId],
    );

    const activeTab = activeTabId
        ? (tabs.find((t) => t.id === activeTabId) ?? null)
        : null;
    const activeCollection = activeTab
        ? collections.find((c) => c.id === activeTab.collectionId)
        : null;

    // The collection the environment pickers and the variable scope both work against.
    // Falls back to the tree selection because `activeCollection` needs an *open request
    // tab*: gating the project picker on it alone meant that before opening a request there
    // was no way to choose a collection-scoped environment at all.
    const currentCollection = activeCollection ?? selectedCollection;

    const { global: activeGlobalEnvironment, scoped: activeScopedEnvironment } =
        resolveEnvironmentLayers(currentCollection?.id);

    // Lowest priority first: the project layer overrides the global one, and the
    // global one fills in everything the project does not mention.
    const variableScope = buildVariableScope(
        currentCollection?.variables ?? [],
        [activeGlobalEnvironment, activeScopedEnvironment],
    );

    // The environment whose name the toolbar shows: the project one when there is
    // one, since that is the layer that wins.
    const activeEnvironment =
        activeScopedEnvironment ?? activeGlobalEnvironment;

    // Screen-state snapshot (agent-workspace-awareness M1) — the open request's method/URL/name.
    // Headers, auth config, and body are deliberately NOT serialized (D5: secrets live there);
    // the URL is sent without its query string/fragment since SAS signatures and api-key params
    // live there too.
    useScreenStateProvider(
        "api-client-page",
        "ApiClient",
        () => {
            const draft = activeTabId ? tabStates[activeTabId]?.draft : null;
            if (!draft)
                return {
                    collection: currentCollection?.name ?? null,
                    openRequest: null,
                };
            return {
                collection:
                    activeCollection?.name ?? currentCollection?.name ?? null,
                environment: activeEnvironment?.name ?? null,
                openRequest: {
                    id: draft.id,
                    name: draft.name,
                    method: draft.method,
                    url: draft.url.split("?")[0].split("#")[0],
                },
                responseStatus:
                    tabStates[activeTabId!]?.response?.statusCode ?? null,
                openTabCount: tabs.length,
            };
        },
        [
            activeTabId,
            tabStates,
            activeCollection,
            currentCollection,
            activeEnvironment,
            tabs,
        ],
    );

    const dismissConflict = useCallback(() => setConflict(null), []);

    const dismissLegacyNotice = useCallback(() => {
        localStorage.setItem("swokit-legacy-secret-notice", "dismissed");
        setLegacyNoticeDismissed(true);
    }, []);

    // Tabs state churns per editor keystroke (updateTabDraft bumps `tabs` and
    // `tabStates`). It lives in its own context so the page, collection tree and
    // dialogs don't re-render on every keystroke — only the tab strip, request
    // editor and response viewer subscribe here.
    const tabsValue: ApiClientTabsContextValue = useMemo(
        () => ({
            tabs,
            activeTabId,
            setActiveTabId,
            tabStates,
            closeTab,
            closeOtherTabs,
            closeAllTabs,
            promoteTab,
            updateTabDraft,
            activeTab,
            activeCollection,
            handleSave,
            handleSend,
            handleSendWithDeps,
            handleSaveExample,
        }),
        [
            tabs,
            activeTabId,
            tabStates,
            closeTab,
            closeOtherTabs,
            closeAllTabs,
            promoteTab,
            updateTabDraft,
            activeTab,
            activeCollection,
            handleSave,
            handleSend,
            handleSendWithDeps,
            handleSaveExample,
        ],
    );

    const value: ApiClientPageContextValue = useMemo(
        () => ({
            collections,
            isLoading,

            environments,
            activeEnvironmentId,
            activeEnvironment,
            activeGlobalEnvironment,
            activeScopedEnvironment,
            currentCollection: currentCollection ?? null,
            activeEnvironmentIdByCollection:
                uiState?.activeEnvironmentIdByCollection ?? {},
            handleSetActiveEnvironment,
            handleSetScopedEnvironment,
            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleSaveEnvironments,

            selectedNodeId,
            selectedCollectionId,
            selectedCollection,
            // These handlers read the tab-state mirror refs at event time only — by
            // design (see the refs comment above); the rule flags their transitive
            // reachability from this render-built object.
            // eslint-disable-next-line react-hooks/refs
            handleSelectNode,

            variableScope,

            handleAddCollection,
            handleAddRequest,
            handleAddFolder,
            handleDeleteNode,
            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleRenameNode,
            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleMoveNode,
            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleMoveCollection,
            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleImportCurlRequest,

            conflict,
            dismissConflict,
            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleReloadConflict,
            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleOverwriteConflict,
            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleSaveAsCopy,

            legacySecretCount,
            legacyNoticeDismissed,
            dismissLegacyNotice,

            showEnvManager,
            setShowEnvManager,
            showColVarEditor,
            setShowColVarEditor,
            exportCollectionId,
            setExportCollectionId,
            exportCollection,
            showGitPanel,
            setShowGitPanel,
            gitInitialRepo,
            setGitInitialRepo,

            linkedRoots,
            handleLinkFolder,
            handleReloadRoot,
            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleRemoveRoot,
            handleRevealPath,
            handleOpenGit,

            nameDialog,
            setNameDialog,
            confirmDialog,
            setConfirmDialog,

            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleSaveCollectionVariables,

            runState,
            runDrawerOpen,
            setRunDrawerOpen,
            abortRun,
            runOptions,
            setRunOptions,
            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleRunSubtree,
            // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
            handleRunSelection,
        }),
        [
            collections,
            isLoading,
            environments,
            activeEnvironmentId,
            activeEnvironment,
            activeGlobalEnvironment,
            activeScopedEnvironment,
            currentCollection,
            uiState?.activeEnvironmentIdByCollection,
            handleSetActiveEnvironment,
            handleSetScopedEnvironment,
            handleSaveEnvironments,
            selectedNodeId,
            selectedCollectionId,
            selectedCollection,
            handleSelectNode,
            variableScope,
            handleAddCollection,
            handleAddRequest,
            handleAddFolder,
            handleDeleteNode,
            handleRenameNode,
            handleMoveNode,
            handleMoveCollection,
            handleImportCurlRequest,
            conflict,
            dismissConflict,
            handleReloadConflict,
            handleOverwriteConflict,
            handleSaveAsCopy,
            legacySecretCount,
            legacyNoticeDismissed,
            dismissLegacyNotice,
            showEnvManager,
            showColVarEditor,
            exportCollectionId,
            exportCollection,
            showGitPanel,
            gitInitialRepo,
            linkedRoots,
            handleLinkFolder,
            handleReloadRoot,
            handleRemoveRoot,
            handleRevealPath,
            handleOpenGit,
            nameDialog,
            confirmDialog,
            handleSaveCollectionVariables,
            runState,
            runDrawerOpen,
            abortRun,
            runOptions,
            handleRunSubtree,
            handleRunSelection,
        ],
    );

    return (
        <ApiClientPageContext.Provider value={value}>
            <ApiClientTabsContext.Provider value={tabsValue}>
                {children}
            </ApiClientTabsContext.Provider>
        </ApiClientPageContext.Provider>
    );
}
