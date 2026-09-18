import { describe, it, expect } from "vitest";
import {
    buildGraphElements,
    filterTopology,
    groupCandidates,
    isOrphan,
    relationshipCountFor,
    suggestionsForNode,
} from "./workspace-map-utils";
import { oklchToHex } from "@/lib/theme-colors";
import type {
    WorkspaceResourceCandidate,
    WorkspaceResourceNode,
    WorkspaceTopology,
} from "@/lib/types";

const node = (
    id: string,
    area: WorkspaceResourceNode["area"],
    resourceKey: string,
    displayLabel = id,
): WorkspaceResourceNode => ({ id, area, resourceKey, displayLabel });

const topology = (
    nodes: WorkspaceResourceNode[],
    relationships: WorkspaceTopology["relationships"] = [],
): WorkspaceTopology => ({ nodes, relationships });

describe("relationshipCountFor / isOrphan", () => {
    const t = topology(
        [node("a", "Aks", "ns/api"), node("b", "Redis", "cache"), node("c", "Sql", "srv/db")],
        [
            { id: "r1", fromNodeId: "a", toNodeId: "b", label: "caches" },
            { id: "r2", fromNodeId: "b", toNodeId: "a", label: null },
        ],
    );

    it("counts relationships in both directions", () => {
        expect(relationshipCountFor(t, "a")).toBe(2);
        expect(relationshipCountFor(t, "b")).toBe(2);
    });

    it("flags nodes with zero relationships as orphans", () => {
        expect(isOrphan(t, "c")).toBe(true);
        expect(isOrphan(t, "a")).toBe(false);
    });
});

describe("filterTopology", () => {
    const t = topology(
        [
            node("a", "Aks", "prod/api", "api"),
            node("b", "ServiceBus", "orders.sb.net", "orders"),
            node("c", "Redis", "cache-1", "sessions"),
        ],
        [
            { id: "r1", fromNodeId: "a", toNodeId: "b", label: "consumes" },
            { id: "r2", fromNodeId: "b", toNodeId: "c", label: null },
        ],
    );
    const all = new Set(["Aks", "ServiceBus", "Redis", "Sql", "Storage"] as const);

    it("matches search against label and resource key, case-insensitive", () => {
        expect(filterTopology(t, "API", all).nodes.map((n) => n.id)).toEqual(["a"]);
        expect(filterTopology(t, "orders.sb", all).nodes.map((n) => n.id)).toEqual(["b"]);
    });

    it("drops relationships whose endpoints are filtered out", () => {
        const result = filterTopology(t, "", new Set(["Aks", "Redis"]));
        expect(result.nodes.map((n) => n.id)).toEqual(["a", "c"]);
        expect(result.relationships).toEqual([]);
    });

    it("keeps edges between visible nodes", () => {
        const result = filterTopology(t, "", new Set(["Aks", "ServiceBus"]));
        expect(result.relationships.map((r) => r.id)).toEqual(["r1"]);
    });
});

describe("buildGraphElements", () => {
    const t = topology(
        [node("a", "Aks", "ns/api", "api"), node("b", "ServiceBus", "q", "queue")],
        [{ id: "r1", fromNodeId: "a", toNodeId: "b", label: "consumes" }],
    );

    it("renders confirmed relationships as solid labeled edges", () => {
        const { nodes, edges } = buildGraphElements(t, []);
        expect(nodes).toEqual([
            { id: "a", label: "api", area: "Aks" },
            { id: "b", label: "queue", area: "ServiceBus" },
        ]);
        expect(edges).toEqual([{ from: "a", to: "b", label: "consumes" }]);
    });

    it("renders suggestions as dashed edges", () => {
        const { edges } = buildGraphElements(
            topology([node("a", "Aks", "ns/api"), node("b", "Redis", "c")]),
            [{ fromNodeId: "a", toNodeId: "b", reason: "matched" }],
        );
        expect(edges).toEqual([{ from: "a", to: "b", dashed: true }]);
    });

    it("suppresses suggestions for pairs that are already declared", () => {
        const { edges } = buildGraphElements(t, [
            { fromNodeId: "b", toNodeId: "a", reason: "reverse pair" },
        ]);
        expect(edges).toHaveLength(1);
        expect(edges[0].dashed).toBeUndefined();
    });

    it("drops suggestions whose endpoints aren't in the node set", () => {
        const { edges } = buildGraphElements(t, [
            { fromNodeId: "a", toNodeId: "ghost", reason: "dangling" },
        ]);
        expect(edges).toHaveLength(1);
    });
});

describe("suggestionsForNode", () => {
    it("returns only suggestions touching the node", () => {
        const suggestions = [
            { fromNodeId: "a", toNodeId: "b", reason: "x" },
            { fromNodeId: "b", toNodeId: "c", reason: "y" },
        ];
        expect(suggestionsForNode(suggestions, "a")).toHaveLength(1);
        expect(suggestionsForNode(suggestions, "b")).toHaveLength(2);
    });
});

describe("groupCandidates", () => {
    const candidate = (
        area: WorkspaceResourceCandidate["area"],
        resourceKey: string,
        displayLabel: string,
    ): WorkspaceResourceCandidate => ({ area, resourceKey, displayLabel });

    it("groups by area, sorts by label, and skips already-added keys", () => {
        const t = topology([node("n1", "Redis", "cache-a")]);
        const groups = groupCandidates(
            [
                candidate("Redis", "cache-b", "Zulu"),
                candidate("Redis", "cache-a", "Already there"),
                candidate("Redis", "cache-c", "Alpha"),
                candidate("Aks", "ns/api", "api"),
            ],
            t,
        );
        expect(groups.get("Redis")!.map((c) => c.resourceKey)).toEqual([
            "cache-c",
            "cache-b",
        ]);
        expect(groups.get("Aks")).toHaveLength(1);
    });
});

describe("oklchToHex", () => {
    it("converts pure white and black", () => {
        expect(oklchToHex("oklch(1 0 0)")).toBe("#ffffff");
        expect(oklchToHex("oklch(0 0 0)")).toBe("#000000");
    });

    it("converts a theme-color oklch value to a valid hex color", () => {
        const hex = oklchToHex("oklch(0.65 0.24 265)");
        expect(hex).toMatch(/^#[0-9a-f]{6}$/);
    });

    it("handles percent lightness and alpha suffix", () => {
        expect(oklchToHex("oklch(100% 0 0 / 50%)")).toBe("#ffffff");
    });

    it("passes through non-oklch input unchanged", () => {
        expect(oklchToHex("#aabbcc")).toBe("#aabbcc");
        expect(oklchToHex("rgb(1, 2, 3)")).toBe("rgb(1, 2, 3)");
    });
});
