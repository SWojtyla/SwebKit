import { test, expect } from "@playwright/test";

async function acceptPrompt(page: import("@playwright/test").Page, value: string) {
  page.once("dialog", async (dialog) => {
    await dialog.accept(value);
  });
}

test.describe("API Client", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/api-client");
  });

  test("creates a collection, request, sends it and shows response", async ({ page }) => {
    await acceptPrompt(page, "E2E Collection");
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId(/collection-root-/).first().waitFor();

    await page.getByTestId(/collection-root-/).first().click();

    await acceptPrompt(page, "Health Check");
    await page.getByTestId("add-request-button").click();

    const requestNode = page.getByTestId(/collection-node-Request-/).first();
    await requestNode.waitFor();
    await requestNode.click();

    await page.getByTestId("request-url-input").fill("http://127.0.0.1:5198/health");
    await page.getByTestId("request-send-button").click();

    await expect(page.getByTestId("response-status")).toContainText("200", { timeout: 10_000 });
    await expect(page.getByTestId("response-body")).toContainText("status");
  });

  test("adds and removes a header", async ({ page }) => {
    await acceptPrompt(page, "Header Collection");
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId(/collection-root-/).first().click();

    await acceptPrompt(page, "Header Request");
    await page.getByTestId("add-request-button").click();

    await page.getByTestId(/collection-node-Request-/).first().click();

    await page.getByTestId("add-request-header-button").click();
    await page.locator('[data-testid="request-header-row-0"] input[placeholder="Header"]').fill("X-Test");
    await page.locator('[data-testid="request-header-row-0"] input[placeholder="Value"]').fill("value");

    await expect(page.locator('[data-testid="request-header-row-0"] input[placeholder="Header"]')).toHaveValue("X-Test");
  });
});
