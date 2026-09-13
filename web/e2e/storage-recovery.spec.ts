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

  test("recovering a deleted blob requires confirmation (unit 6.3)", async ({ page }) => {
    // Regression: Recover used to fire immediately with no confirmation and no explanation
    // of what happens on a same-name collision with a live blob.
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    await page.getByTestId("storage-view-recovery").click();
    await expect(page.getByTestId("blob-recovery-panel")).toBeVisible();

    await page.getByTestId("blob-recover-btn-deleted-config.json").click();
    await expect(page.getByTestId("blob-recover-confirm")).toBeVisible();

    await page.getByTestId("blob-recover-confirm-yes").click();
    await expect(page.getByTestId("blob-recover-confirm")).not.toBeVisible();
    // "deleted-config.json" is the only seeded deleted blob in "configs" — once recovery
    // succeeds and the deleted-blobs query invalidates, the table empties out entirely
    // (rather than asserting the transient local "Recovered" label, which can lose the
    // race against that same invalidation removing the row first).
    await expect(page.getByTestId("blob-recovery-no-results")).toBeVisible();
  });
});
