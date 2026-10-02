import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";
import { sidecarPort } from "./test-config";

const sidecarUrl = `http://127.0.0.1:${sidecarPort}`;

// Multi-context workspace: the context picker is a plain multi-select — every
// checked cluster feeds the merged view, none is privileged. The demo profile's
// configured context is aks-ecommerce-dev (auto-selected on first visit);
// aks-ecommerce-staging joins via the picker's checkbox.

async function attachStaging(page: import("@playwright/test").Page) {
    await page.getByTestId("aks-context-select").click();
    // Wait on the option row first — if the contexts query is still resolving or
    // failed, the dropdown renders empty and a bare checkbox click would stall on
    // the wrong element with no hint about which layer broke.
    const row = page.getByTestId("aks-context-option-aks-ecommerce-staging");
    try {
        await row.waitFor({ timeout: 15_000 });
    } catch (err) {
        const select = page.getByTestId("aks-context-select");
        const rendered = await page
            .locator("[data-testid^='aks-context-option-']")
            .evaluateAll((els) => els.map((el) => el.textContent?.trim()));
        const demo = await page.request
            .get(`${sidecarUrl}/api/demo-mode`)
            .then((r) => r.json())
            .catch((e) => String(e));
        const direct = await page.request
            .get(`${sidecarUrl}/api/aks/contexts`)
            .then((r) => r.json())
            .catch((e) => String(e));
        console.log(
            "attachStaging diagnostics:",
            JSON.stringify({
                url: page.url(),
                expanded: await select.getAttribute("aria-expanded"),
                disabled: await select.isDisabled(),
                renderedOptions: rendered,
                sidecarDemoMode: demo,
                directContexts: direct,
            }),
        );
        throw err;
    }
    // Not `.check()`: the controlled checkbox only reports checked after the
    // `ctxs` URL write re-renders the row, which outlives check()'s post-click
    // verification. Click + auto-waiting assertion instead.
    const checkbox = page.getByTestId(
        "aks-context-check-aks-ecommerce-staging",
    );
    await checkbox.click();
    await expect(checkbox).toBeChecked();
    await page.keyboard.press("Escape");
}

