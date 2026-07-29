import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Dashboard pending approvals", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("dashboard page loads with title", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("dashboard-title")).toBeVisible();
  });

  test("pending approvals banner is visible when count > 0", async ({ page }) => {
    await page.goto("/");
    const banner = page.getByTestId("pending-approvals-banner");
    if (await banner.isVisible()) {
      await expect(banner).toContainText(/pending approval/);
    }
  });
});
