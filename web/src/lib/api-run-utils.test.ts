import { describe, it, expect } from "vitest";
import type {
    ApiCollection,
    ApiCollectionNode,
    ApiClientExecutionResponse,
    ApiRunEvent,
    HttpRequestEntry,
} from "./types";
import {
    initialApiRunState,
    startedApiRunState,
    reduceApiRunEvent,
    lastResponseOf,
    finishedStepCount,
    finalizeRunState,
    cancelRunState,
    flattenRequestNodes,
    collectSubtreeRequestIds,
    orderIdsByTreeOrder,
    dependencyClosure,
    dependencyPickerCandidates,
    findRequestNodeAnyId,
    requestEntryId,
    toggleMultiSelect,
    runnableSelection,
} from "./api-run-utils";

// ── Fixtures ─────────────────────────────────────────────────────────────────

function requestNode(
    id: string,
    deps: string[] = [],
    name: string = id,
): ApiCollectionNode {
    return {
        id,
        type: "Request",
        name,
        isExpanded: true,
        children: [],
        defaultAuth: null,
        request: {
            id,
            name,
            method: "Get",
            url: "",
            dependsOnRequestIds: deps,
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
    nodes: ApiCollectionNode[],
    id: string = "col",
): ApiCollection {
    return {
        id,
        name: id,
        nodes,
        variables: [],
        defaultAuth: null,
        createdAt: "",
        updatedAt: "",
    };
}

function fakeResponse(statusCode = 200): ApiClientExecutionResponse {
    return {
        resolvedUrl: "https://x",
        method: "Get",
        statusCode,
        statusText: "OK",
        errorMessage: null,
        elapsedMs: 12,
        contentLength: 2,
        contentType: "text/plain",
        responseBody: "ok",
        responseBodyTruncated: false,
        headers: [],
        captureWarnings: [],
        graphQlErrors: null,
    };
}

const plan: ApiRunEvent = {
    type: "plan",
    runId: "r1",
    steps: [
        { index: 0, requestId: "a", name: "A" },
        { index: 1, requestId: "b", name: "B" },
    ],
};

// ── Run reducer ──────────────────────────────────────────────────────────────

describe("reduceApiRunEvent", () => {
    it("plan seeds ordered pending steps and the run id", () => {
        const s = reduceApiRunEvent(startedApiRunState(), plan);
        expect(s.status).toBe("running");
        expect(s.runId).toBe("r1");
        expect(s.steps.map((x) => [x.index, x.name, x.status])).toEqual([
            [0, "A", "pending"],
            [1, "B", "pending"],
        ]);
    });

    it("stepStarted marks only that step running", () => {
        const s = reduceApiRunEvent(
            reduceApiRunEvent(startedApiRunState(), plan),
            { type: "stepStarted", index: 1, requestId: "b", name: "B" },
        );
        expect(s.steps[0].status).toBe("pending");
        expect(s.steps[1].status).toBe("running");
    });

    it("stepCompleted records status, duration, captures and response", () => {
        const s = reduceApiRunEvent(
            reduceApiRunEvent(startedApiRunState(), plan),
            {
                type: "stepCompleted",
                index: 0,
                requestId: "a",
                status: 201,
                durationMs: 42,
                captured: [{ targetVariable: "tok", source: "$.token" }],
                response: fakeResponse(201),
            },
        );
        const step = s.steps[0];
        expect(step.status).toBe("completed");
        expect(step.httpStatus).toBe(201);
        expect(step.durationMs).toBe(42);
        expect(step.captured).toEqual([
            { targetVariable: "tok", source: "$.token" },
        ]);
        expect(step.response?.statusCode).toBe(201);
    });

    it("stepFailed keeps an optional response and the error text", () => {
        const s = reduceApiRunEvent(
            reduceApiRunEvent(startedApiRunState(), plan),
            {
                type: "stepFailed",
                index: 0,
                requestId: "a",
                status: 500,
                durationMs: 7,
                error: "boom",
                response: fakeResponse(500),
            },
        );
        expect(s.steps[0].status).toBe("failed");
        expect(s.steps[0].error).toBe("boom");
        expect(s.steps[0].httpStatus).toBe(500);
        expect(s.steps[0].response?.statusCode).toBe(500);
    });

    it("ignores step events for indexes the plan never listed", () => {
        const s0 = reduceApiRunEvent(startedApiRunState(), plan);
        const s = reduceApiRunEvent(s0, {
            type: "stepStarted",
            index: 9,
            requestId: "zz",
            name: "zz",
        });
        expect(s).toBe(s0);
    });

    it("aborted settles running steps as failed-cancelled and reports reason", () => {
        let s = reduceApiRunEvent(startedApiRunState(), plan);
        s = reduceApiRunEvent(s, {
            type: "stepCompleted",
            index: 0,
            requestId: "a",
            status: 200,
            durationMs: 1,
            captured: [],
            response: fakeResponse(),
        });
        s = reduceApiRunEvent(s, {
            type: "stepStarted",
            index: 1,
            requestId: "b",
            name: "B",
        });
        s = reduceApiRunEvent(s, {
            type: "aborted",
            reason: "stopOnError",
            completedSteps: 1,
        });
        expect(s.status).toBe("aborted");
        expect(s.abortReason).toBe("stopOnError");
        expect(s.steps[1].status).toBe("failed");
        expect(s.steps[1].error).toBe("Cancelled");
    });

    it("done stores the summary", () => {
        let s = reduceApiRunEvent(startedApiRunState(), plan);
        s = reduceApiRunEvent(s, {
            type: "done",
            completedSteps: 2,
            failedSteps: 0,
            durationMs: 99,
        });
        expect(s.status).toBe("done");
        expect(s.summary).toEqual({
            completedSteps: 2,
            failedSteps: 0,
            durationMs: 99,
        });
    });

    it("tolerates unknown event shapes", () => {
        const s0 = startedApiRunState();
        const s = reduceApiRunEvent(s0, {
            type: "futureEvent",
        } as unknown as ApiRunEvent);
        expect(s).toBe(s0);
    });
});

describe("run state helpers", () => {
    it("lastResponseOf returns the last step that produced a response", () => {
        let s = reduceApiRunEvent(startedApiRunState(), plan);
        s = reduceApiRunEvent(s, {
            type: "stepCompleted",
            index: 0,
            requestId: "a",
            status: 200,
            durationMs: 1,
            captured: [],
            response: fakeResponse(200),
        });
        s = reduceApiRunEvent(s, {
            type: "stepFailed",
            index: 1,
            requestId: "b",
            durationMs: 1,
            error: "x",
            response: fakeResponse(502),
        });
        expect(lastResponseOf(s.steps)?.statusCode).toBe(502);
    });

    it("lastResponseOf is null when nothing responded yet", () => {
        const s = reduceApiRunEvent(startedApiRunState(), plan);
        expect(lastResponseOf(s.steps)).toBeNull();
    });

    it("finishedStepCount counts completed + failed, not pending/running", () => {
        let s = reduceApiRunEvent(startedApiRunState(), plan);
        s = reduceApiRunEvent(s, {
            type: "stepStarted",
            index: 0,
            requestId: "a",
            name: "A",
        });
        expect(finishedStepCount(s.steps)).toBe(0);
        s = reduceApiRunEvent(s, {
            type: "stepCompleted",
            index: 0,
            requestId: "a",
            status: 200,
            durationMs: 1,
            captured: [],
            response: fakeResponse(),
        });
        expect(finishedStepCount(s.steps)).toBe(1);
    });

    it("initialApiRunState is idle with empty steps", () => {
        expect(initialApiRunState().status).toBe("idle");
        expect(initialApiRunState().steps).toEqual([]);
    });
});

describe("finalizeRunState / cancelRunState", () => {
    // The stream end/abort fallbacks run *after* the last event's setState is
    // queued but before React flushes it — they must synthesize a terminal
    // state only when the reduced state is genuinely still "running".
    it("finalizeRunState turns a running state into done with seen counts", () => {
        let s = reduceApiRunEvent(startedApiRunState(), plan);
        s = reduceApiRunEvent(s, {
            type: "stepCompleted",
            index: 0,
            requestId: "a",
            status: 200,
            durationMs: 5,
            captured: [],
            response: fakeResponse(),
        });
        const done = finalizeRunState(s);
        expect(done.status).toBe("done");
        expect(done.summary).toEqual({
            completedSteps: 1,
            failedSteps: 0,
            durationMs: 0,
        });
    });

    it("finalizeRunState never overwrites a real terminal state", () => {
        let s = reduceApiRunEvent(startedApiRunState(), plan);
        s = reduceApiRunEvent(s, {
            type: "done",
            completedSteps: 2,
            failedSteps: 0,
            durationMs: 42,
        });
        expect(finalizeRunState(s)).toBe(s);
    });

    it("cancelRunState settles running steps and reports cancelled", () => {
        let s = reduceApiRunEvent(startedApiRunState(), plan);
        s = reduceApiRunEvent(s, {
            type: "stepStarted",
            index: 1,
            requestId: "b",
            name: "B",
        });
        const next = cancelRunState(s);
        expect(next.status).toBe("aborted");
        expect(next.abortReason).toBe("cancelled");
        expect(next.steps[0].status).toBe("pending");
        expect(next.steps[1].status).toBe("failed");
        expect(next.steps[1].error).toBe("Cancelled");
    });

    it("cancelRunState leaves a terminal state untouched", () => {
        let s = reduceApiRunEvent(startedApiRunState(), plan);
        s = reduceApiRunEvent(s, {
            type: "done",
            completedSteps: 2,
            failedSteps: 0,
            durationMs: 1,
        });
        expect(cancelRunState(s)).toBe(s);
    });
});

// ── Tree ordering ────────────────────────────────────────────────────────────

describe("flattenRequestNodes / collectSubtreeRequestIds", () => {
    const col = collection([
        requestNode("r1"),
        folderNode("f1", [requestNode("r2"), requestNode("r3")]),
        requestNode("r4"),
    ]);

    it("flattens in display order, folders excluded", () => {
        expect(flattenRequestNodes(col).map((n) => n.id)).toEqual([
            "r1",
            "r2",
            "r3",
            "r4",
        ]);
    });

    it("collection id resolves to every request", () => {
        expect(collectSubtreeRequestIds(col, "col")).toEqual([
            "r1",
            "r2",
            "r3",
            "r4",
        ]);
    });

    it("folder id resolves to its subtree only", () => {
        expect(collectSubtreeRequestIds(col, "f1")).toEqual(["r2", "r3"]);
    });

    it("request node degenerates to itself", () => {
        expect(collectSubtreeRequestIds(col, "r2")).toEqual(["r2"]);
    });

    it("unknown node resolves to an empty list", () => {
        expect(collectSubtreeRequestIds(col, "nope")).toEqual([]);
    });

    it("orderIdsByTreeOrder re-sorts a jumbled selection and drops unknowns", () => {
        expect(orderIdsByTreeOrder(col, ["r4", "zz", "r1", "r3"])).toEqual([
            "r1",
            "r3",
            "r4",
        ]);
    });
});

// ── Dependency graph ─────────────────────────────────────────────────────────

describe("dependencyClosure / dependencyPickerCandidates", () => {
    // r1 ← r2 ← r3; r4 standalone. deps point backwards (r3 runs after r2).
    const col = collection([
        requestNode("r1"),
        requestNode("r2", ["r1"]),
        requestNode("r3", ["r2"]),
        requestNode("r4"),
    ]);

    it("closure walks the transitive chain", () => {
        expect([...dependencyClosure(col, "r3")].sort()).toEqual([
            "r1",
            "r2",
            "r3",
        ]);
    });

    it("closure survives a pre-existing cycle without recursing forever", () => {
        const cyclic = collection([
            requestNode("x", ["y"]),
            requestNode("y", ["x"]),
        ]);
        expect([...dependencyClosure(cyclic, "x")].sort()).toEqual(["x", "y"]);
    });

    it("picker excludes self and existing deps", () => {
        const ids = dependencyPickerCandidates(col, "r2", ["r1"]).map(
            (n) => n.id,
        );
        expect(ids).not.toContain("r1");
        expect(ids).not.toContain("r2");
    });

    it("picker excludes a candidate that would close a cycle", () => {
        // For r1: offering r2 or r3 would cycle (r2/r3 already depend on r1).
        const ids = dependencyPickerCandidates(col, "r1", []).map((n) => n.id);
        expect(ids).not.toContain("r2");
        expect(ids).not.toContain("r3");
        expect(ids).toContain("r4");
    });

    it("picker allows a fresh dep on a leaf request", () => {
        const ids = dependencyPickerCandidates(col, "r4", []).map((n) => n.id);
        expect(ids).toEqual(["r1", "r2", "r3"]);
    });

    // Imported/hand-edited collections can give a node id different from its
    // request entry id — the wire ids are the *entry* ids, so dep references
    // and cycle checks must resolve in both spaces.
    function divergentNode(
        nodeId: string,
        entryId: string,
        deps: string[] = [],
    ): ApiCollectionNode {
        const node = requestNode(nodeId, []);
        node.request = {
            ...node.request!,
            id: entryId,
            dependsOnRequestIds: deps,
        };
        return node;
    }

    it("findRequestNodeAnyId resolves both node and entry ids", () => {
        const div = collection([divergentNode("node-1", "entry-1")]);
        expect(findRequestNodeAnyId(div.nodes, "node-1")?.id).toBe("node-1");
        expect(findRequestNodeAnyId(div.nodes, "entry-1")?.id).toBe("node-1");
        expect(requestEntryId(div.nodes[0])).toBe("entry-1");
    });

    it("dependencyClosure follows entry-id dep references on divergent ids", () => {
        // node-2 depends on entry-1 (entry id of node-1) — the closure must
        // reach node-1 through the entry id, exactly as the server indexes it.
        const div = collection([
            divergentNode("node-1", "entry-1"),
            divergentNode("node-2", "entry-2", ["entry-1"]),
        ]);
        const closure = dependencyClosure(div, "entry-2");
        expect(closure.has("entry-1")).toBe(true);
        expect(closure.has("node-1")).toBe(true);
        // Cycle check uses .has(nodeId): adding entry-2 as a dep of node-1
        // would close a loop — node-1's id space is covered either way.
        expect(closure.has("node-1")).toBe(true);
    });

    it("picker excludes a dep stored by entry id on divergent ids", () => {
        const div = collection([
            divergentNode("node-1", "entry-1"),
            divergentNode("node-2", "entry-2"),
        ]);
        const ids = dependencyPickerCandidates(div, "node-2", ["entry-1"]).map(
            (n) => n.id,
        );
        expect(ids).toEqual([]);
    });
});

// ── Multi-selection ──────────────────────────────────────────────────────────

describe("toggleMultiSelect", () => {
    it("starts a selection, toggles in and out", () => {
        let s = toggleMultiSelect(null, "a", "col");
        expect(s?.ids.has("a")).toBe(true);
        s = toggleMultiSelect(s, "b", "col");
        expect(s?.ids.size).toBe(2);
        s = toggleMultiSelect(s, "a", "col");
        expect(s?.ids.has("a")).toBe(false);
        expect(s?.ids.has("b")).toBe(true);
    });

    it("collapses to null when the last id is removed", () => {
        const s = toggleMultiSelect(
            toggleMultiSelect(null, "a", "col"),
            "a",
            "col",
        );
        expect(s).toBeNull();
    });

    it("toggling a node in another collection restarts the selection there", () => {
        const s = toggleMultiSelect(
            toggleMultiSelect(null, "a", "col1"),
            "x",
            "col2",
        );
        expect(s?.collectionId).toBe("col2");
        expect([...s!.ids]).toEqual(["x"]);
    });
});

describe("runnableSelection", () => {
    const col = collection([
        requestNode("r1"),
        folderNode("f1", [requestNode("r2")]),
    ]);

    it("returns the ids when every one is a request node", () => {
        expect(runnableSelection(col, new Set(["r1", "r2"]))).toEqual([
            "r1",
            "r2",
        ]);
    });

    it("is null when a folder sneaks into the selection", () => {
        expect(runnableSelection(col, new Set(["r1", "f1"]))).toBeNull();
    });

    it("is null for empty or unknown selections", () => {
        expect(runnableSelection(col, new Set())).toBeNull();
        expect(runnableSelection(col, new Set(["ghost"]))).toBeNull();
    });
});
