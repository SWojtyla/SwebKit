import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Settings", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("all tabs are visible and switch content", async ({ page }) => {
    await page.goto("/settings");
    await expect(page.getByTestId("settings-title")).toHaveText("Settings");

    const tabs = ["general", "service-bus", "aks", "redis", "storage", "agent", "diagnostics", "appearance"];
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
    await expect(page.getByTestId("appearance-theme-light")).toBeVisible();
    await expect(page.getByTestId("appearance-theme-fancy")).toBeVisible();
    await expect(page.getByTestId("appearance-font-size")).toBeVisible();
    await expect(page.getByTestId("appearance-density")).toBeVisible();
  });

  test("agent profile base URL persists across reload", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-agent").click();
    await page.getByTestId("agent-add-profile").click();

    const baseUrlInput = page.getByTestId("agent-profile-base-url-0");
    await baseUrlInput.fill("http://localhost:9999/v1");
    await expect(baseUrlInput).toHaveValue("http://localhost:9999/v1");

    await page.reload();
    await page.getByTestId("settings-tab-agent").click();
    await expect(page.getByTestId("agent-profile-base-url-0")).toHaveValue("http://localhost:9999/v1");
  });

  test("test connection button reports capability from the sidecar", async ({ page }) => {
    await page.route("**/api/agent/profiles/*/test", async (route) => {
      await route.fulfill({
        json: {
          serverReachable: true,
          modelAvailable: true,
          chatValid: true,
          toolCallingValid: true,
          capability: "ToolCalling",
          diagnostic: "Tool calling supported.",
          availableModels: ["test-model"],
        },
      });
    });

    await page.goto("/settings");
    await page.getByTestId("settings-tab-agent").click();
    await page.getByTestId("agent-add-profile").click();
    await expect(page.getByTestId("agent-profile-capability-0")).toHaveText(/Not tested/);

    await page.getByTestId("agent-profile-test-0").click();
    await expect(page.getByTestId("agent-profile-capability-0")).toHaveText(/Tool calling supported/);
  });

  test("selecting the fancy theme applies it to the document", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-appearance").click();
    await page.getByTestId("appearance-theme-fancy").click();
    await expect(page.locator("html")).toHaveClass(/fancy/);
    await expect(page.locator("html")).not.toHaveClass(/dark/);

    await page.getByTestId("appearance-theme-dark").click();
    await expect(page.locator("html")).toHaveClass(/dark/);
    await expect(page.locator("html")).not.toHaveClass(/fancy/);
  });
});
