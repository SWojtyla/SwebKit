import { describe, it, expect } from "vitest";
import type {
    ApiChainStep,
    ApiCollection,
    ApiCollectionNode,
    HttpRequestEntry,
} from "./types";
import {
    collectChainCandidates,
    filterChainCandidates,
    resolveChainSteps,
    hasChainStep,
    newChainStep,
    moveChainStep,
    removeChainStep,
    setChainStepEnabled,
    type ChainRequestCandidate,
} from "./api-chain-utils";

// ── Fixtures ─────────────────────────────────────────────────────────────────

function requestNode(
    id: string,
    name: string = id,
    entryId: string = id,
    method: string = "Get",
): ApiCollectionNode {
    return {
        id,
        type: "Request",
        name,
        isExpanded: true,
        children: [],
        defaultAuth: null,
        request: {
            id: entryId,
            name,
            method,
            url: "",
        } as HttpRequestEntry,
    };
}

function folderNode(
    id: string,
    children: ApiCollectionNode[],
    name: string = id,
): ApiCollectionNode {
    return {
        id,
        type: "Folder",
        name,
        isExpanded: true,
        children,
        defaultAuth: null,
        request: null,
    };
}

function collection(
    id: string,
    name: string,
    nodes: ApiCollectionNode[],
    origin?: ApiCollection["origin"],
): ApiCollection {
    return {
        id,
        name,
        nodes,
        variables: [],
        defaultAuth: null,
        createdAt: "",
        updatedAt: "",
        origin,
    };
}

function step(
    id: string,
    collectionId: string,
    requestId: string,
    overrides: Partial<ApiChainStep> = {},
): ApiChainStep {
    return {
        id,
        collectionId,
        linkedRootId: null,
        requestId,
        enabled: true,
        ...overrides,
    };
}

const collections = [
    collection("col-a", "Auth", [
        requestNode("n-login", "Login", "req-login", "Post"),
        folderNode("f-inner", [requestNode("n-refresh", "Refresh token")]),
    ]),
    collection(
        "col-b",
        "Billing",
        [requestNode("n-order", "Create order", "req-order")],
        { kind: "linked", rootId: "root-1" },
    ),
];

// ── collectChainCandidates / filterChainCandidates ───────────────────────────

describe("collectChainCandidates", () => {
    it("flattens requests across collections in tree order", () => {
        const candidates = collectChainCandidates(collections);
        expect(candidates.map((c) => c.name)).toEqual([
            "Login",
            "Refresh token",
            "Create order",
        ]);
    });

    it("carries the request entry id and the linked root", () => {
        const candidates = collectChainCandidates(collections);
        const login = candidates[0];
        expect(login.requestId).toBe("req-login");
        expect(login.nodeId).toBe("n-login");
        expect(login.linkedRootId).toBeNull();
        const order = candidates[2];
        expect(order.collectionId).toBe("col-b");
        expect(order.linkedRootId).toBe("root-1");
        expect(order.originKind).toBe("linked");
    });
});

describe("filterChainCandidates", () => {
    it("matches name, collection and method case-insensitively", () => {
        const candidates = collectChainCandidates(collections);
        expect(filterChainCandidates(candidates, "login")).toHaveLength(1);
        expect(filterChainCandidates(candidates, "BILLING")).toHaveLength(1);
        expect(filterChainCandidates(candidates, "post")).toHaveLength(1);
        expect(filterChainCandidates(candidates, "")).toHaveLength(3);
        expect(filterChainCandidates(candidates, "nope")).toHaveLength(0);
    });
});

// ── resolveChainSteps — missing-ref honesty ──────────────────────────────────

