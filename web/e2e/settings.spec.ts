import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Settings", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("all tabs are visible and switch content", async ({ page }) => {
    await page.goto("/settings");
    await expect(page.getByTestId("settings-title")).toHaveText("Settings");

    const tabs = ["general", "service-bus", "aks", "redis", "storage", "agent", "devops", "diagnostics", "appearance"];
    for (const id of tabs) {
      await page.getByTestId(`settings-tab-${id}`).click();
      await expect(page.getByTestId("settings-content")).toBeVisible();
    }
  });

  test("general tab shows getting started readiness checklist", async ({ page }) => {
    await page.goto("/settings");
    await expect(page.getByTestId("getting-started-checklist")).toBeVisible();
    await expect(page.getByTestId("getting-started-aks")).toBeVisible();
    await expect(page.getByTestId("getting-started-service-bus")).toBeVisible();
    await expect(page.getByTestId("getting-started-redis")).toBeVisible();
    await expect(page.getByTestId("getting-started-storage")).toBeVisible();
  });

  test("devops tab shows configuration form", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-devops").click();
    await expect(page.getByTestId("devops-settings")).toBeVisible();
    await expect(page.getByTestId("devops-org-url")).toBeVisible();
    await expect(page.getByTestId("devops-pat")).toBeVisible();
    await expect(page.getByTestId("devops-project")).toBeVisible();
  });

  test("diagnostics tab shows health and logs", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-diagnostics").click();
    await expect(page.getByTestId("diagnostics-settings")).toBeVisible();
    await expect(page.getByTestId("diag-sidecar-status")).toBeVisible();
    await expect(page.getByTestId("diag-log-viewer")).toBeVisible();
  });

  test("appearance tab shows theme and font options", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-appearance").click();
    await expect(page.getByTestId("appearance-settings")).toBeVisible();
    await expect(page.getByTestId("appearance-theme-dark")).toBeVisible();
    await expect(page.getByTestId("appearance-font-size")).toBeVisible();
    await expect(page.getByTestId("appearance-density")).toBeVisible();
  });
});
