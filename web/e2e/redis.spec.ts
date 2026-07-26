import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Redis", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("displays keys in browser and shows key detail", async ({ page }) => {
    await page.goto("/redis");

    // Wait for key browser to load
    await expect(page.getByTestId("redis-key-browser")).toBeVisible();
    await expect(page.getByTestId("redis-key-user:1001")).toBeVisible();

    // Click a string key
    await page.getByTestId("redis-key-user:1001").click();
    await expect(page.getByTestId("redis-detail-key-name")).toHaveText("user:1001");
    await expect(page.getByTestId("redis-detail-key-type")).toHaveText("string");
    await expect(page.getByTestId("redis-detail-string-value")).toContainText("Alice");
  });

  test("shows hash key fields", async ({ page }) => {
    await page.goto("/redis");

    await expect(page.getByTestId("redis-key-session:abc123")).toBeVisible();
    await page.getByTestId("redis-key-session:abc123").click();
    await expect(page.getByTestId("redis-detail-key-type")).toHaveText("hash");
    await expect(page.getByTestId("redis-detail-hash-fields")).toBeVisible();
    await expect(page.getByTestId("redis-detail-hash-fields")).toContainText("user_id");
    await expect(page.getByTestId("redis-detail-hash-fields")).toContainText("1001");
  });

  test("shows server info tab", async ({ page }) => {
    await page.goto("/redis");

    await page.getByTestId("redis-tab-info").click();
    await expect(page.getByTestId("redis-server-info")).toBeVisible();
    await expect(page.getByTestId("redis-info-version")).toHaveText("7.2-demo");
    await expect(page.getByTestId("redis-info-clients")).toHaveText("3");
  });

  test("shows slow log tab", async ({ page }) => {
    await page.goto("/redis");

    await page.getByTestId("redis-tab-slowlog").click();
    await expect(page.getByTestId("redis-slowlog")).toBeVisible();
    await expect(page.getByTestId("redis-slowlog-count")).toContainText("entries");
  });

  test("searches keys with pattern", async ({ page }) => {
    await page.goto("/redis");

    await expect(page.getByTestId("redis-key-user:1001")).toBeVisible();

    // Search for session keys only
    await page.getByTestId("redis-key-search").fill("session:*");
    await page.getByTestId("redis-key-search-btn").click();

    await expect(page.getByTestId("redis-key-session:abc123")).toBeVisible();
    await expect(page.getByTestId("redis-key-user:1001")).not.toBeVisible();
  });

  test("deletes a key", async ({ page }) => {
    await page.goto("/redis");

    await expect(page.getByTestId("redis-key-user:1002")).toBeVisible();
    await page.getByTestId("redis-key-user:1002").click();
    await expect(page.getByTestId("redis-detail-key-name")).toHaveText("user:1002");

    page.on("dialog", (d) => d.accept());
    await page.getByTestId("redis-delete-key-btn").click();

    // Key should be gone from the list after refresh
    await expect(page.getByTestId("redis-no-key-selected")).toBeVisible();
  });

  test("key detail shows rename, copy, and TTL controls", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-key-user:1001").click();
    await expect(page.getByTestId("redis-detail-key-name")).toBeVisible();
    await expect(page.getByTestId("redis-copy-key-btn")).toBeVisible();
    await expect(page.getByTestId("redis-rename-btn")).toBeVisible();
    await expect(page.getByTestId("redis-ttl-edit-btn")).toBeVisible();
  });

  test("string value editing works", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-key-user:1001").click();
    await expect(page.getByTestId("redis-detail-string-value")).toBeVisible();
    await expect(page.getByTestId("redis-string-edit-btn")).toBeVisible();
    await page.getByTestId("redis-string-edit-btn").click();
    await expect(page.getByTestId("redis-detail-string-edit")).toBeVisible();
    await expect(page.getByTestId("redis-string-save-btn")).toBeVisible();
  });

  test("batch mode shows checkboxes and batch actions", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-batch-toggle").click();
    await expect(page.getByTestId("redis-batch-toggle")).toHaveText("Exit Batch");
    // Checkboxes should be visible for keys
    const firstKey = page.locator("[data-testid^='redis-key-checkbox-']").first();
    await expect(firstKey).toBeVisible();
    await page.getByTestId("redis-batch-toggle").click();
    await expect(page.getByTestId("redis-batch-toggle")).toHaveText("Batch Select");
  });

  test("namespace tree shows key prefixes", async ({ page }) => {
    await page.goto("/redis");
    await expect(page.getByTestId("redis-namespace-tree")).toBeVisible();
    // Should have namespace buttons for user, session, etc.
    const nsButtons = page.locator("[data-testid^='redis-namespace-']");
    const count = await nsButtons.count();
    expect(count).toBeGreaterThan(0);
  });

  test("advanced tab shows keyspace health and ops insights", async ({ page }) => {
    await page.goto("/redis");
    await expect(page.getByTestId("redis-page")).toBeVisible();
    await page.getByTestId("redis-tab-advanced").click();
    await expect(page.getByTestId("redis-advanced")).toBeVisible();
    await expect(page.getByTestId("keyspace-health-panel")).toBeVisible();
    await expect(page.getByTestId("health-hit-rate")).toBeVisible();
    await expect(page.getByTestId("health-memory")).toBeVisible();
    await expect(page.getByTestId("ops-insights-panel")).toBeVisible();
    await expect(page.getByTestId("ops-total-commands")).toBeVisible();
  });

  test("advanced tab shows prefix memory breakdown", async ({ page }) => {
    await page.goto("/redis");
    await expect(page.getByTestId("redis-page")).toBeVisible();
    await page.getByTestId("redis-tab-advanced").click();
    await expect(page.getByTestId("prefix-memory-panel")).toBeVisible();
    await expect(page.getByTestId("prefix-memory-table")).toBeVisible();
  });
});
