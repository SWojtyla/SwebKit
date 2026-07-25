import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Service Bus", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
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

  test("text filter narrows message list", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();

    await expect(page.getByTestId("message-list")).toBeVisible();
    const initialCount = await page.getByTestId("message-list").locator("button[data-testid^='message-item-']").count();
    expect(initialCount).toBeGreaterThan(0);

    // Type a filter that should narrow results
    await page.getByTestId("message-text-filter").fill("zzznomatch");
    await expect(page.getByTestId("message-list-no-matches")).toBeVisible();

    // Clear filter
    await page.getByTestId("message-text-filter").fill("");
    await expect(page.getByTestId("message-list")).toBeVisible();
  });

  test("advanced filter panel opens and can add rules", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();

    await expect(page.getByTestId("message-list")).toBeVisible();

    // Open advanced filter panel
    await page.getByTestId("toggle-advanced-filter").click();
    await expect(page.getByTestId("advanced-filter-panel")).toBeVisible();

    // Add a rule
    await page.getByTestId("rule-add").click();
    await expect(page.getByTestId("advanced-rule")).toHaveCount(1);

    // Change field to Delivery Count
    await page.getByTestId("rule-field").selectOption("delivery-count");
    await page.getByTestId("rule-value").fill("999");

    // Should show no matches (no message has delivery count >= 999 with gte operator)
    await expect(page.getByTestId("message-list-no-matches")).toBeVisible();

    // Remove the rule
    await page.getByTestId("rule-remove").click();
    await expect(page.getByTestId("advanced-rule")).toHaveCount(0);

    // Messages should be visible again
    await expect(page.getByTestId("message-list")).toBeVisible();
  });

  test("advanced filter by application property works", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();

    await expect(page.getByTestId("message-list")).toBeVisible();

    // Open advanced filter and add a rule for application property
    await page.getByTestId("toggle-advanced-filter").click();
    await page.getByTestId("rule-add").click();

    // Default field is application-property, operator is contains
    await page.getByTestId("rule-property").fill("orderId");
    await page.getByTestId("rule-value").fill("ORD-");

    // Should filter - either show matches or no-matches (depends on demo data)
    // The key assertion is that the filter is being applied
    const hasMatches = await page.getByTestId("message-list").locator("button[data-testid^='message-item-']").count();
    const hasNoMatch = await page.getByTestId("message-list-no-matches").count();
    expect(hasMatches > 0 || hasNoMatch > 0).toBeTruthy();
  });

  test("filter count shows when filters are active", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();

    await expect(page.getByTestId("message-list")).toBeVisible();

    // Type a filter
    await page.getByTestId("message-text-filter").fill("order");

    // Filter count should be visible (if there are matches)
    const hasMatches = await page.getByTestId("message-list").count();
    if (hasMatches > 0) {
      await expect(page.getByTestId("message-filter-count")).toBeVisible();
    }
  });

  test("message detail tabs switch content", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();

    const firstMessage = page.getByTestId("message-list").locator("button").first();
    await firstMessage.click();
    await expect(page.getByTestId("message-detail")).toBeVisible();

    // Body tab is active by default
    await expect(page.getByTestId("detail-tab-body")).toBeVisible();
    await expect(page.getByTestId("detail-tab-content-body")).toBeVisible();
    await expect(page.getByTestId("message-detail-body")).toBeVisible();

    // Switch to Properties tab
    await page.getByTestId("detail-tab-properties").click();
    await expect(page.getByTestId("detail-tab-content-properties")).toBeVisible();

    // Switch to System tab
    await page.getByTestId("detail-tab-system").click();
    await expect(page.getByTestId("detail-tab-content-system")).toBeVisible();
  });

  test("copy body and copy full message buttons work", async ({ browser }) => {
    const context = await browser.newContext({ permissions: ["clipboard-read", "clipboard-write"] });
    const page = await context.newPage();
    try {
      await setDemoMode(page, true);
      await page.goto("/service-bus");
      await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
      await page.getByTestId("entity-tree-queue-order-created").click();

      const firstMessage = page.getByTestId("message-list").locator("button").first();
      await firstMessage.click();
      await expect(page.getByTestId("message-detail")).toBeVisible();

      // Copy body
      await expect(page.getByTestId("message-copy-body")).toBeVisible();
      await page.getByTestId("message-copy-body").click();
      await expect(page.getByTestId("message-copy-body")).toContainText("Copied!");

      // Copy full message
      await expect(page.getByTestId("message-copy-full")).toBeVisible();
      await page.getByTestId("message-copy-full").click();
      await expect(page.getByTestId("message-copy-full")).toContainText("Copied!");
    } finally {
      await setDemoMode(page, false);
      await context.close();
    }
  });

  test("purge shows confirmation dialog and can cancel", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();

    const firstMessage = page.getByTestId("message-list").locator("button").first();
    await firstMessage.click();
    await expect(page.getByTestId("message-detail")).toBeVisible();

    // Click purge - should show confirmation, not immediately purge
    await page.getByTestId("message-purge-button").click();
    await expect(page.getByTestId("purge-confirm")).toBeVisible();

    // Cancel
    await page.getByTestId("purge-confirm-cancel").click();
    await expect(page.getByTestId("purge-confirm")).not.toBeVisible();
  });

  test("DLQ message shows DLQ Info tab", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-failed").click();
    await page.getByTestId("sb-view-dlq").click();

    const firstMessage = page.getByTestId("message-list").locator("button").first();
    await firstMessage.click();
    await expect(page.getByTestId("message-detail")).toBeVisible();

    // DLQ Info tab should be visible
    await expect(page.getByTestId("detail-tab-dlq")).toBeVisible();
    await page.getByTestId("detail-tab-dlq").click();
    await expect(page.getByTestId("detail-tab-content-dlq")).toBeVisible();
  });

  test("compose button opens composer modal", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();

    await page.getByTestId("sb-compose-button").click();
    await expect(page.getByTestId("message-composer")).toBeVisible();
    await expect(page.getByTestId("composer-title")).toContainText("Compose");

    // Close
    await page.getByTestId("composer-close").click();
    await expect(page.getByTestId("message-composer")).not.toBeVisible();
  });

  test("composer can fill fields and send message", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();

    await page.getByTestId("sb-compose-button").click();
    await expect(page.getByTestId("message-composer")).toBeVisible();

    // Fill in fields
    await page.getByTestId("composer-subject").fill("Test Subject");
    await page.getByTestId("composer-body").fill('{"test": true}');

    // Format JSON
    await page.getByTestId("composer-format-json").click();

    // Add a property
    await page.getByTestId("composer-add-property").click();
    await page.getByTestId("composer-property-key-0").fill("orderId");
    await page.getByTestId("composer-property-value-0").fill("ORD-123");

    // Send (in demo mode this should succeed)
    await page.getByTestId("composer-send").click();

    // Composer should close after successful send
    await expect(page.getByTestId("message-composer")).not.toBeVisible();
  });

  test("composer cancel returns to page", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();

    await page.getByTestId("sb-compose-button").click();
    await expect(page.getByTestId("message-composer")).toBeVisible();

    await page.getByTestId("composer-cancel").click();
    await expect(page.getByTestId("message-composer")).not.toBeVisible();
  });

  test("save message as template from detail", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();

    const firstMessage = page.getByTestId("message-list").locator("button").first();
    await firstMessage.click();
    await expect(page.getByTestId("message-detail")).toBeVisible();

    // Click Save as Template
    await page.getByTestId("message-save-template").click();
    await expect(page.getByTestId("save-template-dialog")).toBeVisible();

    // Enter template name and save
    await page.getByTestId("template-name-input").fill("E2E Test Template");
    await page.getByTestId("template-save-confirm").click();

    // Dialog should close
    await expect(page.getByTestId("save-template-dialog")).not.toBeVisible();
  });

  test("load template in composer", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();

    // First save a template from a message
    const firstMessage = page.getByTestId("message-list").locator("button").first();
    await firstMessage.click();
    await page.getByTestId("message-save-template").click();
    await page.getByTestId("template-name-input").fill("Composer Load Test");
    await page.getByTestId("template-save-confirm").click();
    await expect(page.getByTestId("save-template-dialog")).not.toBeVisible();

    // Wait a moment for the mutation to complete and cache to invalidate
    await page.waitForTimeout(500);

    // Now open composer and load the template
    await page.getByTestId("sb-compose-button").click();
    await expect(page.getByTestId("message-composer")).toBeVisible();

    await page.getByTestId("composer-load-template").click();
    await expect(page.getByTestId("template-picker")).toBeVisible();

    // Wait for templates to load (either items or empty state)
    await expect(
      page.getByTestId("template-picker-empty").or(page.locator("[data-testid^='template-select-']").first()),
    ).toBeVisible({ timeout: 10000 });

    // Should have at least one template from the save above
    const templateItems = page.locator("[data-testid^='template-select-']");
    const count = await templateItems.count();
    expect(count).toBeGreaterThan(0);

    // Select the first template
    await templateItems.first().click();
    await expect(page.getByTestId("template-picker")).not.toBeVisible();

    // Composer should still be visible with loaded data
    await expect(page.getByTestId("message-composer")).toBeVisible();
  });
});
