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

  test("browser mode names the desktop app, not a generic failure", async ({ page }) => {
    await page.goto("/api-client");
    await page.getByTestId("api-client-git-toggle").click();
    // The old panel showed this text for *every* failure, masking real errors.
    await expect(page.getByTestId("git-panel-unavailable")).toContainText("desktop app");
  });

  test("the fake Bruno import/export buttons are gone", async ({ page }) => {
    await page.goto("/api-client");
    await page.getByTestId("api-client-git-toggle").click();
    // Both reported success while doing nothing real.
    await expect(page.getByTestId("git-reimport-bruno")).toHaveCount(0);
    await expect(page.getByTestId("git-export-bruno")).toHaveCount(0);
  });

  test("drawer is a real dialog: backdrop, Escape, focus return", async ({ page }) => {
    await page.goto("/api-client");
    const toggle = page.getByTestId("api-client-git-toggle");
    await toggle.click();

    const drawer = page.getByTestId("api-client-git-panel");
    await expect(drawer).toHaveAttribute("role", "dialog");
    await expect(drawer).toHaveAttribute("aria-label", "Git");
    await expect(page.getByTestId("api-client-git-backdrop")).toBeVisible();

    await page.keyboard.press("Escape");
    await expect(drawer).not.toBeVisible();
    await expect(toggle).toBeFocused();
  });

  test("clicking the backdrop closes the drawer", async ({ page }) => {
    await page.goto("/api-client");
    await page.getByTestId("api-client-git-toggle").click();
    await page.getByTestId("api-client-git-backdrop").click();
    await expect(page.getByTestId("api-client-git-panel")).not.toBeVisible();
  });

  test("drawer leaves the app status bar visible", async ({ page }) => {
    await page.goto("/api-client");
    await page.getByTestId("api-client-git-toggle").click();

    // The previous fixed overlay covered the titlebar and status bar outright.
    const drawer = await page.getByTestId("api-client-git-panel").boundingBox();
    const viewport = page.viewportSize();
    expect(drawer).not.toBeNull();
    expect(viewport).not.toBeNull();
    expect(drawer!.y).toBeGreaterThan(0);
    expect(drawer!.y + drawer!.height).toBeLessThan(viewport!.height);
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
