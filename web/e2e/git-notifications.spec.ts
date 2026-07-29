import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("API Client Git Panel", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("git toggle button is visible", async ({ page }) => {
    await page.goto("/api-client");
    await expect(page.getByTestId("api-client-git-toggle")).toBeVisible();
  });

  test("git panel opens and closes", async ({ page }) => {
    await page.goto("/api-client");
    await page.getByTestId("api-client-git-toggle").click();
    await expect(page.getByTestId("api-client-git-panel")).toBeVisible();
    // In browser (non-Tauri), should show unavailable message
    await expect(page.getByTestId("git-panel-unavailable")).toBeVisible();
    await page.getByTestId("api-client-git-close").click();
    await expect(page.getByTestId("api-client-git-panel")).not.toBeVisible();
  });
});

test.describe("Notification System", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("notification bell is visible", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("notification-bell")).toBeVisible();
  });

  test("notification history opens and closes", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("notification-bell").click();
    await expect(page.getByTestId("notification-history")).toBeVisible();
    // Close by clicking bell again
    await page.getByTestId("notification-bell").click();
    await expect(page.getByTestId("notification-history")).not.toBeVisible();
  });
});
