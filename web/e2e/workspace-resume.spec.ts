import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";
import { sidecarPort } from "./test-config";

const sidecarUrl = `http://127.0.0.1:${sidecarPort}`;

async function getUserSettings(page: import("@playwright/test").Page) {
  const res = await page.request.get(`${sidecarUrl}/api/config/user-settings`);
  return (await res.json()) as Record<string, unknown>;
}

async function putUserSettings(
  page: import("@playwright/test").Page,
  patch: Record<string, unknown>,
) {
  const settings = await getUserSettings(page);
  await page.request.put(`${sidecarUrl}/api/config/user-settings`, {
    data: { ...settings, ...patch },
  });
}

test.describe("startup warm-up", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("prefetches AKS and Service Bus topology without visiting those pages", async ({ page }) => {
    const seen = { contexts: 0, namespaces: 0, sbQueues: 0, sbTopics: 0 };
    await page.route("**/api/aks/contexts", async (route) => {
      seen.contexts++;
      await route.fallback();
    });
    await page.route("**/api/aks/namespaces", async (route) => {
      seen.namespaces++;
      await route.fallback();
    });
    await page.route("**/api/servicebus/*/queues", async (route) => {
      seen.sbQueues++;
      await route.fallback();
    });
    await page.route("**/api/servicebus/*/topics", async (route) => {
      seen.sbTopics++;
      await route.fallback();
    });

    // Land on Redis — nothing on that page fetches AKS or SB data.
    await page.goto("/redis");
    await expect.poll(() => seen.contexts, { timeout: 10_000 }).toBeGreaterThan(0);
    await expect.poll(() => seen.namespaces).toBeGreaterThan(0);
    await expect.poll(() => seen.sbQueues).toBeGreaterThan(0);
    await expect.poll(() => seen.sbTopics).toBeGreaterThan(0);
  });

  test("honours the warm-up toggle being off", async ({ page }) => {
    await putUserSettings(page, { warmupConnectionsOnStartup: false });

    let aksNamespaces = 0;
    let sbQueues = 0;
    await page.route("**/api/aks/namespaces", async (route) => {
      aksNamespaces++;
      await route.fallback();
    });
    await page.route("**/api/servicebus/*/queues", async (route) => {
      sbQueues++;
      await route.fallback();
    });

    await page.goto("/redis");
    // The page's own data settles quickly; give the warm-up window to fire if it were going to.
    await page.waitForTimeout(2000);
    expect(aksNamespaces).toBe(0);
    expect(sbQueues).toBe(0);
  });
});

test.describe("restore last workspace", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("a cold start at / returns to the last page with its params", async ({ page }) => {
    await page.goto("/aks?ns=ecommerce");
    await expect(page.getByTestId("aks-namespace-dropdown")).toBeVisible();

    // Full reload at the root — the app should navigate back to the workspace.
    await page.goto("/");
    await expect(page).toHaveURL(/\/aks\?ns=ecommerce/, { timeout: 10_000 });
    await expect(page.getByTestId("aks-namespace-dropdown")).toContainText("ecommerce");
  });

  test("the restore toggle off always opens the dashboard", async ({ page }) => {
    await putUserSettings(page, { restoreLastWorkspaceOnStartup: false });

    await page.goto("/aks?ns=ecommerce");
    await expect(page.getByTestId("aks-namespace-dropdown")).toBeVisible();

    await page.goto("/");
    await page.waitForTimeout(1500);
    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByTestId("demo-mode-toggle")).toBeVisible();
  });

  test("launching directly at a deep link never redirects", async ({ page }) => {
    await page.goto("/aks?ns=payments");
    await expect(page.getByTestId("aks-namespace-dropdown")).toBeVisible();
    await page.waitForTimeout(1500);
    await expect(page).toHaveURL(/\/aks\?ns=payments/);
  });
});
