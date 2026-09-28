import { describe, it, expect } from "vitest";
import { classifyEnvironment, environmentBadgeTitle } from "./env-badge";

describe("classifyEnvironment", () => {
    it("explicit tag is authoritative for label and tier", () => {
        expect(
            classifyEnvironment({ environmentTag: "prd" }),
        ).toEqual({ tier: "prd", label: "PRD", source: "tag" });
        expect(
            classifyEnvironment({ environmentTag: "stg" }),
        ).toEqual({ tier: "stg", label: "STG", source: "tag" });
        expect(
            classifyEnvironment({ environmentTag: "dev" }),
        ).toEqual({ tier: "dev", label: "DEV", source: "tag" });
    });

    it("still renders a custom tag it cannot classify, tinted neutral", () => {
        expect(classifyEnvironment({ environmentTag: "qa" })).toEqual({
            tier: "neutral",
            label: "QA",
            source: "tag",
        });
    });

    it("tag wins over isProduction — the operator's choice overrides the flag", () => {
        expect(
            classifyEnvironment({ environmentTag: "stg", isProduction: true }),
        ).toEqual({ tier: "stg", label: "STG", source: "tag" });
    });

    it("blank/whitespace tags fall through to the flag", () => {
        expect(
            classifyEnvironment({ environmentTag: "  ", isProduction: true })
                .tier,
        ).toBe("prd");
        expect(
            classifyEnvironment({ environmentTag: null, isProduction: true })
                .source,
        ).toBe("flag");
    });

    it("isProduction classifies as PRD without needing a name match", () => {
        expect(
            classifyEnvironment({ isProduction: true, name: "Default" }),
        ).toEqual({ tier: "prd", label: "PRD", source: "flag" });
    });

    it("flag wins over name heuristics", () => {
        expect(
            classifyEnvironment({ isProduction: true, name: "dev sandbox" })
                .tier,
        ).toBe("prd");
    });

    it.each([
        ["orders-prod-sql", "prd"],
        ["Production", "prd"],
        ["prd-eu", "prd"],
        ["Staging", "stg"],
        ["uat-eu", "stg"],
        ["payments-stg", "stg"],
        ["dev", "dev"],
        ["integration-test", "dev"],
        ["local", "dev"],
    ])("name %s classifies as %s", (name, tier) => {
        expect(classifyEnvironment({ name })).toEqual({
            tier,
            label: (tier as string).toUpperCase(),
            source: "name",
        });
    });

    it("production terms win when a name contains several tier tokens", () => {
        // Err toward caution: a dev mirror OF production still mutates prod.
        expect(classifyEnvironment({ name: "dev-prod-mirror" }).tier).toBe(
            "prd",
        );
    });

    it("unmatched names are neutral and show the profile name", () => {
        expect(classifyEnvironment({ name: "Default" })).toEqual({
            tier: "neutral",
            label: "Default",
            source: "none",
        });
    });

    it("empty everything is neutral with a placeholder label", () => {
        expect(classifyEnvironment({})).toEqual({
            tier: "neutral",
            label: "Unnamed",
            source: "none",
        });
        expect(classifyEnvironment({ name: "  " }).label).toBe("Unnamed");
    });
});

describe("environmentBadgeTitle", () => {
    it("explains the classification source", () => {
        expect(
            environmentBadgeTitle({
                tier: "prd",
                label: "PRD",
                source: "flag",
            }),
        ).toContain("production");
        expect(
            environmentBadgeTitle({
                tier: "neutral",
                label: "Default",
                source: "none",
            }),
        ).toContain("profile name");
    });
});
