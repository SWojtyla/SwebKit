import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("AKS", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("selects namespace and displays deployments", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");

    await expect(page.getByTestId("deployments-table-body")).toBeVisible();
    await expect(page.getByTestId("deployments-table-body").locator("tr")).toHaveCount(10);
    await expect(page.getByTestId("deployment-row-order-api")).toBeVisible();
  });

  test("switches between resource tabs", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");

    await page.getByTestId("aks-tab-pods").click();
    await expect(page.getByTestId("pods-table-body")).toBeVisible();

    await page.getByTestId("aks-tab-network").click();
    await page.getByTestId("aks-tab-services").click();
    await expect(page.getByTestId("services-table-body")).toBeVisible();

    await page.getByTestId("aks-tab-helm").click();
    await expect(page.getByTestId("helm-table-body")).toBeVisible();

    await page.getByTestId("aks-tab-secrets").click();
    await expect(page.getByTestId("secrets-table-body")).toBeVisible();

    await page.getByTestId("aks-tab-events").click();
    await expect(page.getByTestId("events-list")).toBeVisible();
  });

  test("new resource tabs are visible and functional", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");

    await page.getByTestId("aks-tab-statefulsets").click();
    await expect(page.getByTestId("statefulsets-table-body")).toBeVisible();

    await page.getByTestId("aks-tab-cronjobs").click();
    await expect(page.getByTestId("cronjobs-table-body")).toBeVisible();

    await page.getByTestId("aks-tab-jobs").click();
    await expect(page.getByTestId("jobs-table-body")).toBeVisible();

    await page.getByTestId("aks-tab-configmaps").click();
    await expect(page.getByTestId("configmaps-table-body")).toBeVisible();

    await page.getByTestId("aks-tab-network").click();
    await page.getByTestId("aks-tab-ingresses").click();
    await expect(page.getByTestId("ingresses-table-body")).toBeVisible();

    await page.getByTestId("aks-tab-hpa").click();
    await expect(page.getByTestId("hpas-table-body")).toBeVisible();
  });

  test("pod detail panel opens on pod click", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");

    await page.getByTestId("aks-tab-pods").click();
    await expect(page.getByTestId("pods-table-body")).toBeVisible();

    await page.getByTestId("pods-table-body").locator("tr").first().click();
    await expect(page.getByTestId("pod-detail-panel")).toBeVisible();
    await expect(page.getByTestId("pod-log-view")).toBeVisible();
  });

  test("helm detail panel opens on release click", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption("ecommerce");

    await page.getByTestId("aks-tab-helm").click();
    await expect(page.getByTestId("helm-table-body")).toBeVisible();

    await page.getByTestId("helm-table-body").locator("tr").first().click();
    await expect(page.getByTestId("helm-detail-panel")).toBeVisible();
    await expect(page.getByTestId("helm-tab-history")).toBeVisible();
    await expect(page.getByTestId("helm-tab-values")).toBeVisible();
  });
});
