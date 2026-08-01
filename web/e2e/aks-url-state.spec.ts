import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("AKS URL state", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("pods grid shows CPU and Memory columns with values in demo mode", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");
    await page.getByTestId("aks-tab-pods").click();

    const table = page.getByTestId("pods-table-body");
    await expect(table).toBeVisible();
    const firstRow = table.locator("tr").first();
    await expect(firstRow).toBeVisible();

    // Column headers should be present.
    const headers = page.locator('table thead th');
    await expect(headers.filter({ hasText: "CPU" })).toBeVisible();
    await expect(headers.filter({ hasText: "Memory" })).toBeVisible();

    const cells = firstRow.locator("td");
    // With a single namespace selected, expected columns:
    // Name, Status, Ready, CPU, Memory, Restarts, Node, Age, Actions
    const cpuCell = cells.nth(3);
    const memoryCell = cells.nth(4);

    await expect(cpuCell).toBeVisible();
    await expect(memoryCell).toBeVisible();

    const cpuText = await cpuCell.textContent() ?? "";
    const memoryText = await memoryCell.textContent() ?? "";

    expect(cpuText).toMatch(/\d+m$/);
    expect(memoryText).toMatch(/\d+Mi$/);

    // Each metric cell should contain a colored progress bar.
    await expect(cpuCell.locator("div > div").first()).toBeVisible();
    await expect(memoryCell.locator("div > div").first()).toBeVisible();
  });

  test("pod click and YAML view update the URL and are restored after reload", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");
    await expect(page).toHaveURL(/ns=ecommerce/);
    await page.getByTestId("aks-tab-pods").click();

    const table = page.getByTestId("pods-table-body");
    await expect(table).toBeVisible();
    const firstRow = table.locator("tr").first();
    await expect(firstRow).toBeVisible();
    await firstRow.click();

    await expect(page.getByTestId("pod-detail-panel")).toBeVisible();
    await expect(page).toHaveURL(/tab=pods/);
    await expect(page).toHaveURL(/pod=ecommerce%2F/);

    await page.getByTestId("pod-yaml-btn").click();
    await expect(page.getByTestId("yaml-viewer")).toBeVisible();
    await expect(page).toHaveURL(/yaml=pod%3Aecommerce%2F/);
    await expect(page).not.toHaveURL(/&pod=/);

    // Deep link: reload should restore the YAML viewer.
    const currentUrl = page.url();
    await page.reload();
    await expect(page.getByTestId("yaml-viewer")).toBeVisible();
    await expect(page).toHaveURL(/yaml=pod%3Aecommerce%2F/);
    expect(page.url()).toBe(currentUrl);

    // Back/forward between pod detail and YAML.
    await page.goBack();
    await expect(page.getByTestId("pod-detail-panel")).toBeVisible();
    await expect(page).toHaveURL(/pod=ecommerce%2F/);
    await expect(page).not.toHaveURL(/yaml=/);

    await page.goForward();
    await expect(page.getByTestId("yaml-viewer")).toBeVisible();
    await expect(page).toHaveURL(/yaml=pod%3Aecommerce%2F/);
  });

  test("namespace and tab selection sync to URL and survive reload", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");

    await expect(page).toHaveURL(/ns=ecommerce/);

    await page.getByTestId("aks-tab-pods").click();
    await expect(page.getByTestId("pods-table-body")).toBeVisible();
    await expect(page).toHaveURL(/tab=pods/);

    await page.reload();
    await expect(page.getByTestId("pods-table-body")).toBeVisible();
    await expect(page).toHaveURL(/ns=ecommerce/);
    await expect(page).toHaveURL(/tab=pods/);
  });
});
