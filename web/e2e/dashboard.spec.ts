import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Dashboard", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("loads and shows sidecar connection", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("dashboard-title")).toHaveText("Dashboard");
    await expect(page.getByTestId("sidecar-status-text")).toContainText("Connected");
  });

  test("navigates to AKS and Settings from dashboard", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("service-card-aks").click();
    await expect(page).toHaveURL(/\/aks$/);
    await page.getByTestId("nav-dashboard").click();
    await expect(page).toHaveURL(/\/$/);
    await page.getByTestId("settings-quick-link").click();
    await expect(page).toHaveURL(/\/settings$/);
  });

  test("toggles demo mode and updates service bus namespace count", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("service-card-service-bus")).toContainText("0 namespaces");
    await setDemoMode(page, true);
    await page.goto("/");
    await expect(page.getByTestId("service-card-service-bus")).toContainText("2 namespaces");
  });
});
