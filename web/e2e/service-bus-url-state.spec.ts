import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Service Bus URL state", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("namespace, entity, view and message selection update the URL and survive reload", async ({ page }) => {
    await page.goto("/service-bus");

    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    const expectedNs = "00000000-0000-0000-0000-000000000001";
    await expect(page).toHaveURL(new RegExp(`ns=${expectedNs}`));

    await page.getByTestId("entity-tree-queue-order-created").click();
    await expect(page).toHaveURL(/entity=order-created/);
    await expect(page).toHaveURL(/entityName=order-created/);
    await expect(page.getByTestId("message-list")).toBeVisible();

    await page.getByTestId("message-item-4501").click();
    await expect(page.getByTestId("message-detail")).toBeVisible();
    await expect(page).toHaveURL(/msg=oc-001/);
    await expect(page).toHaveURL(/seq=4501/);

    // Reload should restore the same selected namespace/entity/message.
    const currentUrl = page.url();
    await page.reload();
    await expect(page.getByTestId("message-detail")).toBeVisible();
    await expect(page.getByTestId("message-detail-subject")).toContainText("OrderCreated");
    await expect(page).toHaveURL(/msg=oc-001/);
    await expect(page).toHaveURL(/seq=4501/);
    expect(page.url()).toBe(currentUrl);
  });

  test("browser back and forward navigate between previous and current Service Bus views", async ({ page }) => {
    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();
    await page.getByTestId("message-item-4501").click();
    await expect(page.getByTestId("message-detail")).toBeVisible();

    await page.goBack();
    await expect(page.getByTestId("message-detail")).not.toBeVisible();
    await expect(page).toHaveURL(/entity=order-created/);
    await expect(page).not.toHaveURL(/msg=/);
    await expect(page).not.toHaveURL(/seq=/);

    await page.goForward();
    await expect(page.getByTestId("message-detail")).toBeVisible();
    await expect(page).toHaveURL(/msg=oc-001/);
    await expect(page).toHaveURL(/seq=4501/);
  });
});
