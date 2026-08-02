import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("AKS Port-Forward & Analysis", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });
  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("port-forward tab is visible and shows empty state", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption({ label: "default" });
    await page.getByTestId("aks-tab-portforward").click();
    await expect(page.getByTestId("port-forward-panel")).toBeVisible();
    await expect(page.getByTestId("port-forward-empty")).toBeVisible();
  });

  test("port-forward add form opens", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption({ label: "default" });
    await page.getByTestId("aks-tab-portforward").click();
    await page.getByTestId("port-forward-add").click();
    await expect(page.getByTestId("port-forward-form")).toBeVisible();
    await page.getByTestId("port-forward-cancel").click();
    await expect(page.getByTestId("port-forward-form")).not.toBeVisible();
  });

  test("open shell in pod opens the terminal panel and reports it needs the desktop app", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption({ label: "default" });
    await page.getByTestId("aks-tab-pods").click();
    const firstRow = page.getByTestId("pods-table-body").locator("tr").first();
    await firstRow.click({ button: "right" });
    await expect(page.getByTestId("aks-context-menu")).toBeVisible();
    await page.getByTestId("ctx-item-open-shell-in-pod").click();

    await expect(page.getByTestId("pod-shell-panel")).toBeVisible();
    // Playwright runs the app as a plain browser page, not the Tauri desktop shell, so the
    // pty/kubectl bridge isn't available — the panel should surface that gracefully instead of
    // hanging on "Connecting…" forever or throwing an unhandled error.
    await expect(page.getByTestId("pod-shell-status")).toHaveText(/desktop app/i);

    await page.getByTestId("pod-shell-close").click();
    await expect(page.getByTestId("pod-shell-panel")).not.toBeVisible();
  });

  test("analysis tab shows ingress and probe analysis", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption({ label: "default" });
    await page.getByTestId("aks-tab-analysis").click();
    await expect(page.getByTestId("aks-analysis-panel")).toBeVisible();
    await expect(page.getByTestId("aks-ingress-analysis")).toBeVisible();
    await expect(page.getByTestId("aks-probe-analysis")).toBeVisible();
    await expect(page.getByTestId("aks-quota-summary")).toBeVisible();
    await expect(page.getByTestId("aks-network-policy-summary")).toBeVisible();
  });
});