test.describe("AKS multi-context", () => {
    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, true);
        await page.goto("/aks");
    });

    test.afterEach(async ({ page }) => {
        await setDemoMode(page, false);
    });

    test("selecting a context adds it to the URL and counts the picker", async ({
        page,
    }) => {
        await attachStaging(page);

        await expect(page.getByTestId("aks-context-select")).toContainText(
            "2 contexts",
        );
        await expect(page).toHaveURL(/ctxs=[^&]*aks-ecommerce-staging/);
    });

    test("the namespace picker groups selections per cluster", async ({
        page,
    }) => {
        await attachStaging(page);

        await page.getByTestId("aks-namespace-dropdown").click();
        await expect(
            page.getByTestId("aks-ns-group-aks-ecommerce-dev"),
        ).toBeVisible();
        await expect(
            page.getByTestId("aks-ns-group-aks-ecommerce-staging"),
        ).toBeVisible();
    });

    test("the merged pods view shows a Context column with both clusters", async ({
        page,
    }) => {
        await attachStaging(page);
        // Each cluster's init seeds its kubeconfig namespace hint — staging's is
        // "ecommerce" — so both clusters feed rows without any manual namespace pick.
        await expect(page.getByTestId("aks-namespace-dropdown")).toContainText(
            "namespaces",
        );

        await page.getByTestId("aks-tab-pods").click();
        await expect(page.getByTestId("pods-table-body")).toBeVisible();

        await expect(page.getByTestId("pods-sort-context")).toBeVisible();
        const contexts = page.locator('[data-testid^="pod-cell-context-"]');
        await expect(
            contexts.filter({ hasText: "aks-ecommerce-dev" }).first(),
        ).toBeVisible();
        await expect(
            contexts.filter({ hasText: "aks-ecommerce-staging" }).first(),
        ).toBeVisible();
    });

    test("a deep link restores the selected contexts and their namespaces", async ({
        page,
    }) => {
        await attachStaging(page);
        await page.getByTestId("aks-tab-pods").click();
        await expect(page.getByTestId("pods-sort-context")).toBeVisible();

        await page.reload();

        await expect(page.getByTestId("aks-context-select")).toContainText(
            "2 contexts",
        );
        await page.getByTestId("aks-tab-pods").click();
        await expect(page.getByTestId("pods-sort-context")).toBeVisible();
    });

    test("a failed context shows a named banner while healthy rows still render", async ({
        page,
    }) => {
        await attachStaging(page);

        await page.route(
            "**/api/aks/*/pods?context=aks-ecommerce-staging*",
            async (route) => {
                await route.fulfill({
                    status: 500,
                    json: { error: "RBAC denied" },
                });
            },
        );

        await page.getByTestId("aks-tab-pods").click();

        await expect(
            page.getByTestId("pods-context-error-aks-ecommerce-staging"),
        ).toBeVisible();
        // The other cluster's rows are unaffected by the second cluster's failure.
        await expect(
            page.getByTestId("pods-table-body").locator("tr").first(),
        ).toBeVisible();
    });

    test("an action on a second-cluster row names that cluster in the confirm bar", async ({
        page,
    }) => {
        await attachStaging(page);
        await expect(page.getByTestId("aks-namespace-dropdown")).toContainText(
            "namespaces",
        );

        const stagingRow = page
            .getByTestId("deployments-table-body")
            .locator("tr", { hasText: "aks-ecommerce-staging" })
            .first();
        await expect(stagingRow).toBeVisible();
        await stagingRow.getByRole("button", { name: "Restart" }).click();

        await expect(page.getByTestId("aks-confirm-bar")).toContainText(
            "in aks-ecommerce-staging",
        );
        await page.getByTestId("aks-confirm-cancel").click();
    });

    test("deselecting the extra context removes the Context column", async ({
        page,
    }) => {
        await attachStaging(page);
        await page.getByTestId("aks-tab-pods").click();
        await expect(page.getByTestId("pods-sort-context")).toBeVisible();

        await page.getByTestId("aks-context-select").click();
        const checkbox = page.getByTestId(
            "aks-context-check-aks-ecommerce-staging",
        );
        await checkbox.click();
        await expect(checkbox).not.toBeChecked();
        await page.keyboard.press("Escape");

        await expect(page.getByTestId("aks-context-select")).toContainText(
            "aks-ecommerce-dev",
        );
        await expect(page.getByTestId("pods-sort-context")).toHaveCount(0);
    });

    test("the configured context can be deselected like any other", async ({
        page,
    }) => {
        await attachStaging(page);

        await page.getByTestId("aks-context-select").click();
        const devCheckbox = page.getByTestId(
            "aks-context-check-aks-ecommerce-dev",
        );
        await expect(devCheckbox).toBeChecked();
        await expect(devCheckbox).toBeEnabled();
        await devCheckbox.click();
        await expect(devCheckbox).not.toBeChecked();
        await page.keyboard.press("Escape");

        // Only staging is selected now — the URL carries the whole set and the
        // namespace picker groups under staging alone.
        await expect(page).toHaveURL(
            /ctxs=aks-ecommerce-staging(?!.*aks-ecommerce-dev)/,
        );
        await page.getByTestId("aks-namespace-dropdown").click();
        await expect(
            page.getByTestId("aks-ns-group-aks-ecommerce-staging"),
        ).toBeVisible();
        await expect(
            page.getByTestId("aks-ns-group-aks-ecommerce-dev"),
        ).toHaveCount(0);
    });

    test("select-only narrows the view to a single context", async ({
        page,
    }) => {
        await attachStaging(page);

        await page.getByTestId("aks-context-select").click();
        await page
            .getByTestId("aks-context-only-aks-ecommerce-staging")
            .click();

        await expect(page.getByTestId("aks-context-select")).toContainText(
            "aks-ecommerce-staging",
        );
        await expect(page).toHaveURL(/ctxs=aks-ecommerce-staging(?:&|$)/);
    });
});
