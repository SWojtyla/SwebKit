import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Service Bus", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test("selects demo namespace, queue and displays active messages", async ({ page }) => {
    await page.goto("/service-bus");
    await expect(page.getByTestId("sb-namespace-select")).toContainText("orders-dev");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });

    await expect(page.getByTestId("entity-tree-queue-order-created")).toBeVisible();
    await page.getByTestId("entity-tree-queue-order-created").click();

    await expect(page.getByTestId("message-list")).toBeVisible();
    const firstMessage = page.getByTestId("message-list").locator("button").first();
    await expect(firstMessage).toBeVisible();

    await firstMessage.click();
    await expect(page.getByTestId("message-detail")).toBeVisible();
    await expect(page.getByTestId("message-detail-body")).toBeVisible();
    await expect(page.getByTestId("message-complete-button")).toBeVisible();
  });

  test("switches to DLQ view and shows dead-letter messages", async ({ page }) => {
    await page.goto("/service-bus");
    await expect(page.getByTestId("sb-namespace-select")).toContainText("orders-dev");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });

    await page.getByTestId("entity-tree-queue-order-failed").click();
    await page.getByTestId("sb-view-dlq").click();

    await expect(page.getByTestId("message-list")).toBeVisible();
    const firstMessage = page.getByTestId("message-list").locator("button").first();
    await expect(firstMessage).toBeVisible();

    await firstMessage.click();
    await expect(page.getByTestId("message-detail")).toBeVisible();
    await expect(page.getByTestId("message-resubmit-button")).toBeVisible();
    await expect(page.getByTestId("message-complete-dlq-button")).toBeVisible();
  });
});
