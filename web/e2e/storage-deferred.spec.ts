import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Storage deferred features", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("upload toggle shows upload panel", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    await expect(page.getByTestId("storage-upload-toggle")).toBeVisible();
    await page.getByTestId("storage-upload-toggle").click();
    await expect(page.getByTestId("storage-upload-panel")).toBeVisible();
  });

  test("blob detail tabs are visible", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    // A named blob, not "first item": the configs container also lists the
    // env/ virtual folder, and clicking that navigates instead of opening detail.
    await page.getByTestId("storage-item-app-settings.json").click();
    await expect(page.getByTestId("storage-blob-tab-properties")).toBeVisible();
    await expect(page.getByTestId("storage-blob-tab-versions")).toBeVisible();
    await expect(page.getByTestId("storage-blob-tab-content")).toBeVisible();
  });

  test("sas url button is visible", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    // A named blob, not "first item": the configs container also lists the
    // env/ virtual folder, and clicking that navigates instead of opening detail.
    await page.getByTestId("storage-item-app-settings.json").click();
    await expect(page.getByTestId("storage-sas-url-btn")).toBeVisible();
  });

  test("sas url display toggles on click", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    // A named blob, not "first item": the configs container also lists the
    // env/ virtual folder, and clicking that navigates instead of opening detail.
    await page.getByTestId("storage-item-app-settings.json").click();
    await page.getByTestId("storage-sas-url-btn").click();
    await expect(page.getByTestId("storage-sas-url-display")).toBeVisible();
  });

  test("copy blob dialog opens", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    // A named blob, not "first item": the configs container also lists the
    // env/ virtual folder, and clicking that navigates instead of opening detail.
    await page.getByTestId("storage-item-app-settings.json").click();
    await page.getByTestId("storage-copy-blob-btn").click();
    await expect(page.getByTestId("storage-copy-dialog")).toBeVisible();
  });

  test("versions tab shows version list", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    // A named blob, not "first item": the configs container also lists the
    // env/ virtual folder, and clicking that navigates instead of opening detail.
    await page.getByTestId("storage-item-app-settings.json").click();
    await page.getByTestId("storage-blob-tab-versions").click();
    await expect(page.getByTestId("storage-blob-versions")).toBeVisible();
  });

  test("content tab shows content preview", async ({ page }) => {
    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    // A named blob, not "first item": the configs container also lists the
    // env/ virtual folder, and clicking that navigates instead of opening detail.
    await page.getByTestId("storage-item-app-settings.json").click();
    await page.getByTestId("storage-blob-tab-content").click();
    await expect(page.getByTestId("storage-blob-content-tab")).toBeVisible();
  });
});
