import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("AKS deferred features", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("multi-pod logs button opens panel", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");
    await expect(page.getByTestId("aks-multi-pod-logs")).toBeVisible();
    await page.getByTestId("aks-multi-pod-logs").click();
    await expect(page.getByTestId("multi-pod-log-view")).toBeVisible();
  });

  test("multi-pod logs stream every pod by default and share the log toolbar", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");
    await page.getByTestId("aks-multi-pod-logs").click();
    await expect(page.getByTestId("multi-pod-log-view")).toBeVisible();

    // The view used to open with every pod switched off — a correlation view that
    // correlated nothing until each pod was clicked.
    const toggles = page.locator('[data-testid^="multi-pod-toggle-"]');
    await expect(toggles.first()).toBeVisible();
    const count = await toggles.count();
    for (let i = 0; i < count; i++) {
      await expect(toggles.nth(i)).toHaveClass(/bg-primary/);
    }

    // Logs arrive without any further interaction, and the toolbar the single-pod view
    // has always had is now present here too.
    await expect(page.getByTestId("multi-pod-log-output")).not.toContainText(
      "Select pods to start streaming logs",
      { timeout: 15_000 },
    );
    await expect(page.getByTestId("log-filter-input")).toBeVisible();
    await expect(page.getByTestId("log-export-btn")).toBeVisible();
    await expect(page.getByTestId("log-timestamp-select")).toBeVisible();

    // Filtering narrows the merged output rather than only one pod's.
    await page.getByTestId("log-filter-input").fill("zzz-no-such-line");
    await expect(page.getByTestId("log-line-count")).toContainText("0 lines");
  });

  test("yaml viewer edit mode toggle", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");
    await page.getByTestId("aks-tab-pods").click();
    await page.getByTestId("pods-table-body").locator("tr").first().click();
    await expect(page.getByTestId("pod-detail-panel")).toBeVisible();
    await page.getByTestId("pod-yaml-btn").click();
    await expect(page.getByTestId("yaml-viewer")).toBeVisible();
    await page.getByTestId("yaml-edit-toggle").click();
    await expect(page.getByTestId("yaml-editor")).toBeVisible();
  });

  test("helm rollback is disabled pending a sidecar endpoint", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");
    await page.getByTestId("aks-tab-helm").click();
    await page.getByTestId("helm-table-body").locator("tr").first().click();
    await expect(page.getByTestId("helm-detail-panel")).toBeVisible();
    const rollbackButtons = page.locator('[data-testid^="helm-rollback-rev-"]');
    const count = await rollbackButtons.count();
    for (let i = 0; i < count; i++) {
      await expect(rollbackButtons.nth(i)).toBeDisabled();
    }
  });

  test("keyboard shortcut r refreshes data", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");
    await expect(page.getByTestId("aks-page")).toBeVisible();
    await page.keyboard.press("r");
  });
});
