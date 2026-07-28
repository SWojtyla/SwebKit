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
    await page.getByTestId("storage-blob-tab-content").click();
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
    await page.getByTestId("storage-blob-tab-content").click();
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

  test("metadata edits persist after reload", async ({ page }) => {
    await page.goto("/storage");

    await page.getByTestId("storage-container-configs").click();
    await page.getByTestId("storage-item-app-settings.json").click();

    await expect(page.getByTestId("storage-metadata-edit-btn")).toBeVisible();
    await page.getByTestId("storage-metadata-edit-btn").click();
    await expect(page.getByTestId("storage-metadata-editor")).toBeVisible();

    // Add a new metadata key (default name is "new-key"; the key input is read-only).
    await page.getByTestId("storage-metadata-add-key").click();

    // The editor starts with the existing "demo" key (inputs 0/1), then the new "new-key" row (inputs 2/3).
    const newKeyValueInput = page.locator("[data-testid='storage-metadata-editor'] input").nth(3);
    await newKeyValueInput.fill("e2e-persist-value");

    await page.getByTestId("storage-metadata-save").click();

    // The metadata table should now show the new key/value.
    await expect(page.getByText("new-key")).toBeVisible();
    await expect(page.getByText("e2e-persist-value")).toBeVisible();

    // Reload and navigate back to the same blob; the edit must still be present.
    await page.reload();
    await page.getByTestId("storage-container-configs").click();
    await page.getByTestId("storage-item-app-settings.json").click();
    await expect(page.getByText("new-key")).toBeVisible();
    await expect(page.getByText("e2e-persist-value")).toBeVisible();
  });

  test("blob filter narrows the list", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-exports").click();
    await expect(page.getByTestId("storage-blob-filter")).toBeVisible();
    await page.getByTestId("storage-blob-filter").fill("report");
    // Wait for filtered items to appear
    await expect(page.getByTestId("storage-item-2026-03-21-report.csv")).toBeVisible();
    // Verify not all items are shown (archive/ prefix should be filtered out)
    await expect(page.getByTestId("storage-item-archive/")).not.toBeVisible();
  });

  test("copy URL and download buttons are visible", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-exports").click();
    await page.getByTestId("storage-item-2026-03-21-report.csv").click();
    await expect(page.getByTestId("storage-copy-url-btn")).toBeVisible();
    await expect(page.getByTestId("storage-download-btn")).toBeVisible();
  });

  test("multi-select mode shows checkboxes", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-exports").click();
    await page.getByTestId("storage-multi-select-toggle").click();
    await expect(page.getByTestId("storage-multi-select-toggle")).toHaveText("Exit Multi");
    const checkbox = page.locator("[data-testid^='storage-blob-checkbox-']").first();
    await expect(checkbox).toBeVisible();
    await page.getByTestId("storage-multi-select-toggle").click();
    await expect(page.getByTestId("storage-multi-select-toggle")).toHaveText("Multi-Select");
  });

  test("metadata editor can be opened", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    await page.getByTestId("storage-item-app-settings.json").click();
    await expect(page.getByTestId("storage-metadata-edit-btn")).toBeVisible();
    await page.getByTestId("storage-metadata-edit-btn").click();
    await expect(page.getByTestId("storage-metadata-editor")).toBeVisible();
    await expect(page.getByTestId("storage-metadata-add-key")).toBeVisible();
  });

  test("uploads a file through the dropzone", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    await page.getByTestId("storage-upload-toggle").click();

    await page.getByTestId("storage-upload-file").setInputFiles({
      name: "e2e-upload.json",
      mimeType: "application/json",
      buffer: Buffer.from('{"source":"e2e"}'),
    });
    await expect(page.getByTestId("storage-upload-name")).toHaveValue("e2e-upload.json");
    await page.getByTestId("storage-upload-confirm").click();

    await expect(page.getByTestId("storage-item-e2e-upload.json")).toBeVisible();
  });

  test("compares and restores blob versions", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    await page.getByTestId("storage-item-app-settings.json").click();
    await page.getByTestId("storage-blob-tab-versions").click();

    await page.getByTestId("storage-version-base").selectOption({ index: 1 });
    await page.getByTestId("storage-version-compare-btn").click();
    await expect(page.getByTestId("storage-version-diff-pane")).toBeVisible();
    await expect(page.getByTestId("storage-version-text-diff")).toContainText("version: 2");

    await page.getByTestId("storage-version-restore-2026-03-15T08:30:00Z").click();
    await expect(page.getByTestId("storage-version-restore-confirm")).toBeVisible();
    await page.getByTestId("storage-version-restore-confirm-yes").click();
    await expect(page.getByTestId("storage-version-restore-confirm")).not.toBeVisible();
  });

  test("uses a container picker and guards overwrite copies", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    await page.getByTestId("storage-item-app-settings.json").click();
    await page.getByTestId("storage-copy-blob-btn").click();

    await expect(page.getByTestId("storage-copy-dest-container")).toHaveValue("configs");
    await page.getByTestId("storage-copy-dest-container").selectOption("exports");
    await page.getByTestId("storage-copy-dest-blob").fill("e2e-copy.json");
    await page.getByTestId("storage-copy-overwrite").check();
    await page.getByTestId("storage-copy-confirm").click();

    await expect(page.getByTestId("storage-copy-overwrite-confirm")).toBeVisible();
    await expect(page.getByTestId("storage-copy-overwrite-confirm-yes")).toBeDisabled();
    await page.getByTestId("storage-copy-overwrite-confirm-name").fill("exports/e2e-copy.json");
    await page.getByTestId("storage-copy-overwrite-confirm-yes").click();
    await expect(page.getByTestId("storage-copy-status")).toHaveText("Copied successfully");
  });
});
