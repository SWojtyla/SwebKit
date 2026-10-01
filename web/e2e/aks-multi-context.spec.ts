import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";
import { sidecarPort } from "./test-config";

const sidecarUrl = `http://127.0.0.1:${sidecarPort}`;

// Multi-context workspace: attach a second demo cluster beside the primary, browse the
// merged view, and verify every surface keeps the row's cluster identity. The demo
// profile's primary context is aks-ecommerce-dev; aks-ecommerce-staging is attached
// via the picker's checkbox (never promoted — promotion is a profile mutation).

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
        "aks-context-attach-aks-ecommerce-staging",
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

    test("attaching a context adds it to the URL and badges the picker", async ({
        page,
    }) => {
        await attachStaging(page);

        await expect(page.getByTestId("aks-context-attached-count")).toHaveText(
            "+1",
        );
        await expect(page).toHaveURL(/ctxs=aks-ecommerce-staging/);
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
        // Each attached cluster's init seeds its kubeconfig namespace hint — staging's is
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

    test("a deep link restores the attached context and its namespaces", async ({
        page,
    }) => {
        await attachStaging(page);
        await page.getByTestId("aks-tab-pods").click();
        await expect(page.getByTestId("pods-sort-context")).toBeVisible();

        await page.reload();

        await expect(page.getByTestId("aks-context-attached-count")).toHaveText(
            "+1",
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
        // The primary cluster's rows are unaffected by the second cluster's failure.
        await expect(
            page.getByTestId("pods-table-body").locator("tr").first(),
        ).toBeVisible();
    });

    test("an action on a secondary-cluster row names that cluster in the confirm bar", async ({
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

    test("detaching the context removes the Context column", async ({
        page,
    }) => {
        await attachStaging(page);
        await page.getByTestId("aks-tab-pods").click();
        await expect(page.getByTestId("pods-sort-context")).toBeVisible();

        await page.getByTestId("aks-context-select").click();
        const checkbox = page.getByTestId(
            "aks-context-attach-aks-ecommerce-staging",
        );
        await checkbox.click();
        await expect(checkbox).not.toBeChecked();
        await page.keyboard.press("Escape");

        await expect(
            page.getByTestId("aks-context-attached-count"),
        ).toHaveCount(0);
        await expect(page.getByTestId("pods-sort-context")).toHaveCount(0);
    });
});
