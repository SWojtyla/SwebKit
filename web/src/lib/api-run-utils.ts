import type {
    ApiClientExecutionResponse,
    ApiCollection,
    ApiCollectionNode,
    ApiRunAbortReason,
    ApiRunCapturedVariable,
    ApiRunEvent,
} from "./types";
import { findRequestNode } from "./collection-tree-utils";

// ── Request runs — pure helpers ───────────────────────────────────────────────
// Everything the run UI needs that is not React: the SSE event → state reducer,
// dependency-graph guards for the "Runs after" picker, tree-order resolution for
// subtree/explicit runs, and the ctrl+click multi-selection toggle. Keeping it
// pure is what lets vitest cover the logic without mounting the page.

/** User-tunable run options — the `stopOnError`/`delayMs` fields of ApiRunRequest. */
export interface ApiRunOptions {
    stopOnError: boolean;
    delayMs: number;
}

export const DEFAULT_RUN_OPTIONS: ApiRunOptions = {
    stopOnError: true,
    delayMs: 0,
};

// ── Run state machine ────────────────────────────────────────────────────────

export type ApiRunStepStatus = "pending" | "running" | "completed" | "failed";

export interface ApiRunStepState {
    index: number;
    requestId: string;
    name: string;
    status: ApiRunStepStatus;
    httpStatus: number | null;
    durationMs: number | null;
    error: string | null;
    captured: ApiRunCapturedVariable[];
    response: ApiClientExecutionResponse | null;
}

export interface ApiRunSummary {
    completedSteps: number;
    failedSteps: number;
    durationMs: number;
}

export type ApiRunStatus = "idle" | "running" | "done" | "aborted" | "error";

export interface ApiRunState {
    status: ApiRunStatus;
    runId: string | null;
    /** Ordered plan steps; empty while the `plan` event hasn't arrived yet. */
    steps: ApiRunStepState[];
    /** Set when `status === "error"` — plan rejected (400) or stream failed. */
    error: string | null;
    abortReason: ApiRunAbortReason | null;
    summary: ApiRunSummary | null;
}

export function initialApiRunState(): ApiRunState {
    return {
        status: "idle",
        runId: null,
        steps: [],
        error: null,
        abortReason: null,
        summary: null,
    };
}

/** A fresh state for a just-started run — running, no plan steps yet. */
export function startedApiRunState(): ApiRunState {
    return { ...initialApiRunState(), status: "running" };
}

function patchStep(
    steps: ApiRunStepState[],
    index: number,
    patch: Partial<ApiRunStepState>,
): ApiRunStepState[] {
    return steps.map((s) => (s.index === index ? { ...s, ...patch } : s));
}

/**
 * Folds one streamed {@link ApiRunEvent} into the run state. Tolerant of
 * unknown event types (forward compatibility) and of step events whose index
 * the plan never listed (ignored rather than crashing the drawer).
 */
export function reduceApiRunEvent(
    state: ApiRunState,
    event: ApiRunEvent,
): ApiRunState {
    switch (event.type) {
        case "plan":
            return {
                ...state,
                status: "running",
                runId: event.runId ?? state.runId,
                steps: (event.steps ?? []).map((s) => ({
                    index: s.index,
                    requestId: s.requestId,
                    name: s.name,
                    status: "pending" as const,
                    httpStatus: null,
                    durationMs: null,
                    error: null,
                    captured: [],
                    response: null,
                })),
            };
        case "stepStarted":
            if (!state.steps.some((s) => s.index === event.index)) return state;
            return {
                ...state,
                steps: patchStep(state.steps, event.index, {
                    status: "running",
                    name: event.name,
                    requestId: event.requestId,
                }),
            };
        case "stepCompleted":
            if (!state.steps.some((s) => s.index === event.index)) return state;
            return {
                ...state,
                steps: patchStep(state.steps, event.index, {
                    status: "completed",
                    httpStatus: event.status,
                    durationMs: event.durationMs,
                    captured: event.captured ?? [],
                    response: event.response ?? null,
                }),
            };
        case "stepFailed":
            if (!state.steps.some((s) => s.index === event.index)) return state;
            return {
                ...state,
                steps: patchStep(state.steps, event.index, {
                    status: "failed",
                    httpStatus: event.status ?? null,
                    durationMs: event.durationMs ?? null,
                    error: event.error ?? "Step failed",
                    response: event.response ?? null,
                }),
            };
        case "aborted":
            return {
                ...state,
                status: "aborted",
                abortReason: event.reason,
                // A step still marked "running" never resolves once the run is
                // aborted — surface it as cancelled rather than spinning forever.
                steps: state.steps.map((s) =>
                    s.status === "running"
                        ? {
                              ...s,
                              status: "failed" as const,
                              error: "Cancelled",
                          }
                        : s,
                ),
                summary: {
                    completedSteps: event.completedSteps,
                    failedSteps: state.steps.filter(
                        (s) => s.status === "failed" || s.status === "running",
                    ).length,
                    durationMs: state.summary?.durationMs ?? 0,
                },
            };
        case "done":
            return {
                ...state,
                status: "done",
                summary: {
                    completedSteps: event.completedSteps,
                    failedSteps: event.failedSteps,
                    durationMs: event.durationMs,
                },
            };
        default:
            return state;
    }
}

