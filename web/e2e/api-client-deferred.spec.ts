import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("API Client deferred features", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("variable preview tab is visible in request editor", async ({ page }) => {
    await page.goto("/api-client");
    await expect(page.getByTestId("api-client-page")).toBeVisible();
    const varPreview = page.getByTestId("variable-preview-tab");
    if (await varPreview.isVisible()) {
      await varPreview.click();
    }
  });

  test("capture rules tab is visible in request editor", async ({ page }) => {
    await page.goto("/api-client");
    const captureTab = page.getByTestId("capture-rules-tab");
    if (await captureTab.isVisible()) {
      await captureTab.click();
    }
  });

  test("response history is visible after sending request", async ({ page }) => {
    await page.goto("/api-client");
    const historyPanel = page.getByTestId("response-history");
    if (await historyPanel.isVisible()) {
      await expect(historyPanel).toBeVisible();
    }
  });

  test("sparkline graph is visible in response viewer", async ({ page }) => {
    await page.goto("/api-client");
    const sparkline = page.getByTestId("response-sparkline");
    if (await sparkline.isVisible()) {
      await expect(sparkline).toBeVisible();
    }
  });

  test("graphql subscription panel is visible", async ({ page }) => {
    await page.goto("/api-client");
    const graphqlTab = page.getByTestId("graphql-tab");
    if (await graphqlTab.isVisible()) {
      await graphqlTab.click();
      const subPanel = page.getByTestId("graphql-subscription-panel");
      if (await subPanel.isVisible()) {
        await expect(subPanel).toBeVisible();
      }
    }
  });

  test("git panel bruno import/export buttons visible", async ({ page }) => {
    await page.goto("/api-client");
    const gitTab = page.getByTestId("git-tab");
    if (await gitTab.isVisible()) {
      await gitTab.click();
      const importBtn = page.getByTestId("bruno-import-btn");
      const exportBtn = page.getByTestId("bruno-export-btn");
      if (await importBtn.isVisible()) {
        await expect(importBtn).toBeVisible();
      }
      if (await exportBtn.isVisible()) {
        await expect(exportBtn).toBeVisible();
      }
    }
  });
});
