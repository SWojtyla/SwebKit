import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Layout", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("top bar and status bar are visible", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("top-bar")).toBeVisible();
    await expect(page.getByTestId("status-bar")).toBeVisible();
    await expect(page.getByTestId("status-bar-connection")).toBeVisible();
  });

  test("status bar shows live health for each infrastructure area", async ({ page }) => {
    await page.goto("/");
    const areas = ["service-bus", "aks", "redis", "storage"];

    for (const area of areas) {
      await expect(page.getByTestId(`status-bar-health-${area}`)).toBeVisible();
    }

    await expect(page.getByTestId("status-bar-health-service-bus")).toHaveAttribute("aria-label", "Service Bus: Connected");
    await expect(page.getByTestId("status-bar-health-aks")).toHaveAttribute("aria-label", "AKS: Connected");
    await expect(page.getByTestId("status-bar-health-redis")).toHaveAttribute("aria-label", "Redis: Connected");
    await expect(page.getByTestId("status-bar-health-storage")).toHaveAttribute("aria-label", "Storage: Connected");
  });

  test("command palette opens via trigger button", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("command-palette-trigger").click();
    await expect(page.getByTestId("command-palette")).toBeVisible();
    await expect(page.getByTestId("command-palette-input")).toBeVisible();
    // Should show all commands
    await expect(page.getByTestId("command-palette-item-dashboard")).toBeVisible();
    await expect(page.getByTestId("command-palette-item-aks")).toBeVisible();
  });

  test("command palette navigates to selected page", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("command-palette-trigger").click();
    await page.getByTestId("command-palette-item-redis").click();
    await expect(page).toHaveURL(/\/redis$/);
  });

  test("command palette closes on Escape", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("command-palette-trigger").click();
    await expect(page.getByTestId("command-palette")).toBeVisible();
    // Focus the input first so it receives the Escape key
    await page.getByTestId("command-palette-input").focus();
    await page.keyboard.press("Escape");
    await expect(page.getByTestId("command-palette")).not.toBeVisible();
  });

  test("command palette opens with Ctrl+K", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("top-bar").waitFor();
    // Dispatch on document so the window listener receives the bubbling event.
    await page.evaluate(() => {
      document.dispatchEvent(new KeyboardEvent("keydown", { key: "k", ctrlKey: true, bubbles: true }));
    });
    await expect(page.getByTestId("command-palette")).toBeVisible();
  });

  test("command palette filters commands by search", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("command-palette-trigger").click();
    await page.getByTestId("command-palette-input").fill("redis");
    await expect(page.getByTestId("command-palette-item-redis")).toBeVisible();
    await expect(page.getByTestId("command-palette-item-dashboard")).not.toBeVisible();
  });
});
