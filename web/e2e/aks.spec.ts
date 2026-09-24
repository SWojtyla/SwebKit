import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("AKS", () => {
    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, true);
    });

    test.afterEach(async ({ page }) => {
        await setDemoMode(page, false);
    });

    test("selects namespace and displays deployments", async ({ page }) => {
        await page.goto("/aks");
        await page
            .getByTestId("aks-namespace-select")
            .selectOption("ecommerce");

        await expect(page.getByTestId("deployments-table-body")).toBeVisible();
        await expect(
            page.getByTestId("deployments-table-body").locator("tr"),
        ).toHaveCount(10);
        await expect(
            page.getByTestId("deployment-row-order-api"),
        ).toBeVisible();
    });

    test("switches between resource tabs", async ({ page }) => {
        await page.goto("/aks");
        await page
            .getByTestId("aks-namespace-select")
            .selectOption("ecommerce");

        await page.getByTestId("aks-tab-pods").click();
        await expect(page.getByTestId("pods-table-body")).toBeVisible();

        await page.getByTestId("aks-tab-network").click();
        await page.getByTestId("aks-tab-services").click();
        await expect(page.getByTestId("services-table-body")).toBeVisible();

        await page.getByTestId("aks-tab-helm").click();
        await expect(page.getByTestId("helm-table-body")).toBeVisible();

        await page.getByTestId("aks-tab-secrets").click();
        await expect(page.getByTestId("secrets-table-body")).toBeVisible();

        await page.getByTestId("aks-tab-events").click();
        await expect(page.getByTestId("events-list")).toBeVisible();
    });

    test("new resource tabs are visible and functional", async ({ page }) => {
        await page.goto("/aks");
        await page
            .getByTestId("aks-namespace-select")
            .selectOption("ecommerce");

        await page.getByTestId("aks-tab-statefulsets").click();
        await expect(page.getByTestId("statefulsets-table-body")).toBeVisible();

        await page.getByTestId("aks-tab-cronjobs").click();
        await expect(page.getByTestId("cronjobs-table-body")).toBeVisible();

        await page.getByTestId("aks-tab-jobs").click();
        await expect(page.getByTestId("jobs-table-body")).toBeVisible();

        await page.getByTestId("aks-tab-configmaps").click();
        await expect(page.getByTestId("configmaps-table-body")).toBeVisible();

        await page.getByTestId("aks-tab-network").click();
        await page.getByTestId("aks-tab-ingresses").click();
        await expect(page.getByTestId("ingresses-table-body")).toBeVisible();

        await page.getByTestId("aks-tab-hpa").click();
        await expect(page.getByTestId("hpas-table-body")).toBeVisible();
    });

    test("hpa rows open YAML on click and delete via the actions menu", async ({
        page,
    }) => {
        await page.goto("/aks");
        await page
            .getByTestId("aks-namespace-select")
            .selectOption("ecommerce");
        await page.getByTestId("aks-tab-hpa").click();
        await expect(page.getByTestId("hpas-table-body")).toBeVisible();
        await expect(
            page.getByTestId("hpas-table-body").locator("tr"),
        ).toHaveCount(4);

        // Overflow menu → Delete → confirm → the row is gone (demo delete persists).
        await page.getByTestId("hpa-actions-payment-gateway-hpa").click();
        await expect(page.getByTestId("aks-context-menu")).toBeVisible();
        await page.getByTestId("ctx-item-delete").click();
        await expect(page.getByTestId("aks-confirm-bar")).toBeVisible();
        await page.getByTestId("aks-confirm-yes").click();
        await expect(
            page.getByTestId("hpa-row-payment-gateway-hpa"),
        ).toHaveCount(0);

        // Row click opens the shared YAML side panel, like every other resource tab.
        await page.getByTestId("hpa-row-order-api-hpa").click();
        await expect(page.getByTestId("yaml-viewer")).toBeVisible();
    });

    test("cronjob context menu triggers a run and toggles suspend", async ({
        page,
    }) => {
        await page.goto("/aks");
        await page
            .getByTestId("aks-namespace-select")
            .selectOption("ecommerce");
        await page.getByTestId("aks-tab-cronjobs").click();
        await expect(page.getByTestId("cronjobs-table-body")).toBeVisible();

        // Trigger via the right-click menu — the demo client persists the created Job.
        await page
            .getByTestId("cronjob-row-inventory-sync")
            .click({ button: "right" });
        await expect(page.getByTestId("aks-context-menu")).toBeVisible();
        await page.getByTestId("ctx-item-trigger").click();
        await expect(page.getByTestId("notification-toasts")).toContainText(
            "inventory-sync",
        );

        // The created job is discoverable on the Jobs tab.
        await page.getByTestId("aks-tab-jobs").click();
        await expect(page.getByTestId("jobs-table-body")).toContainText(
            "inventory-sync-manual-",
        );

        // Suspend goes through the confirm bar, then the row flips to suspended.
        await page.getByTestId("aks-tab-cronjobs").click();
        await expect(page.getByTestId("cronjobs-table-body")).toBeVisible();
        await page
            .getByTestId("cronjob-row-report-generator")
            .click({ button: "right" });
        await page.getByTestId("ctx-item-suspend").click();
        await expect(page.getByTestId("aks-confirm-bar")).toBeVisible();
        await page.getByTestId("aks-confirm-yes").click();
        await expect(
            page.getByTestId("cronjob-row-report-generator"),
        ).toContainText("Yes");
    });

    test("pod detail panel opens on pod click", async ({ page }) => {
        await page.goto("/aks");
        await page
            .getByTestId("aks-namespace-select")
            .selectOption("ecommerce");

        await page.getByTestId("aks-tab-pods").click();
        await expect(page.getByTestId("pods-table-body")).toBeVisible();

        await page.getByTestId("pods-table-body").locator("tr").first().click();
        await expect(page.getByTestId("pod-detail-panel")).toBeVisible();
        await expect(page.getByTestId("pod-log-view")).toBeVisible();
    });

    test("helm detail panel opens on release click", async ({ page }) => {
        await page.goto("/aks");
        await page
            .getByTestId("aks-namespace-select")
            .selectOption("ecommerce");

        await page.getByTestId("aks-tab-helm").click();
        await expect(page.getByTestId("helm-table-body")).toBeVisible();

        await page.getByTestId("helm-table-body").locator("tr").first().click();
        await expect(page.getByTestId("helm-detail-panel")).toBeVisible();
        await expect(page.getByTestId("helm-tab-history")).toBeVisible();
        await expect(page.getByTestId("helm-tab-values")).toBeVisible();
    });

    test("surfaces the reason when namespaces cannot be listed", async ({
        page,
    }) => {
        // Regression: a failing /api/aks/namespaces used to render as an empty picker reading
        // "0 total / No namespaces found", which looks identical to a cluster with no namespaces.
        // The auth failure has to be visible instead.
        const authError =
            "The cluster rejected the request as unauthorized (HTTP 401): no valid Azure AD token could be obtained.";
        await page.route("**/api/aks/namespaces", async (route) => {
            await route.fulfill({
                status: 401,
                contentType: "application/json",
                body: JSON.stringify({ error: authError }),
            });
        });

        await page.goto("/aks");

        await expect(page.getByTestId("aks-namespace-error")).toBeVisible();

        await page.getByTestId("aks-namespace-dropdown").click();
        await expect(
            page.getByTestId("aks-namespace-error-detail"),
        ).toContainText(authError);
        await expect(page.getByText("No namespaces found")).toHaveCount(0);
    });
});
