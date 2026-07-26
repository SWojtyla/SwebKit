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

  test("shows health tiles for each service", async ({ page }) => {
    await setDemoMode(page, true);
    await page.goto("/");
    await expect(page.getByTestId("health-tiles")).toBeVisible();
    await expect(page.getByTestId("health-tile-service-bus")).toBeVisible();
    await expect(page.getByTestId("health-tile-aks")).toBeVisible();
    await expect(page.getByTestId("health-tile-redis")).toBeVisible();
    await expect(page.getByTestId("health-tile-storage")).toBeVisible();
  });

  test("shows watch tiles with metrics", async ({ page }) => {
    await setDemoMode(page, true);
    await page.goto("/");
    await expect(page.getByTestId("watch-tiles")).toBeVisible();
    await expect(page.getByTestId("watch-tile-deployments")).toBeVisible();
    await expect(page.getByTestId("watch-tile-pods")).toBeVisible();
    await expect(page.getByTestId("watch-tile-containers")).toBeVisible();
    await expect(page.getByTestId("watch-tile-cache-hit-rate")).toBeVisible();
  });
});
