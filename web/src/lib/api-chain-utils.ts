import type {
    ApiChainStep,
    ApiCollection,
    ApiCollectionNode,
} from "./types";
import { findRequestNodeAnyId, requestEntryId } from "./api-run-utils";

// ── Request chains — pure helpers ─────────────────────────────────────────────
// Editor state transitions (reorder, add/remove/toggle), step→request
// resolution against the loaded collections (missing refs are kept and marked,
// never silently dropped), and the "Add request" picker's candidate list. Kept
// pure so vitest covers them without mounting the page.

/** A request that can become a chain step — one per request node across all
 *  loaded collections (internal, linked-root, demo), in tree order. */
export interface ChainRequestCandidate {
    /** Tree node id — presentational identity inside the picker. */
    nodeId: string;
    /** The id the wire carries — the request *entry* id (`requestEntryId`). */
    requestId: string;
    name: string;
    method: string;
    collectionId: string;
    collectionName: string;
    /** Owning linked root id when the collection lives under one. */
    linkedRootId: string | null;
    /** `origin.kind` so the picker can label/demo-tag the owning collection. */
    originKind: "internal" | "linked" | "demo" | undefined;
}

function collectNodes(
    nodes: ApiCollectionNode[],
    out: ApiCollectionNode[],
): void {
    for (const n of nodes) {
        if (n.type === "Request" && n.request) out.push(n);
        else if (n.type === "Folder") collectNodes(n.children, out);
    }
}

/** Every request across `collections`, flattened in display order. */
export function collectChainCandidates(
    collections: ApiCollection[],
): ChainRequestCandidate[] {
    const out: ChainRequestCandidate[] = [];
    for (const collection of collections) {
        const nodes: ApiCollectionNode[] = [];
        collectNodes(collection.nodes, nodes);
        for (const node of nodes) {
            out.push({
                nodeId: node.id,
                requestId: requestEntryId(node),
                name: node.name,
                method: node.request!.method,
                collectionId: collection.id,
                collectionName: collection.name,
                linkedRootId:
                    collection.origin?.kind === "linked"
                        ? (collection.origin.rootId ?? null)
                        : null,
                originKind: collection.origin?.kind,
            });
        }
    }
    return out;
}

/** Candidates matching `query` by request name, method, url-free path… simple
 *  case-insensitive substring over name + collection name + method. */
export function filterChainCandidates(
    candidates: ChainRequestCandidate[],
    query: string,
): ChainRequestCandidate[] {
    const q = query.trim().toLowerCase();
    if (!q) return candidates;
    return candidates.filter(
        (c) =>
            c.name.toLowerCase().includes(q) ||
            c.collectionName.toLowerCase().includes(q) ||
            c.method.toLowerCase().includes(q),
    );
}

/** A chain step resolved against the loaded collections. `missing` is true when
 *  the collection or the request no longer exists — the editor shows these
 *  marked rather than dropping them, because the stored chain must round-trip
 *  verbatim. */
export interface ResolvedChainStep {
    step: ApiChainStep;
    collection: ApiCollection | null;
    /** The request node the step points at — both id spaces are tried. */
    node: ApiCollectionNode | null;
    missing: boolean;
}

/**
 * Resolves each step to its collection + request node. Tolerant of the two id
 * spaces (node id vs request entry id) — `requestId` on the wire is the entry
 * id, but hand-edited/imported chains may carry either.
 */
export function resolveChainSteps(
    steps: ApiChainStep[],
    collections: ApiCollection[],
): ResolvedChainStep[] {
    return steps.map((step) => {
        const collection =
            collections.find((c) => c.id === step.collectionId) ?? null;
        const node = collection
            ? findRequestNodeAnyId(collection.nodes, step.requestId)
            : null;
        return {
            step,
            collection,
            node: node?.type === "Request" ? node : null,
            missing: !collection || !node || node.type !== "Request",
        };
    });
}

/** True when a step for (collectionId, requestId) already exists — dedupe for
 *  "Add to chain". `requestId`/`nodeId` are both checked because the stored
 *  step may carry either id space. */
export function hasChainStep(
    steps: ApiChainStep[],
    candidate: Pick<ChainRequestCandidate, "collectionId" | "requestId" | "nodeId">,
): boolean {
    return steps.some(
        (s) =>
            s.collectionId === candidate.collectionId &&
            (s.requestId === candidate.requestId ||
                s.requestId === candidate.nodeId),
    );
}

/** The step to append for a picked request — fresh stable id, enabled. */
export function newChainStep(
    candidate: Pick<
        ChainRequestCandidate,
        "collectionId" | "requestId" | "linkedRootId"
    >,
): ApiChainStep {
    return {
        id: crypto.randomUUID(),
        collectionId: candidate.collectionId,
        linkedRootId: candidate.linkedRootId,
        requestId: candidate.requestId,
        enabled: true,
    };
}

/**
 * Moves the step at `from` to index `to` (post-removal index), returning a new
 * array. Out-of-range or no-op moves return the input unchanged so callers can
 * cheaply skip a save.
 */
export function moveChainStep(
    steps: ApiChainStep[],
    from: number,
    to: number,
): ApiChainStep[] {
    if (
        from === to ||
        from < 0 ||
        to < 0 ||
        from >= steps.length ||
        to >= steps.length
    ) {
        return steps;
    }
    const next = steps.slice();
    const [moved] = next.splice(from, 1);
    next.splice(to, 0, moved);
    return next;
}

/** Removes one step by id. */
export function removeChainStep(
    steps: ApiChainStep[],
    stepId: string,
): ApiChainStep[] {
    return steps.filter((s) => s.id !== stepId);
}

/** Toggles a step's enabled flag. Disabled steps stay in the chain (and are
 *  stored) but are skipped at plan time. */
export function setChainStepEnabled(
    steps: ApiChainStep[],
    stepId: string,
    enabled: boolean,
): ApiChainStep[] {
    return steps.map((s) => (s.id === stepId ? { ...s, enabled } : s));
}
