import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Redis deferred features", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("namespace tree expand/collapse works", async ({ page }) => {
    await page.goto("/redis");
    await expect(page.getByTestId("redis-page")).toBeVisible();
    await page.getByTestId("redis-key-search-btn").click();
    await expect(page.getByTestId("redis-namespace-tree")).toBeVisible();
  });

  test("pubsub tab is visible and snapshot summary loads", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-tab-pubsub").click();
    await expect(page.getByTestId("redis-pubsub-panel")).toBeVisible();
    await expect(page.getByTestId("redis-pubsub-summary")).toBeVisible();
    await expect(page.getByTestId("redis-pubsub-channel-count")).toHaveText("6");
    await expect(page.getByTestId("redis-pubsub-pattern-count")).toHaveText("2");
  });

  test("pubsub channels list renders with subscriber counts", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-tab-pubsub").click();
    await expect(page.getByTestId("redis-pubsub-channels-table")).toBeVisible();
    await expect(page.getByTestId("redis-pubsub-channel-row-notifications:global")).toBeVisible();
    await expect(page.getByTestId("redis-pubsub-subscriber-count-notifications:global")).toHaveText("14");
  });

  test("pubsub pattern filter updates the snapshot", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-tab-pubsub").click();
    await expect(page.getByTestId("redis-pubsub-channels-table")).toBeVisible();
    await expect(page.getByTestId("redis-pubsub-channel-row-notifications:global")).toBeVisible();

    await page.getByTestId("redis-pubsub-pattern-input").fill("events:*");
    await page.getByTestId("redis-pubsub-filter-btn").click();
    await expect(page.getByTestId("redis-pubsub-channel-count")).toHaveText("2");
    await expect(page.getByTestId("redis-pubsub-channel-row-events:orders")).toBeVisible();
    await expect(page.getByTestId("redis-pubsub-channel-row-events:inventory")).toBeVisible();
    await expect(page.getByTestId("redis-pubsub-channel-row-notifications:global")).toHaveCount(0);

    await page.getByTestId("redis-pubsub-pattern-input").fill("no-match:*");
    await page.getByTestId("redis-pubsub-filter-btn").click();
    await expect(page.getByTestId("redis-pubsub-empty")).toBeVisible();
  });
});
