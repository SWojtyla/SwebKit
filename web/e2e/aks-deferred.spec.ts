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
    // has always had is now present here too. Checking line count rather than just the
    // absence of the "Select pods..." empty-state string matters: that string is also
    // absent while merely "Connecting..." with zero lines ever delivered — which is exactly
    // the state every multi-pod stream used to get stuck in (a 400 from a missing required
    // query parameter, invisible to this weaker assertion).
    await expect(page.getByTestId("log-line-count")).not.toContainText("0 lines", { timeout: 15_000 });
    await expect(page.getByTestId("multi-pod-log-error")).toHaveCount(0);
    await expect(page.getByTestId("log-filter-input")).toBeVisible();
    await expect(page.getByTestId("log-export-btn")).toBeVisible();
    await expect(page.getByTestId("log-timestamp-select")).toBeVisible();

    // Filtering narrows the merged output rather than only one pod's.
    await page.getByTestId("log-filter-input").fill("zzz-no-such-line");
    await expect(page.getByTestId("log-line-count")).toContainText("0 lines");
  });

  // Multi-pod previously had no time-range control at all — every stream just pulled a flat
  // tail of 100 lines with no way to widen or narrow the window, unlike single-pod's dropdown.
  test("multi-pod logs have a time-range selector that restarts the streams", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");
    await page.getByTestId("aks-multi-pod-logs").click();
    await expect(page.getByTestId("multi-pod-log-view")).toBeVisible();

    const rangeSelect = page.getByTestId("multi-pod-range-select");
    await expect(rangeSelect).toBeVisible();
    await expect(rangeSelect).toHaveValue("5m");
    // "Previous container" is a single-pod-only concept — a pod's own previous instance
    // isn't something that correlates across multiple pods.
    await expect(rangeSelect.locator("option")).toHaveCount(4);

    await expect(page.getByTestId("log-line-count")).not.toContainText("0 lines", { timeout: 15_000 });

    // Demo mode correlates every pod in the namespace by default (~19 here), which alone
    // saturates the browser's six-connections-per-origin cap (docs/pitfalls/react-frontend.md).
    // Deselect down to two before exercising a reconnect, so the range-change assertion below
    // isn't itself racing that unrelated, pre-existing limit.
    const toggles = page.locator('[data-testid^="multi-pod-toggle-"]');
    const toggleCount = await toggles.count();
    for (let i = toggleCount - 1; i >= 2; i--) {
      await toggles.nth(i).click();
    }

    // Changing the range is a filter change: it tears down and reopens every stream, and
    // logs still arrive afterward (demo mode can't exercise the real-cluster container
    // ambiguity this range selector shipped alongside, but it does prove the range change
    // itself doesn't break streaming).
    await page.getByTestId("log-clear-btn").click();
    await expect(page.getByTestId("log-line-count")).toContainText("0 lines");
    await rangeSelect.selectOption("1h");
    await expect(page.getByTestId("log-line-count")).not.toContainText("0 lines", { timeout: 15_000 });
    await expect(page.getByTestId("multi-pod-log-error")).toHaveCount(0);
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
