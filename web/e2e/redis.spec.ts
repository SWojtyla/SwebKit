import { test, expect } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { setDemoMode, scrollVirtualListIntoView } from "./helpers";

// The key browser tree is virtualized (@tanstack/react-virtual): rows outside the visible
// window aren't in the DOM at all, so keys alphabetically past what fits on screen (session:*,
// user:*) need the list scrolled toward them before Playwright can find/click their row.
const scrollToKey = (page: import("@playwright/test").Page, key: string) =>
  scrollVirtualListIntoView(page, "redis-key-tree-scroll", `redis-key-${key}`);

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
    await scrollToKey(page, "user:1001");

    // Click a string key
    await page.getByTestId("redis-key-user:1001").click();
    await expect(page.getByTestId("redis-detail-key-name")).toHaveText("user:1001");
    await expect(page.getByTestId("redis-detail-key-type")).toHaveText("string");
    await expect(page.getByTestId("redis-detail-string-value")).toContainText("Alice");
  });

  test("shows hash key fields", async ({ page }) => {
    await page.goto("/redis");

    await scrollToKey(page, "session:abc123");
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

    await scrollToKey(page, "user:1001");

    // Search for session keys only
    await page.getByTestId("redis-key-search").fill("session:*");
    await page.getByTestId("redis-key-search-btn").click();

    await expect(page.getByTestId("redis-key-session:abc123")).toBeVisible();
    await expect(page.getByTestId("redis-key-user:1001")).not.toBeVisible();
  });

  test("deletes a key after confirm", async ({ page }) => {
    await page.goto("/redis");

    await scrollToKey(page, "user:1002");
    await page.getByTestId("redis-key-user:1002").click();
    await expect(page.getByTestId("redis-detail-key-name")).toHaveText("user:1002");

    await page.getByTestId("redis-delete-key-btn").click();
    await expect(page.getByTestId("redis-confirm-bar")).toBeVisible();
    await page.getByTestId("redis-confirm-yes").click();

    // Key should be gone from the list after refresh
    await expect(page.getByTestId("redis-no-key-selected")).toBeVisible();
  });

  test("key detail shows rename, copy, and TTL controls", async ({ page }) => {
    await page.goto("/redis");
    await scrollToKey(page, "user:1001");
    await page.getByTestId("redis-key-user:1001").click();
    await expect(page.getByTestId("redis-detail-key-name")).toBeVisible();
    await expect(page.getByTestId("redis-copy-key-btn")).toBeVisible();
    await expect(page.getByTestId("redis-rename-btn")).toBeVisible();
    await expect(page.getByTestId("redis-ttl-edit-btn")).toBeVisible();
  });

  test("string value editing works", async ({ page }) => {
    await page.goto("/redis");
    await scrollToKey(page, "user:1001");
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

  test("exports selected keys with their values", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-batch-toggle").click();
    await scrollToKey(page, "user:1001");
    await page.getByTestId("redis-key-checkbox-user:1001").check();
    await scrollToKey(page, "session:abc123");
    await page.getByTestId("redis-key-checkbox-session:abc123").check();

    const downloadPromise = page.waitForEvent("download");
    await page.getByTestId("redis-batch-export").click();
    const download = await downloadPromise;
    const downloadPath = await download.path();
    expect(downloadPath).not.toBeNull();

    const exported = JSON.parse(await readFile(downloadPath!, "utf8")) as Record<string, unknown>;
    expect(exported["user:1001"]).toBe('{"id":1001,"name":"Alice","email":"alice@example.com"}');
    expect(exported["session:abc123"]).toMatchObject({
      user_id: "1001",
      ip: "10.0.0.1",
    });
    expect(exported["session:abc123"]).not.toEqual(exported["user:1001"]);
  });

  test("single key list shows all keys and loaded count", async ({ page }) => {
    await page.goto("/redis");
    // Namespace tree removed; keys render directly in the browser list.
    await expect(page.getByTestId("redis-key-browser")).toBeVisible();
    await scrollToKey(page, "user:1001");
    await expect(page.getByTestId("redis-key-count")).toContainText(/keys loaded/);
  });

  test("separator control filters namespace grouping in health/prefix tabs", async ({ page }) => {
    await page.goto("/redis");
    await expect(page.getByTestId("redis-separator-input")).toBeVisible();
    await page.getByTestId("redis-separator-input").fill(":");
    await page.getByTestId("redis-tab-keyspace").click();
    await expect(page.getByTestId("keyspace-health-panel")).toBeVisible();
    await page.getByTestId("redis-tab-prefix").click();
    await expect(page.getByTestId("prefix-memory-panel")).toBeVisible();
  });

  test("keyspace tab shows health panel", async ({ page }) => {
    await page.goto("/redis");
    await expect(page.getByTestId("redis-page")).toBeVisible();
    await page.getByTestId("redis-tab-keyspace").click();
    await expect(page.getByTestId("redis-keyspace")).toBeVisible();
    await expect(page.getByTestId("keyspace-health-panel")).toBeVisible();
    await expect(page.getByTestId("health-hit-rate")).toBeVisible();
    await expect(page.getByTestId("health-memory")).toBeVisible();
    await expect(page.getByTestId("health-findings")).toBeVisible();
    await expect(page.getByTestId("health-severity-filters")).toBeVisible();
    await page.getByTestId("health-filter-critical").click();
    await expect(page.getByTestId("health-findings")).toContainText("Critical");
    await page.locator("[data-testid^='health-open-']").first().click();
    await expect(page.getByTestId("redis-detail-key-name")).toBeVisible();
  });

  test("ops tab shows operational insights", async ({ page }) => {
    await page.goto("/redis");
    await expect(page.getByTestId("redis-page")).toBeVisible();
    await page.getByTestId("redis-tab-ops").click();
    await expect(page.getByTestId("redis-ops")).toBeVisible();
    await expect(page.getByTestId("ops-insights-panel")).toBeVisible();
    await expect(page.getByTestId("ops-total-commands")).toBeVisible();
  });

  test("prefixes tab shows memory breakdown", async ({ page }) => {
    await page.goto("/redis");
    await expect(page.getByTestId("redis-page")).toBeVisible();
    await page.getByTestId("redis-tab-prefix").click();
    await expect(page.getByTestId("redis-prefix")).toBeVisible();
    await expect(page.getByTestId("prefix-memory-panel")).toBeVisible();
    await expect(page.getByTestId("prefix-memory-table")).toBeVisible();
    await expect(page.getByTestId("prefix-memory-table")).toContainText("B");
    await expect(page.getByTestId("prefix-memory-table")).toContainText("%");
  });

  test("pub/sub tab is reachable", async ({ page }) => {
    await page.goto("/redis");
    await expect(page.getByTestId("redis-page")).toBeVisible();
    await page.getByTestId("redis-tab-pubsub").click();
    await expect(page.getByTestId("redis-pubsub")).toBeVisible();
    await expect(page.getByTestId("redis-pubsub-panel")).toBeVisible();
  });

  test("hash field add, edit, and delete", async ({ page }) => {
    await page.goto("/redis");
    await scrollToKey(page, "session:abc123");
    await page.getByTestId("redis-key-session:abc123").click();
    await expect(page.getByTestId("redis-detail-hash-fields")).toBeVisible();

    const field = `e2e_field_${Date.now()}`;

    await page.getByTestId("redis-hash-add-btn").click();
    await page.getByTestId("redis-hash-new-field").fill(field);
    await page.getByTestId("redis-hash-new-value").fill("initial-value");
    await page.getByTestId("redis-hash-new-save").click();

    const table = page.getByTestId("redis-detail-hash-fields");
    await expect(table).toContainText(field);
    await expect(table).toContainText("initial-value");

    await page.getByTestId(`redis-hash-edit-${field}`).click();
    await page.getByTestId(`redis-hash-edit-value-${field}`).fill("updated-value");
    await page.getByTestId(`redis-hash-save-${field}`).click();
    await expect(table).toContainText("updated-value");

    await page.getByTestId(`redis-hash-delete-${field}`).click();
    await expect(page.getByTestId("redis-confirm-bar")).toBeVisible();
    await page.getByTestId("redis-confirm-yes").click();
    await expect(table).not.toContainText(field);
  });

  test("zset score click-to-edit", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-key-leaderboard:daily").click();
    await expect(page.getByTestId("redis-detail-zset-members")).toBeVisible();

    await expect(page.getByTestId("redis-zset-score-alice")).toHaveText("1500");

    await page.getByTestId("redis-zset-score-alice").click();
    await page.getByTestId("redis-zset-score-input-alice").fill("9999");
    await page.getByTestId("redis-zset-score-save-alice").click();
    await expect(page.getByTestId("redis-zset-score-alice")).toHaveText("9999");

    // restore original score
    await page.getByTestId("redis-zset-score-alice").click();
    await page.getByTestId("redis-zset-score-input-alice").fill("1500");
    await page.getByTestId("redis-zset-score-save-alice").click();
    await expect(page.getByTestId("redis-zset-score-alice")).toHaveText("1500");
  });

  test("list pagination loads more items", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-key-cache:products").click();
    await expect(page.getByTestId("redis-detail-list-items")).toBeVisible();

    const table = page.getByTestId("redis-detail-list-items");
    await expect(table.getByText("product-5")).toBeVisible();
    await expect(table.getByText("product-6")).toHaveCount(0);

    await page.getByTestId("redis-list-load-more").click();
    await expect(table.getByText("product-6")).toBeVisible();
    await expect(table.getByText("product-10")).toBeVisible();
  });

  test("set pagination loads more members", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-key-cache:categories").click();
    await expect(page.getByTestId("redis-detail-set-members")).toBeVisible();

    const container = page.getByTestId("redis-detail-set-members");
    await expect(container.getByText("books")).toBeVisible();
    await expect(container.getByText("food")).toHaveCount(0);

    await page.getByTestId("redis-set-load-more").click();
    await expect(container.getByText("electronics")).toBeVisible();
    await expect(container.getByText("food")).toBeVisible();
  });
});