describe("resolveChainSteps", () => {
    it("resolves each step to collection + request node", () => {
        const resolved = resolveChainSteps(
            [step("s1", "col-a", "req-login"), step("s2", "col-b", "req-order")],
            collections,
        );
        expect(resolved).toHaveLength(2);
        expect(resolved[0].missing).toBe(false);
        expect(resolved[0].node?.name).toBe("Login");
        expect(resolved[1].collection?.name).toBe("Billing");
    });

    it("resolves a step that carries the node id instead of the entry id", () => {
        const resolved = resolveChainSteps(
            [step("s1", "col-a", "n-login")],
            collections,
        );
        expect(resolved[0].missing).toBe(false);
        expect(resolved[0].node?.id).toBe("n-login");
    });

    it("marks steps missing when the collection or request is gone — never drops", () => {
        const resolved = resolveChainSteps(
            [
                step("s1", "col-gone", "req-login"),
                step("s2", "col-a", "req-gone"),
                step("s3", "col-a", "f-inner"), // folder, not a request
            ],
            collections,
        );
        expect(resolved).toHaveLength(3);
        expect(resolved.map((r) => r.missing)).toEqual([true, true, true]);
        expect(resolved[0].collection).toBeNull();
        expect(resolved[1].collection?.id).toBe("col-a");
        expect(resolved[1].node).toBeNull();
    });
});

// ── hasChainStep — add-to-chain dedupe ───────────────────────────────────────

describe("hasChainStep", () => {
    const steps = [step("s1", "col-a", "req-login")];

    it("dedupes on the request entry id", () => {
        const candidate: ChainRequestCandidate = {
            nodeId: "n-login",
            requestId: "req-login",
            name: "Login",
            method: "Post",
            collectionId: "col-a",
            collectionName: "Auth",
            linkedRootId: null,
            originKind: "internal",
        };
        expect(hasChainStep(steps, candidate)).toBe(true);
    });

    it("dedupes when the stored step carries the node id", () => {
        const withNodeId = [step("s1", "col-a", "n-login")];
        const candidate = {
            collectionId: "col-a",
            requestId: "req-login",
            nodeId: "n-login",
        };
        expect(hasChainStep(withNodeId, candidate)).toBe(true);
    });

    it("allows the same request id in a different collection", () => {
        const candidate = {
            collectionId: "col-b",
            requestId: "req-login",
            nodeId: "n-login",
        };
        expect(hasChainStep(steps, candidate)).toBe(false);
    });
});

describe("newChainStep", () => {
    it("builds an enabled step carrying the entry id and linked root", () => {
        const s = newChainStep({
            collectionId: "col-b",
            requestId: "req-order",
            linkedRootId: "root-1",
        });
        expect(s.id).toBeTruthy();
        expect(s.requestId).toBe("req-order");
        expect(s.linkedRootId).toBe("root-1");
        expect(s.enabled).toBe(true);
    });
});

// ── moveChainStep / remove / toggle ──────────────────────────────────────────

describe("moveChainStep", () => {
    const steps = [
        step("a", "c", "r1"),
        step("b", "c", "r2"),
        step("c", "c", "r3"),
        step("d", "c", "r4"),
    ];
    const ids = (ss: ApiChainStep[]) => ss.map((s) => s.id);

    it("moves forward and backward", () => {
        expect(ids(moveChainStep(steps, 0, 2))).toEqual(["b", "c", "a", "d"]);
        expect(ids(moveChainStep(steps, 3, 0))).toEqual(["d", "a", "b", "c"]);
        expect(ids(moveChainStep(steps, 1, 2))).toEqual(["a", "c", "b", "d"]);
    });

    it("returns the input unchanged for no-op or out-of-range moves", () => {
        expect(moveChainStep(steps, 1, 1)).toBe(steps);
        expect(moveChainStep(steps, -1, 0)).toBe(steps);
        expect(moveChainStep(steps, 0, 4)).toBe(steps);
        expect(moveChainStep(steps, 5, 0)).toBe(steps);
    });
});

describe("removeChainStep / setChainStepEnabled", () => {
    const steps = [step("a", "c", "r1"), step("b", "c", "r2")];

    it("removes by step id", () => {
        expect(removeChainStep(steps, "a").map((s) => s.id)).toEqual(["b"]);
    });

    it("toggles enabled without reordering", () => {
        const next = setChainStepEnabled(steps, "a", false);
        expect(next[0]).toMatchObject({ id: "a", enabled: false });
        expect(next[1]).toMatchObject({ id: "b", enabled: true });
    });
});
