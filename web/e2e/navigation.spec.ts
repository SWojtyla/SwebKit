import { test, expect } from "@playwright/test";

const navItems = [
  { nav: "nav-dashboard", url: "/", page: "dashboard-page" },
  { nav: "nav-service-bus", url: "/service-bus", page: "service-bus-page" },
  { nav: "nav-aks", url: "/aks", page: "aks-page" },
  { nav: "nav-api-client", url: "/api-client", page: "api-client-page" },
  { nav: "nav-redis", url: "/redis", page: "redis-page" },
  { nav: "nav-storage", url: "/storage", page: "storage-page" },
  { nav: "nav-ai-agent", url: "/agent", page: "agent-page" },
  { nav: "nav-settings", url: "/settings", page: "settings-page" },
];

test.describe("Navigation", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/");
  });

  for (const item of navItems) {
    test(`navigates to ${item.url} via sidebar`, async ({ page }) => {
      await page.getByTestId(item.nav).click();
      await expect(page).toHaveURL(new RegExp(`${item.url}$`));
      await expect(page.getByTestId(item.page)).toBeVisible();
    });
  }
});
