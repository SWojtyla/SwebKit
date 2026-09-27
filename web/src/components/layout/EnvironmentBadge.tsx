import {
    environmentBadgeTitle,
    type EnvironmentBadgeInfo,
    type EnvironmentTier,
} from "@/lib/env-badge";

const TIER_CLASSES: Record<EnvironmentTier, string> = {
    prd: "border-destructive/50 bg-destructive/10 text-destructive",
    stg: "border-warning/60 bg-warning/10 text-warning",
    dev: "border-info/50 bg-info/10 text-info",
    neutral: "bg-muted/60 text-muted-foreground",
};

/**
 * The shell's environment pill (ux-power-pack §3) — a single badge in the top
 * bar plus a matching `status-bar-env` label. Deliberately not stamped per
 * surface: the pill + the PRD banner carry the identity signal.
 */
export function EnvironmentBadge({
    env,
    testId = "env-badge",
}: {
    env: EnvironmentBadgeInfo;
    testId?: string;
}) {
    const classified = env.tier !== "neutral";
    return (
        <span
            className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] ${
                classified
                    ? "font-semibold uppercase tracking-wide"
                    : "font-medium"
            } ${TIER_CLASSES[env.tier]}`}
            title={environmentBadgeTitle(env)}
            data-testid={testId}
            data-env-tier={env.tier}
        >
            {env.label}
        </span>
    );
}
