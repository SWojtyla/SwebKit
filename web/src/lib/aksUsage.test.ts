import { describe, it, expect } from "vitest";
import {
    absoluteCpuSeverity,
    absoluteMemorySeverity,
    cpuUsageBasis,
    memoryUsageBasis,
    severityForRatio,
} from "./aksUsage";

const pod = (fields: {
    cpuRequestCores?: number | null;
    cpuLimitCores?: number | null;
    memoryRequestBytes?: number | null;
    memoryLimitBytes?: number | null;
}) => ({
    cpuRequestCores: fields.cpuRequestCores ?? null,
    cpuLimitCores: fields.cpuLimitCores ?? null,
    memoryRequestBytes: fields.memoryRequestBytes ?? null,
    memoryLimitBytes: fields.memoryLimitBytes ?? null,
});

describe("cpuUsageBasis", () => {
    it("prefers the limit when both are declared", () => {
        const basis = cpuUsageBasis(
            pod({ cpuRequestCores: 0.25, cpuLimitCores: 0.5 }),
        );
        expect(basis).toEqual({ value: 0.5, kind: "limit" });
    });

    it("falls back to the request when no limit is declared", () => {
        const basis = cpuUsageBasis(pod({ cpuRequestCores: 0.25 }));
        expect(basis).toEqual({ value: 0.25, kind: "request" });
    });

    it("returns null when the pod declares nothing — the fixed-ceiling fallback", () => {
        expect(cpuUsageBasis(pod({}))).toBeNull();
    });

    it("treats zero-valued declarations as undeclared — 0 is not a usable basis", () => {
        expect(
            cpuUsageBasis(pod({ cpuRequestCores: 0, cpuLimitCores: 0 })),
        ).toBeNull();
    });
});

describe("memoryUsageBasis", () => {
    const Gi = 1024 * 1024 * 1024;

    it("prefers the limit when both are declared", () => {
        const basis = memoryUsageBasis(
            pod({ memoryRequestBytes: 0.5 * Gi, memoryLimitBytes: Gi }),
        );
        expect(basis).toEqual({ value: Gi, kind: "limit" });
    });

    it("falls back to the request when no limit is declared", () => {
        const basis = memoryUsageBasis(pod({ memoryRequestBytes: 0.5 * Gi }));
        expect(basis).toEqual({ value: 0.5 * Gi, kind: "request" });
    });

    it("returns null when the pod declares nothing", () => {
        expect(memoryUsageBasis(pod({}))).toBeNull();
    });
});

describe("severityForRatio", () => {
    it("is green well under a limit", () => {
        // The reported bug: 500Mi used against a 1Gi request/limit must not be red.
        expect(severityForRatio(0.5, "limit")).toBe("success");
    });

    it("warns at 70% and goes destructive at 90% of a limit", () => {
        expect(severityForRatio(0.7, "limit")).toBe("warning");
        expect(severityForRatio(0.9, "limit")).toBe("destructive");
        expect(severityForRatio(1.4, "limit")).toBe("destructive");
    });

    it("against a request-only basis, crossing the request is a warning, not critical", () => {
        // Bursting past the guaranteed share is expected — only a heavy overshoot is red.
        expect(severityForRatio(0.99, "request")).toBe("success");
        expect(severityForRatio(1.0, "request")).toBe("warning");
        expect(severityForRatio(1.49, "request")).toBe("warning");
        expect(severityForRatio(1.5, "request")).toBe("destructive");
    });
});

describe("absolute fallbacks", () => {
    it("keeps the legacy absolute CPU thresholds for undeclared pods", () => {
        expect(absoluteCpuSeverity(0.1)).toBe("success");
        expect(absoluteCpuSeverity(0.2)).toBe("warning");
        expect(absoluteCpuSeverity(0.45)).toBe("destructive");
    });

    it("keeps the legacy absolute memory thresholds for undeclared pods", () => {
        expect(absoluteMemorySeverity(150)).toBe("success");
        expect(absoluteMemorySeverity(250)).toBe("warning");
        expect(absoluteMemorySeverity(450)).toBe("destructive");
    });
});
