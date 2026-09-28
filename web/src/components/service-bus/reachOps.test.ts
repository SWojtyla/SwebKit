import { describe, expect, it } from "vitest";
import {
    clampMaxParked,
    isResumableState,
    isTerminalState,
    opProgressText,
    parseSequenceList,
    parseTargetSequence,
    REACH_ABSOLUTE_MAX_PARKED,
    REACH_DEFAULT_MAX_PARKED,
} from "./reachOps";
import type { SbOperationStatus } from "@/lib/types";

function op(overrides: Partial<SbOperationStatus>): SbOperationStatus {
    return {
        id: "op-1",
        namespaceId: "ns-1",
        entityPath: "q",
        kind: "reach-message",
        targetSequenceNumber: 42,
        targetAction: "Complete",
        restoreBeforeTarget: true,
        state: "Running",
        phase: null,
        parkedCount: 0,
        restoredCount: 0,
        parkedInDlq: null,
        error: null,
        createdAt: "",
        updatedAt: "",
        // replay-to fields — inert on a reach op
        targetNamespaceId: null,
        targetEntityPath: null,
        sourceIsDeadLetter: false,
        removeSource: false,
        requestedCount: 0,
        replayedCount: 0,
        failedCount: 0,
        missingCount: 0,
        ...overrides,
    };
}

describe("parseTargetSequence", () => {
    it("accepts positive integers", () => {
        expect(parseTargetSequence("42")).toBe(42);
        expect(parseTargetSequence("  4504  ")).toBe(4504);
    });

    it("rejects empty, non-numeric, fractional and non-positive input", () => {
        expect(parseTargetSequence("")).toBeNull();
        expect(parseTargetSequence("   ")).toBeNull();
        expect(parseTargetSequence("abc")).toBeNull();
        expect(parseTargetSequence("4.5")).toBeNull();
        expect(parseTargetSequence("0")).toBeNull();
        expect(parseTargetSequence("-3")).toBeNull();
    });
});

describe("clampMaxParked", () => {
    it("defaults to 1,000 on empty input", () => {
        expect(clampMaxParked(null)).toBe(REACH_DEFAULT_MAX_PARKED);
        expect(clampMaxParked(undefined)).toBe(REACH_DEFAULT_MAX_PARKED);
        expect(clampMaxParked(NaN)).toBe(REACH_DEFAULT_MAX_PARKED);
    });

    it("clamps to [1, 5000] — the server's contract, applied client-side first", () => {
        expect(clampMaxParked(0)).toBe(1);
        expect(clampMaxParked(-10)).toBe(1);
        expect(clampMaxParked(50)).toBe(50);
        expect(clampMaxParked(10_000)).toBe(REACH_ABSOLUTE_MAX_PARKED);
        expect(clampMaxParked(1_000.7)).toBe(1_000);
    });
});

describe("op state classification", () => {
    it("Interrupted/Failed/Cancelled are resumable — parked copies may be in the DLQ", () => {
        expect(isResumableState("Interrupted")).toBe(true);
        expect(isResumableState("Failed")).toBe(true);
        expect(isResumableState("Cancelled")).toBe(true);
        expect(isResumableState("Running")).toBe(false);
        expect(isResumableState("Completed")).toBe(false);
        expect(isResumableState("Dismissed")).toBe(false);
    });

    it("terminal excludes only Running", () => {
        expect(isTerminalState("Running")).toBe(false);
        expect(isTerminalState("Completed")).toBe(true);
        expect(isTerminalState("Interrupted")).toBe(true);
        expect(isTerminalState("Dismissed")).toBe(true);
    });
});

describe("opProgressText", () => {
    it("reports parking progress", () => {
        expect(
            opProgressText(op({ phase: "Parking", parkedCount: 17 })),
        ).toContain("17 parked");
    });

    it("reports restore progress", () => {
        expect(
            opProgressText(op({ phase: "Restoring", restoredCount: 9 })),
        ).toContain("9 restored");
    });

    it("reports replay transfer progress", () => {
        expect(
            opProgressText(
                op({
                    kind: "replay-to",
                    phase: "Transferring",
                    requestedCount: 40,
                    replayedCount: 12,
                    targetEntityPath: "payments-dev/order-failed",
                }),
            ),
        ).toContain("12 of 40");
    });
});

describe("parseSequenceList", () => {
    it("accepts comma/space-separated positive integers", () => {
        expect(parseSequenceList("4501, 4502")).toEqual([4501, 4502]);
        expect(parseSequenceList("4501 4502\n4503")).toEqual([4501, 4502, 4503]);
    });

    it("rejects empty input and non-integer parts", () => {
        expect(parseSequenceList("")).toBeNull();
        expect(parseSequenceList("  ")).toBeNull();
        expect(parseSequenceList("4501, abc")).toBeNull();
        expect(parseSequenceList("4501, 0")).toBeNull();
        expect(parseSequenceList("-5")).toBeNull();
    });
});
