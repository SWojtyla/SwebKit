import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Settings", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("all tabs are visible and switch content", async ({ page }) => {
    await page.goto("/settings");
    await expect(page.getByTestId("settings-title")).toHaveText("Settings");

    const tabs = ["general", "service-bus", "aks", "redis", "storage", "agent"];
    for (const id of tabs) {
      await page.getByTestId(`settings-tab-${id}`).click();
      await expect(page.getByTestId("settings-content")).toBeVisible();
    }
  });
});
