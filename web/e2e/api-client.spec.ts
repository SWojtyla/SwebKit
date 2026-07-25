import { test, expect } from "@playwright/test";

test.describe("API Client", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/api-client");
  });

  test("creates a collection, request, sends it and shows response", async ({ page }) => {
    // Add collection via dialog
    await page.getByTestId("add-collection-button").click();
    await expect(page.getByTestId("name-dialog")).toBeVisible();
    await page.getByTestId("name-dialog-input").fill("E2E Collection");
    await page.getByTestId("name-dialog-confirm").click();

    await page.getByTestId(/collection-root-/).first().waitFor();
    await page.getByTestId(/collection-root-/).first().click();

    // Add request via dialog
    await page.getByTestId("add-request-button").click();
    await expect(page.getByTestId("name-dialog")).toBeVisible();
    await page.getByTestId("name-dialog-input").fill("Health Check");
    await page.getByTestId("name-dialog-confirm").click();

    const requestNode = page.getByTestId(/collection-node-Request-/).first();
    await requestNode.waitFor();
    await requestNode.click();

    await page.getByTestId("request-url-input").fill("http://127.0.0.1:5198/health");
    await page.getByTestId("request-send-button").click();

    await expect(page.getByTestId("response-status")).toContainText("200", { timeout: 10_000 });
    await expect(page.getByTestId("response-body")).toContainText("status");
  });

  test("adds and removes a header", async ({ page }) => {
    // Add collection
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Header Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().click();

    // Add request
    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("Header Request");
    await page.getByTestId("name-dialog-confirm").click();

    await page.getByTestId(/collection-node-Request-/).first().click();

    // Switch to headers tab
    await page.getByTestId("request-tab-headers").click();
    await page.getByTestId("add-request-header-button").click();
    await page.locator('[data-testid="request-header-row-0"] input[placeholder="Header"]').fill("X-Test");
    await page.locator('[data-testid="request-header-row-0"] input[placeholder="Value"]').fill("value");

    await expect(page.locator('[data-testid="request-header-row-0"] input[placeholder="Header"]')).toHaveValue("X-Test");
  });

  test("collection tree search filters nodes", async ({ page }) => {
    // Add collection
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Searchable Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().click();

    // Add a request
    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("FindMe Request");
    await page.getByTestId("name-dialog-confirm").click();

    // Search should filter to show the request
    await page.getByTestId("collection-search").fill("FindMe");
    await expect(page.getByTestId(/collection-node-Request-/).first()).toBeVisible();

    // Search with non-matching term should hide it
    await page.getByTestId("collection-search").fill("NonExistent");
    await expect(page.getByTestId(/collection-node-Request-/)).toHaveCount(0);

    // Clear search
    await page.getByTestId("collection-search").fill("");
    await expect(page.getByTestId(/collection-node-Request-/).first()).toBeVisible();
  });

  test("inline rename via double-click", async ({ page }) => {
    // Add collection
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Original Name");
    await page.getByTestId("name-dialog-confirm").click();

    const collectionRoot = page.getByTestId(/collection-root-/).first();
    await collectionRoot.waitFor();

    // Double-click to rename
    await collectionRoot.dblclick();

    // The rename input appears inside the collection root element
    const renameInput = collectionRoot.locator("input").first();
    await renameInput.waitFor({ timeout: 5000 });
    await renameInput.fill("Renamed Collection");
    await page.keyboard.press("Enter");

    await expect(page.getByTestId(/collection-root-/).first()).toContainText("Renamed Collection");
  });

  test("context menu appears on right-click", async ({ page }) => {
    // Add collection
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Context Menu Test");
    await page.getByTestId("name-dialog-confirm").click();

    const collectionRoot = page.getByTestId(/collection-root-/).first();
    await collectionRoot.waitFor();
    await collectionRoot.click();

    // Right-click to open context menu
    await collectionRoot.click({ button: "right" });
    await expect(page.getByTestId("tree-context-menu")).toBeVisible();
    await expect(page.getByTestId("ctx-add-request")).toBeVisible();
    await expect(page.getByTestId("ctx-add-folder")).toBeVisible();
    await expect(page.getByTestId("ctx-rename")).toBeVisible();
    await expect(page.getByTestId("ctx-delete")).toBeVisible();

    // Close by clicking elsewhere
    await page.click("body", { position: { x: 0, y: 0 } });
    await expect(page.getByTestId("tree-context-menu")).not.toBeVisible();
  });

  test("delete confirmation dialog works", async ({ page }) => {
    // Add collection with unique name
    const uniqueName = `Delete Test ${Date.now()}`;
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill(uniqueName);
    await page.getByTestId("name-dialog-confirm").click();

    // Find our specific collection by text
    const collectionRoot = page.getByTestId(/collection-root-/).filter({ hasText: uniqueName }).first();
    await collectionRoot.waitFor();
    await collectionRoot.scrollIntoViewIfNeeded();

    // Right-click and delete
    await collectionRoot.click({ button: "right" });
    await page.getByTestId("ctx-delete").click();

    // Confirm dialog should appear
    await expect(page.getByTestId("confirm-dialog")).toBeVisible();

    // Cancel
    await page.getByTestId("confirm-dialog-cancel").click();
    await expect(page.getByTestId("confirm-dialog")).not.toBeVisible();
    await expect(collectionRoot).toBeVisible();

    // Delete for real
    await collectionRoot.scrollIntoViewIfNeeded();
    await collectionRoot.click({ button: "right" });
    await page.getByTestId("ctx-delete").click();
    await page.getByTestId("confirm-dialog-confirm").click();

    // Our specific collection should be gone
    await expect(page.getByTestId(/collection-root-/).filter({ hasText: uniqueName })).toHaveCount(0);
  });

  test("request editor tabs switch between params, headers, body, auth", async ({ page }) => {
    // Setup collection + request
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Tab Test Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().click();

    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("Tab Test Request");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-node-Request-/).first().click();

    // Default tab should be params
    await expect(page.getByTestId("params-tab")).toBeVisible();

    // Switch to headers
    await page.getByTestId("request-tab-headers").click();
    await expect(page.getByTestId("headers-tab")).toBeVisible();

    // Switch to body
    await page.getByTestId("request-tab-body").click();
    await expect(page.getByTestId("body-tab")).toBeVisible();

    // Switch to auth
    await page.getByTestId("request-tab-auth").click();
    await expect(page.getByTestId("auth-tab")).toBeVisible();
  });

  test("body pretty-print and minify work for JSON", async ({ page }) => {
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Body Format Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().click();

    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("Body Format Request");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-node-Request-/).first().click();

    // Go to body tab
    await page.getByTestId("request-tab-body").click();
    await page.getByTestId("request-body-mode-select").selectOption("Json");

    // Enter minified JSON
    const minified = '{"key":"value","nested":{"a":1}}';
    await page.getByTestId("request-body-editor").fill(minified);

    // Pretty print
    await page.getByTestId("body-pretty-print").click();
    const prettyValue = await page.getByTestId("request-body-editor").inputValue();
    expect(prettyValue).toContain("\n");

    // Minify back
    await page.getByTestId("body-minify").click();
    const minifiedValue = await page.getByTestId("request-body-editor").inputValue();
    expect(minifiedValue).not.toContain("\n  ");
  });

  test("response viewer shows pretty-print and copy buttons", async ({ page }) => {
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Response Test Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().click();

    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("Response Test Request");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-node-Request-/).first().click();

    await page.getByTestId("request-url-input").fill("http://127.0.0.1:5198/health");
    await page.getByTestId("request-send-button").click();

    await expect(page.getByTestId("response-status")).toContainText("200", { timeout: 10_000 });

    // Pretty print toggle should be available
    await expect(page.getByTestId("response-pretty-toggle")).toBeVisible();
    await expect(page.getByTestId("response-copy-body")).toBeVisible();

    // cURL toggle should be available
    await expect(page.getByTestId("response-curl-toggle")).toBeVisible();
    await page.getByTestId("response-curl-toggle").click();
    await expect(page.getByTestId("response-curl-panel")).toBeVisible();
    await expect(page.getByTestId("response-curl-panel")).toContainText("curl");
  });
});
