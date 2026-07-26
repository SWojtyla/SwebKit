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

  test("pubsub tab is visible and functional", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-tab-pubsub").click();
    await expect(page.getByTestId("redis-pubsub-panel")).toBeVisible();
    await expect(page.getByTestId("redis-pubsub-channel-input")).toBeVisible();
  });

  test("pubsub add channel and subscribe", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-tab-pubsub").click();
    await page.getByTestId("redis-pubsub-channel-input").fill("test-channel");
    await page.getByTestId("redis-pubsub-add-channel").click();
    await expect(page.getByTestId("redis-pubsub-channel-test-channel")).toBeVisible();
    await page.getByTestId("redis-pubsub-subscribe-test-channel").click();
  });

  test("pubsub publish message form", async ({ page }) => {
    await page.goto("/redis");
    await page.getByTestId("redis-tab-pubsub").click();
    await expect(page.getByTestId("redis-pubsub-publish-channel")).toBeVisible();
    await expect(page.getByTestId("redis-pubsub-publish-message")).toBeVisible();
    await expect(page.getByTestId("redis-pubsub-publish-btn")).toBeVisible();
  });
});
