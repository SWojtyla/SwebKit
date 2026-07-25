import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Storage", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("displays containers and selects one to show blobs", async ({ page }) => {
    await page.goto("/storage");

    await expect(page.getByTestId("storage-container-list")).toBeVisible();
    await expect(page.getByTestId("storage-container-configs")).toBeVisible();
    await expect(page.getByTestId("storage-container-exports")).toBeVisible();
    await expect(page.getByTestId("storage-container-fixtures")).toBeVisible();

    await page.getByTestId("storage-container-configs").click();
    await expect(page.getByTestId("storage-blob-browser")).toBeVisible();
    await expect(page.getByTestId("storage-item-app-settings.json")).toBeVisible();
    await expect(page.getByTestId("storage-item-feature-flags.json")).toBeVisible();
  });

  test("shows blob detail with properties and content", async ({ page }) => {
    await page.goto("/storage");

    await page.getByTestId("storage-container-configs").click();
    await page.getByTestId("storage-item-app-settings.json").click();

    await expect(page.getByTestId("storage-blob-name")).toHaveText("app-settings.json");
    await expect(page.getByTestId("storage-blob-type")).toHaveText("application/json");
    await expect(page.getByTestId("storage-blob-content")).toContainText("Logging");
  });

  test("navigates into virtual folder and back via breadcrumb", async ({ page }) => {
    await page.goto("/storage");

    await page.getByTestId("storage-container-configs").click();
    await expect(page.getByTestId("storage-item-env/")).toBeVisible();

    // Navigate into env/ folder
    await page.getByTestId("storage-item-env/").click();
    await expect(page.getByTestId("storage-item-env/prod.json")).toBeVisible();
    await expect(page.getByTestId("storage-item-env/staging.json")).toBeVisible();

    // Navigate back via breadcrumb
    await page.getByTestId("storage-breadcrumb-0").click();
    await expect(page.getByTestId("storage-item-app-settings.json")).toBeVisible();
    await expect(page.getByTestId("storage-item-env/prod.json")).not.toBeVisible();
  });

  test("shows CSV content in exports container", async ({ page }) => {
    await page.goto("/storage");

    await page.getByTestId("storage-container-exports").click();
    await page.getByTestId("storage-item-2026-03-21-report.csv").click();

    await expect(page.getByTestId("storage-blob-name")).toHaveText("2026-03-21-report.csv");
    await expect(page.getByTestId("storage-blob-type")).toHaveText("text/csv");
    await expect(page.getByTestId("storage-blob-content")).toContainText("OrderId");
  });

  test("shows metadata table when blob has metadata", async ({ page }) => {
    await page.goto("/storage");

    await page.getByTestId("storage-container-configs").click();
    await page.getByTestId("storage-item-app-settings.json").click();

    // Demo blobs have metadata key "demo" = "true"
    const metadataSection = page.locator("text=Metadata");
    await expect(metadataSection).toBeVisible();
  });
});
