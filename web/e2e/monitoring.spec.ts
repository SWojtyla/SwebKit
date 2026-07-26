import { test, expect } from "@playwright/test";

test.describe("Monitoring", () => {
  test("page loads with title and tabs", async ({ page }) => {
    await page.goto("/monitoring");
    await expect(page.getByTestId("monitoring-page")).toBeVisible();
    await expect(page.getByTestId("monitoring-title")).toBeVisible();
    await expect(page.getByTestId("monitoring-tab-rules")).toBeVisible();
    await expect(page.getByTestId("monitoring-tab-history")).toBeVisible();
  });

  test("alert rules table shows demo rules", async ({ page }) => {
    await page.goto("/monitoring");
    await expect(page.getByTestId("monitoring-rules-table")).toBeVisible();
    const rows = page.locator("tbody tr");
    expect(await rows.count()).toBeGreaterThan(0);
  });

  test("toggle rule enabled/disabled", async ({ page }) => {
    await page.goto("/monitoring");
    await page.getByTestId("monitoring-rule-toggle-1").click();
    // Toggle should still be present
    await expect(page.getByTestId("monitoring-rule-toggle-1")).toBeVisible();
  });

  test("add rule opens editor and saves", async ({ page }) => {
    await page.goto("/monitoring");
    await page.getByTestId("monitoring-add-rule").click();
    await expect(page.getByTestId("alert-rule-editor")).toBeVisible();
    await page.getByTestId("alert-rule-name").fill("Test Alert");
    await page.getByTestId("alert-rule-condition").fill("Memory > 90%");
    await page.getByTestId("alert-rule-resource").fill("test-resource");
    await page.getByTestId("alert-rule-save").click();
    await expect(page.getByTestId("alert-rule-editor")).not.toBeVisible();
    // New rule should appear in table
    await expect(page.getByText("Test Alert")).toBeVisible();
  });

  test("edit rule opens editor with existing values", async ({ page }) => {
    await page.goto("/monitoring");
    await page.getByTestId("monitoring-rule-edit-1").click();
    await expect(page.getByTestId("alert-rule-editor")).toBeVisible();
    await expect(page.getByTestId("alert-rule-name")).toHaveValue("High CPU Usage");
  });

  test("delete rule removes from table", async ({ page }) => {
    await page.goto("/monitoring");
    const initialRows = await page.locator("tbody tr").count();
    await page.getByTestId("monitoring-rule-delete-4").click();
    const finalRows = await page.locator("tbody tr").count();
    expect(finalRows).toBe(initialRows - 1);
  });

  test("alert history tab shows events", async ({ page }) => {
    await page.goto("/monitoring");
    await page.getByTestId("monitoring-tab-history").click();
    await expect(page.getByTestId("monitoring-history-table")).toBeVisible();
    const rows = page.locator("tbody tr");
    expect(await rows.count()).toBeGreaterThan(0);
  });

  test("acknowledge event updates status", async ({ page }) => {
    await page.goto("/monitoring");
    await page.getByTestId("monitoring-tab-history").click();
    await expect(page.getByTestId("monitoring-event-ack-e1")).toBeVisible();
    await page.getByTestId("monitoring-event-ack-e1").click();
    await expect(page.getByTestId("monitoring-event-ack-e1")).not.toBeVisible();
  });
});
