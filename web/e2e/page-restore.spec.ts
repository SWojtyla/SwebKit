import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";
import { sidecarPort } from "./test-config";

const sidecarUrl = `http://127.0.0.1:${sidecarPort}`;

test.describe("page restore & deep-link parity", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("storage browsing position is URL-driven and survives a reload", async ({ page }) => {
    await page.goto("/storage");
    // The resolved account settles into the URL so the position is shareable.
    await expect(page).toHaveURL(/account=demo-storage/);

    await page.getByTestId("storage-container-configs").click();
    await expect(page).toHaveURL(/container=configs/);

    await page.getByTestId("storage-item-env/").click();
    await expect(page).toHaveURL(/prefix=env/);

    await page.getByTestId("storage-item-env/prod.json").click();
    await expect(page).toHaveURL(/blob=env/);
    await expect(page.getByTestId("storage-blob-name")).toHaveText("env/prod.json");

    await page.reload();
    // The breadcrumb shows the container crumb plus the "you are here" leaf label.
    await expect(page.getByTestId("storage-breadcrumb-0")).toBeVisible();
    await expect(page.getByText("/ env")).toBeVisible();
    await expect(page.getByTestId("storage-blob-name")).toHaveText("env/prod.json");
    await expect(page).toHaveURL(/container=configs/);
    await expect(page).toHaveURL(/prefix=env/);
  });

  test("a bare storage visit restores the account's last-used container", async ({ page }) => {
    // Remember "exports" as this account's last container.
    await page.goto("/storage?container=exports");
    await expect(page.getByTestId("storage-blob-browser")).toBeVisible();

    // A bare visit restores the persisted container once the list validates it.
    await page.goto("/storage");
    await expect(page).toHaveURL(/container=exports/);
    await expect(page.getByTestId("storage-item-2026-03-21-report.csv")).toBeVisible();
  });

  test("a storage deep link into a prefix reconstructs the full breadcrumb", async ({ page }) => {
    await page.goto("/storage?account=demo-storage&container=configs&prefix=env%2F");
    // The breadcrumb trail is derived from the prefix, so a deep link shows the
    // container crumb plus the "you are here" leaf segment.
    await expect(page.getByTestId("storage-breadcrumb-0")).toBeVisible();
    await expect(page.getByText("/ env")).toBeVisible();
    await expect(page.getByTestId("storage-item-env/prod.json")).toBeVisible();

    // Clicking the container crumb returns to the root listing.
    await page.getByTestId("storage-breadcrumb-0").click();
    await expect(page).not.toHaveURL(/prefix=/);
    await expect(page.getByTestId("storage-item-app-settings.json")).toBeVisible();
  });

  test("redis tab is a deep-linkable URL param", async ({ page }) => {
    await page.goto("/redis?tab=slowlog");
    await expect(page.getByTestId("redis-slowlog")).toBeVisible();

    // Clicking a tab writes the param back; the default tab clears it.
    await page.getByTestId("redis-tab-info").click();
    await expect(page).toHaveURL(/tab=info/);
    await page.getByTestId("redis-tab-keys").click();
    await expect(page).not.toHaveURL(/tab=/);
  });

  test("redis restores the applied key pattern across a reload", async ({ page }) => {
    await page.goto("/redis");
    const search = page.getByTestId("redis-key-search");
    await search.fill("user:*");
    await page.getByTestId("redis-key-search-btn").click();
    await expect(search).toHaveValue("user:*");

    await page.reload();
    await expect(page.getByTestId("redis-key-search")).toHaveValue("user:*");
  });

  test("sql connection deep link settles the param and switching writes it back", async ({ page }) => {
    await page.goto("/sql?connection=demo-sql-2");
    await expect(page.getByTestId("sql-connection-select")).toHaveValue("demo-sql-2");

    await page.getByTestId("sql-connection-select").selectOption("demo-sql");
    await expect(page).toHaveURL(/connection=demo-sql(?!-)/);
  });

  test("monitoring tab is a deep-linkable URL param", async ({ page }) => {
    await page.goto("/monitoring?tab=history");
    await expect(page.getByTestId("monitoring-tab-history")).toHaveClass(/text-primary/);

    await page.getByTestId("monitoring-tab-rules").click();
    await expect(page).not.toHaveURL(/tab=/);
  });
});

test.describe("not-configured CTAs", () => {
  test("storage empty state links to the Storage settings tab", async ({ page }) => {
    await setDemoMode(page, false);
    const profileRes = await page.request.get(`${sidecarUrl}/api/config/profiles`);
    const original = (await profileRes.json()) as Record<string, unknown>;
    const stripped = structuredClone(original) as { config: { storageAccounts?: unknown[] } };
    stripped.config.storageAccounts = [];

    try {
      await page.request.put(`${sidecarUrl}/api/config/profiles`, { data: stripped });
      await page.goto("/storage");
      await expect(page.getByTestId("storage-no-account")).toBeVisible();

      await page.getByTestId("storage-goto-settings").click();
      await expect(page).toHaveURL(/\/settings/);
      await expect(page.getByTestId("settings-tab-storage")).toHaveClass(/bg-primary/);
    } finally {
      await page.request.put(`${sidecarUrl}/api/config/profiles`, { data: original });
    }
  });

  test("redis empty state links to the Redis settings tab", async ({ page }) => {
    await setDemoMode(page, false);
    const profileRes = await page.request.get(`${sidecarUrl}/api/config/profiles`);
    const original = (await profileRes.json()) as Record<string, unknown>;
    const stripped = structuredClone(original) as { config: { redisConfig?: { caches?: unknown[] } } };
    stripped.config.redisConfig = { caches: [] };

    try {
      await page.request.put(`${sidecarUrl}/api/config/profiles`, { data: stripped });
      await page.goto("/redis");
      await expect(page.getByTestId("redis-no-cache")).toBeVisible();

      await page.getByTestId("redis-goto-settings").click();
      await expect(page).toHaveURL(/\/settings/);
      await expect(page.getByTestId("settings-tab-redis")).toHaveClass(/bg-primary/);
    } finally {
      await page.request.put(`${sidecarUrl}/api/config/profiles`, { data: original });
    }
  });
});