/** The last step that produced a response — what the response viewer mirrors. */
export function lastResponseOf(
    steps: ApiRunStepState[],
): ApiClientExecutionResponse | null {
    for (let i = steps.length - 1; i >= 0; i--) {
        if (steps[i].response) return steps[i].response;
    }
    return null;
}

/** Steps that actually completed or failed — the "3/5" progress numerator. */
export function finishedStepCount(steps: ApiRunStepState[]): number {
    return steps.filter(
        (s) => s.status === "completed" || s.status === "failed",
    ).length;
}

/**
 * The stream ended without a terminal `done`/`aborted` frame — synthesize one
 * from the steps we saw so the drawer never spins forever. Identity-preserving:
 * returns the input unchanged for any state that already reached a terminal
 * status (the caller uses the !== check to decide whether anything changed).
 */
export function finalizeRunState(state: ApiRunState): ApiRunState {
    if (state.status !== "running") return state;
    return {
        ...state,
        status: "done",
        summary: {
            completedSteps: state.steps.filter((s) => s.status === "completed")
                .length,
            failedSteps: state.steps.filter((s) => s.status === "failed")
                .length,
            durationMs: 0,
        },
    };
}

/**
 * Client-side abort — the fetch rejects before the server can emit its own
 * `aborted` frame, so the cancel is synthesized here: still-running steps are
 * marked failed/"Cancelled" and the run settles as `aborted`/`cancelled`.
 * Already-terminal states pass through unchanged.
 */
export function cancelRunState(state: ApiRunState): ApiRunState {
    if (state.status !== "running" && state.status !== "idle") return state;
    return {
        ...state,
        status: "aborted",
        abortReason: "cancelled",
        steps: state.steps.map((s) =>
            s.status === "running"
                ? { ...s, status: "failed" as const, error: "Cancelled" }
                : s,
        ),
    };
}

// ── Collection helpers (deps + run targets) ──────────────────────────────────

/** Every request node in a collection, flattened in tree (display) order. */
export function flattenRequestNodes(
    collection: ApiCollection,
): ApiCollectionNode[] {
    const out: ApiCollectionNode[] = [];
    const walk = (nodes: ApiCollectionNode[]) => {
        for (const n of nodes) {
            if (n.type === "Request") out.push(n);
            else walk(n.children);
        }
    };
    walk(collection.nodes);
    return out;
}

/**
 * Request node ids under `nodeId` in tree order — the `subtree` mode's client
 * mirror of the server's plan ordering. `nodeId` equal to the collection id
 * means the whole collection; a request node degenerates to just itself.
 */
export function collectSubtreeRequestIds(
    collection: ApiCollection,
    nodeId: string,
): string[] {
    if (nodeId === collection.id) {
        return flattenRequestNodes(collection).map((n) => n.id);
    }
    const node = findRequestNode(collection.nodes, nodeId);
    if (!node) return [];
    if (node.type === "Request") return [node.id];
    const out: string[] = [];
    const walk = (nodes: ApiCollectionNode[]) => {
        for (const n of nodes) {
            if (n.type === "Request") out.push(n.id);
            else walk(n.children);
        }
    };
    walk(node.children);
    return out;
}

/** `ids` re-ordered into collection tree order; unknown ids are dropped. */
export function orderIdsByTreeOrder(
    collection: ApiCollection,
    ids: Iterable<string>,
): string[] {
    const order = new Map(
        flattenRequestNodes(collection).map((n, i) => [n.id, i] as const),
    );
    return [...ids]
        .filter((id) => order.has(id))
        .sort((a, b) => order.get(a)! - order.get(b)!);
}

