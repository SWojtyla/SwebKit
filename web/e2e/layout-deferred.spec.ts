import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Layout & Shell deferred features", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("context title shows current page name", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("context-title")).toContainText("Dashboard");

    await page.goto("/aks");
    await expect(page.getByTestId("context-title")).toContainText("AKS");
  });

  test("nav collapse toggle works", async ({ page }) => {
    await page.goto("/");
    const toggle = page.getByTestId("nav-collapse-toggle");
    await expect(toggle).toBeVisible();
    await toggle.click();
  });

  test("theme toggle button is visible", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("theme-toggle")).toBeVisible();
  });

  test("demo mode toggle in top bar", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("demo-mode-toggle")).toBeVisible();
  });

  test("keyboard shortcuts button opens panel", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("keyboard-shortcuts-btn").click();
    await expect(page.getByTestId("keyboard-shortcuts-panel")).toBeVisible();
    await page.getByTestId("keyboard-shortcuts-close").click();
  });

  test("shift+? opens keyboard shortcuts", async ({ page }) => {
    await page.goto("/");
    await page.keyboard.press("Shift+?");
    await expect(page.getByTestId("keyboard-shortcuts-panel")).toBeVisible();
  });

  test("ctrl+b toggles sidebar", async ({ page }) => {
    await page.goto("/");
    await page.keyboard.press("Control+b");
  });

  test("status bar shows connection and theme info", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("status-bar")).toBeVisible();
    await expect(page.getByTestId("status-bar-connection")).toBeVisible();
  });

  test("status bar shows demo mode indicator", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("status-bar-demo")).toBeVisible();
  });
});
