import { test, expect, type Page } from "@playwright/test";
import { setDemoMode } from "./helpers";
import { sidecarPort } from "./test-config";

const sidecarUrl = `http://127.0.0.1:${sidecarPort}`;

/**
 * Patches the persisted profile's environment fields directly through the
 * sidecar. The badge only reacts on the next full page load — React Query's
 * `profile` cache is in-memory and a PUT made outside the app can't invalidate
 * it, so each test calls `page.goto` after this.
 */
async function setProfileEnv(
    page: Page,
    patch: { environmentTag?: string | null; isProduction?: boolean },
) {
    const res = await page.request.get(`${sidecarUrl}/api/config/profiles`);
    const profile = (await res.json()) as { config: Record<string, unknown> };
    profile.config = { ...profile.config, ...patch };
    const put = await page.request.put(`${sidecarUrl}/api/config/profiles`, {
        data: profile,
    });
    if (!put.ok()) {
        throw new Error(`profile PUT failed: ${put.status()}`);
    }
}

const NEUTRAL = { environmentTag: null, isProduction: false };

test.describe("Environment badge", () => {
    test.beforeEach(async ({ page }) => {
        // Reset first — a leaked tag/flag from an earlier spec would make the
        // demo profile classify as something other than neutral.
        await setProfileEnv(page, NEUTRAL);
        await setDemoMode(page, true);
    });

    test.afterEach(async ({ page }) => {
        // `isProduction` changes AKS mutation prompts — never let it leak.
        await setProfileEnv(page, NEUTRAL);
        await setDemoMode(page, false);
    });

    test("unclassified profile shows the profile name, no PRD banner", async ({
        page,
    }) => {
        // The e2e appdata profile is the stock "Default" — no tag, flag off, and
        // the name hits no heuristic, so the badge stays neutral.
        await page.goto("/");
        const badge = page.getByTestId("env-badge");
        await expect(badge).toBeVisible();
        await expect(badge).toHaveText("Default");
        await expect(badge).toHaveAttribute("data-env-tier", "neutral");
        await expect(page.getByTestId("env-prd-banner")).toHaveCount(0);
        await expect(page.getByTestId("status-bar-env")).toHaveText("Default");
    });

    test("environmentTag=prd renders the destructive pill and the banner", async ({
        page,
    }) => {
        await setProfileEnv(page, { environmentTag: "prd" });
        await page.goto("/");

        const badge = page.getByTestId("env-badge");
        await expect(badge).toHaveText("PRD");
        await expect(badge).toHaveAttribute("data-env-tier", "prd");

        const banner = page.getByTestId("env-prd-banner");
        await expect(banner).toBeVisible();
        await expect(banner).toContainText(
            "Production profile — mutations guarded",
        );

        await expect(page.getByTestId("status-bar-env")).toHaveText("PRD");
    });

    test("isProduction alone classifies as PRD", async ({ page }) => {
        await setProfileEnv(page, { isProduction: true });
        await page.goto("/");

        await expect(page.getByTestId("env-badge")).toHaveAttribute(
            "data-env-tier",
            "prd",
        );
        await expect(page.getByTestId("env-prd-banner")).toBeVisible();
    });

    test("a custom tag renders uppercased without a tier tint", async ({
        page,
    }) => {
        await setProfileEnv(page, { environmentTag: "qa" });
        await page.goto("/");

        const badge = page.getByTestId("env-badge");
        await expect(badge).toHaveText("QA");
        await expect(badge).toHaveAttribute("data-env-tier", "neutral");
        await expect(page.getByTestId("env-prd-banner")).toHaveCount(0);
    });
});
