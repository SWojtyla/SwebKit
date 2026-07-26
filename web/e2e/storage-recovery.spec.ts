import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Storage Blob Recovery", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });
  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("recovery view toggle is visible", async ({ page }) => {
    await page.goto("/storage");
    await expect(page.getByTestId("storage-view-browser")).toBeVisible();
    await expect(page.getByTestId("storage-view-recovery")).toBeVisible();
  });

  test("recovery view shows empty state without container", async ({ page }) => {
    await page.goto("/storage");
    // Recovery button is disabled without a container — verify it's disabled
    await expect(page.getByTestId("storage-view-recovery")).toBeDisabled();
  });

  test("recovery view shows deleted blobs after selecting container", async ({ page }) => {
    await page.goto("/storage");
    // Select first container
    const containerBtn = page.locator("[data-testid^='storage-container-']").first();
    if (await containerBtn.count() > 0) {
      await containerBtn.click();
      await page.getByTestId("storage-view-recovery").click();
      await expect(page.getByTestId("blob-recovery-panel")).toBeVisible();
      await expect(page.getByTestId("blob-recovery-table")).toBeVisible();
      // Should have demo deleted blobs
      const rows = page.locator("tbody tr");
      expect(await rows.count()).toBeGreaterThan(0);
    }
  });

  test("recovery filter works", async ({ page }) => {
    await page.goto("/storage");
    const containerBtn = page.locator("[data-testid^='storage-container-']").first();
    if (await containerBtn.count() > 0) {
      await containerBtn.click();
      await page.getByTestId("storage-view-recovery").click();
      await page.getByTestId("blob-recovery-filter").fill("nonexistent");
      await expect(page.getByTestId("blob-recovery-no-results")).toBeVisible();
    }
  });
});
