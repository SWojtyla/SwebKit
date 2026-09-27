/**
 * Environment classification for the shell badge (ux-power-pack §3).
 *
 * Pure functions — vitest coverage lives in env-badge.test.ts and the module
 * must stay free of React/DOM imports so it runs under the node environment.
 *
 * Precedence: an explicit `environmentTag` is authoritative (it decides both
 * label and tier — an unrecognized tag still renders, tinted neutral) →
 * `isProduction` → name heuristics → neutral.
 */

export type EnvironmentTier = "prd" | "stg" | "dev" | "neutral";

export type EnvironmentSource = "tag" | "flag" | "name" | "none";

export interface EnvironmentBadgeInfo {
    tier: EnvironmentTier;
    /** Pill/status-bar text — canonical PRD/STG/DEV, an explicit custom tag
     * uppercased, or the profile name when nothing classified. */
    label: string;
    /** Which input produced the classification (drives the tooltip). */
    source: EnvironmentSource;
}

export interface EnvironmentBadgeInput {
    environmentTag?: string | null;
    isProduction?: boolean;
    name?: string | null;
}

// Production terms are checked first so a name like "dev-prod-mirror" errs on
// the cautious tier. Matching is a lowercase substring check — "production",
// "orders-prd-sql" and "prd-eu" all classify without needing exact tokens.
const TIER_TERMS: ReadonlyArray<{
    tier: Exclude<EnvironmentTier, "neutral">;
    terms: readonly string[];
}> = [
    { tier: "prd", terms: ["prod", "prd"] },
    { tier: "stg", terms: ["stg", "staging", "uat"] },
    { tier: "dev", terms: ["dev", "test", "local"] },
];

function tierFromText(text: string): EnvironmentTier | null {
    const lower = text.toLowerCase();
    for (const { tier, terms } of TIER_TERMS) {
        if (terms.some((term) => lower.includes(term))) return tier;
    }
    return null;
}

export function classifyEnvironment(
    config: EnvironmentBadgeInput,
): EnvironmentBadgeInfo {
    const tag = config.environmentTag?.trim();
    if (tag) {
        return {
            tier: tierFromText(tag) ?? "neutral",
            label: tag.toUpperCase(),
            source: "tag",
        };
    }

    if (config.isProduction) {
        return { tier: "prd", label: "PRD", source: "flag" };
    }

    const name = config.name?.trim() ?? "";
    const tier = tierFromText(name);
    if (tier) {
        return { tier, label: tier.toUpperCase(), source: "name" };
    }

    return {
        tier: "neutral",
        label: name || "Unnamed",
        source: "none",
    };
}

/** Tooltip for the badge — says *why* the pill reads what it does. */
export function environmentBadgeTitle(info: EnvironmentBadgeInfo): string {
    switch (info.source) {
        case "tag":
            return `Environment tag: ${info.label}`;
        case "flag":
            return "Profile flagged as production";
        case "name":
            return `Inferred ${info.label} from the profile name`;
        case "none":
            return "No environment configured — showing the profile name";
    }
}