/**
 * Finds a request node by *either* id space: the tree node id or the request
 * entry id (`node.request.id`). Dependency ids and run plan targets live in
 * the entry-id space on the wire (`ApiClientRunService` indexes
 * `node.Request.Id`), while the tree always hands us node ids — created data
 * makes them equal, but imported/hand-edited collections may not.
 */
export function findRequestNodeAnyId(
    nodes: ApiCollectionNode[],
    id: string,
): ApiCollectionNode | null {
    for (const node of nodes) {
        if (node.id === id || node.request?.id === id) return node;
        const found = findRequestNodeAnyId(node.children, id);
        if (found) return found;
    }
    return null;
}

/** The id a run/dependency wire reference should carry — the request entry id. */
export function requestEntryId(node: ApiCollectionNode): string {
    return node.request?.id || node.id;
}

/**
 * The transitive closure of `startId`'s dependencies (`dependsOnRequestIds`
 * carry request *entry* ids, resolved tolerantly). Used to detect would-be
 * cycles — adding dep D to request R closes a loop exactly when R is already
 * reachable from D. The visited set holds both id spaces of every node seen
 * and doubles as protection against dependency cycles already in the data
 * (hand-edited collections.json / imports), which would otherwise recurse
 * forever.
 */
export function dependencyClosure(
    collection: ApiCollection,
    startId: string,
): Set<string> {
    const visited = new Set<string>();
    const stack = [startId];
    while (stack.length > 0) {
        const id = stack.pop()!;
        if (visited.has(id)) continue;
        visited.add(id);
        const node = findRequestNodeAnyId(collection.nodes, id);
        if (node?.type === "Request") {
            visited.add(node.id);
            if (node.request?.id) visited.add(node.request.id);
            for (const dep of node.request?.dependsOnRequestIds ?? []) {
                if (!visited.has(dep)) stack.push(dep);
            }
        }
    }
    return visited;
}

/**
 * Requests the "Add prerequisite" picker may offer for `nodeId`: same
 * collection, flattened in tree order, excluding the request itself, deps
 * already chosen, and any request whose own dependency closure already reaches
 * `nodeId` (choosing it would create a cycle the server rejects with
 * `dependency_cycle`). `nodeId`/`currentDeps` may mix node and entry ids.
 */
export function dependencyPickerCandidates(
    collection: ApiCollection,
    nodeId: string,
    currentDeps: string[],
): ApiCollectionNode[] {
    const excluded = new Set(currentDeps);
    return flattenRequestNodes(collection).filter((n) => {
        if (n.id === nodeId || n.request?.id === nodeId) return false;
        if (excluded.has(n.id) || (n.request && excluded.has(n.request.id)))
            return false;
        const closure = dependencyClosure(collection, n.id);
        return !closure.has(nodeId);
    });
}

// ── Tree multi-selection ─────────────────────────────────────────────────────

/** The ctrl/cmd+click multi-selection — confined to a single collection. */
export interface TreeMultiSelection {
    collectionId: string;
    /** Request/folder node ids, unordered. */
    ids: Set<string>;
}

/**
 * Toggles `nodeId` in the multi-selection. Selecting a node from a different
 * collection starts a fresh selection (runs are same-collection only); removing
 * the last id collapses back to `null` so no empty-selection state lingers.
 */
export function toggleMultiSelect(
    current: TreeMultiSelection | null,
    nodeId: string,
    collectionId: string,
): TreeMultiSelection | null {
    if (!current || current.collectionId !== collectionId) {
        return { collectionId, ids: new Set([nodeId]) };
    }
    const ids = new Set(current.ids);
    if (ids.has(nodeId)) ids.delete(nodeId);
    else ids.add(nodeId);
    return ids.size === 0 ? null : { collectionId, ids };
}

/**
 * The selected ids when every one of them resolves to a *request* node inside
 * `collection` — the only shape "Run selection" can execute — or null
 * otherwise (a folder or unknown id in the mix disables the menu item).
 */
export function runnableSelection(
    collection: ApiCollection,
    ids: Iterable<string>,
): string[] | null {
    const resolved: string[] = [];
    for (const id of ids) {
        const node = findRequestNode(collection.nodes, id);
        if (!node || node.type !== "Request") return null;
        resolved.push(id);
    }
    return resolved.length === 0 ? null : resolved;
}
