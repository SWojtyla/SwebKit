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

  test("environment manager creates and edits environments", async ({ page }) => {
    // Open environment manager
    await page.getByTestId("env-manager-button").click();
    await expect(page.getByTestId("env-manager")).toBeVisible();

    // Add a new environment
    await page.getByTestId("env-add-button").click();
    await expect(page.getByTestId("env-editor")).toBeVisible();

    // Edit name
    await page.getByTestId("env-name-input").fill("Test Environment");

    // Add a variable
    await page.getByTestId("env-add-variable").click();
    await page.getByTestId("env-var-key-0").fill("baseUrl");
    await page.getByTestId("env-var-value-0").fill("http://localhost:5198");

    // Save
    await page.getByTestId("env-save-all").click();

    // Environment selector should show the new environment
    const envSelector = page.getByTestId("env-selector");
    await expect(envSelector).toContainText("Test Environment");
  });

  test("environment selector dropdown shows environments", async ({ page }) => {
    // Open env manager and create an environment
    await page.getByTestId("env-manager-button").click();
    await page.getByTestId("env-add-button").click();
    await page.getByTestId("env-name-input").fill("Selector Test Env");
    await page.getByTestId("env-save-all").click();

    // Selector should contain it
    const envSelector = page.getByTestId("env-selector");
    await expect(envSelector).toContainText("Selector Test Env");

    // Select it
    await envSelector.selectOption({ label: "Selector Test Env" });

    // Active env name should show
    await expect(page.getByTestId("active-env-name")).toContainText("Selector Test Env");
  });

  test("collection variables editor works", async ({ page }) => {
    // Create a collection
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Col Var Test Collection");
    await page.getByTestId("name-dialog-confirm").click();

    // Select it
    await page.getByTestId(/collection-root-/).filter({ hasText: "Col Var Test Collection" }).first().click();

    // Open collection variables editor
    await page.getByTestId("col-vars-button").click();
    await expect(page.getByTestId("col-var-editor")).toBeVisible();

    // Add a variable
    await page.getByTestId("col-var-add").click();
    await page.getByTestId("col-var-key-0").fill("apiKey");
    await page.getByTestId("col-var-value-0").fill("test-key-123");

    // Save
    await page.getByTestId("col-var-save").click();

    // Reopen to verify
    await page.getByTestId("col-vars-button").click();
    await expect(page.getByTestId("col-var-key-0")).toHaveValue("apiKey");
    await expect(page.getByTestId("col-var-value-0")).toHaveValue("test-key-123");
  });

  test("multi-tab: opening requests creates tabs and switching preserves state", async ({ page }) => {
    // Create a collection with two requests
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Multi-Tab Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().click();

    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("First Request");
    await page.getByTestId("name-dialog-confirm").click();

    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("Second Request");
    await page.getByTestId("name-dialog-confirm").click();

    // Creating requests auto-opens tabs, so we should already have 2 tabs
    const tabItems = page.locator('[data-testid^="open-tab-"]');
    await expect(tabItems).toHaveCount(2);
    await expect(page.getByTestId("request-tab-strip")).toBeVisible();

    // Set URL on second request (currently active tab from last creation)
    await page.getByTestId("request-url-input").fill("http://127.0.0.1:5198/second");

    // Switch to first tab — URL should be empty
    await tabItems.filter({ hasText: "First Request" }).first().click();
    await expect(page.getByTestId("request-url-input")).toHaveValue("");

    // Set URL on first request
    await page.getByTestId("request-url-input").fill("http://127.0.0.1:5198/first");

    // Switch to second tab — URL should be preserved
    await tabItems.filter({ hasText: "Second Request" }).first().click();
    await expect(page.getByTestId("request-url-input")).toHaveValue("http://127.0.0.1:5198/second");

    // Switch back to first tab — URL should also be preserved
    await tabItems.filter({ hasText: "First Request" }).first().click();
    await expect(page.getByTestId("request-url-input")).toHaveValue("http://127.0.0.1:5198/first");
  });

  test("multi-tab: closing a tab works", async ({ page }) => {
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Close Tab Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().click();

    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("Closable Request");
    await page.getByTestId("name-dialog-confirm").click();

    // Tab should be open
    const tabItems = page.locator('[data-testid^="open-tab-"]');
    await expect(tabItems).toHaveCount(1);

    // Close it
    await page.locator('[data-testid^="tab-close-"]').first().click();
    await expect(tabItems).toHaveCount(0);
    await expect(page.getByTestId("api-client-empty-editor")).toBeVisible();
  });

  test("GraphQL panel shows query and variables editors", async ({ page }) => {
    // Create a collection and request
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("GraphQL Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().click();

    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("GraphQL Request");
    await page.getByTestId("name-dialog-confirm").click();

    // Switch method to GraphQL
    await page.getByTestId("request-method-select").selectOption("GraphQl");

    // Should see GraphQL tab instead of Body
    await expect(page.getByTestId("request-tab-graphql")).toBeVisible();
    await expect(page.getByTestId("request-tab-body")).not.toBeVisible();

    // Click GraphQL tab
    await page.getByTestId("request-tab-graphql").click();
    await expect(page.getByTestId("graphql-panel")).toBeVisible();

    // Type a query
    await page.getByTestId("graphql-query-input").fill("query { hello }");
    await expect(page.getByTestId("graphql-query-input")).toHaveValue("query { hello }");

    // Type variables
    await page.getByTestId("graphql-variables-input").fill('{\n  "key": "value"\n}');
    await expect(page.getByTestId("graphql-variables-input")).toHaveValue('{\n  "key": "value"\n}');
  });

  test("WebSocket panel shows connection controls and message log", async ({ page }) => {
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("WebSocket Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().click();

    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("WebSocket Request");
    await page.getByTestId("name-dialog-confirm").click();

    // Switch method to WebSocket
    await page.getByTestId("request-method-select").selectOption("WebSocket");

    // Should see WebSocket tab instead of Body
    await expect(page.getByTestId("request-tab-websocket")).toBeVisible();
    await expect(page.getByTestId("request-tab-body")).not.toBeVisible();

    // Click WebSocket tab
    await page.getByTestId("request-tab-websocket").click();
    await expect(page.getByTestId("websocket-panel")).toBeVisible();

    // Should see connect button and status
    await expect(page.getByTestId("ws-connect-button")).toBeVisible();
    await expect(page.getByTestId("ws-status")).toContainText("Disconnected");

    // Should see message log area
    await expect(page.getByTestId("ws-messages")).toBeVisible();

    // Should see saved messages section
    await expect(page.getByTestId("ws-add-saved")).toBeVisible();
  });
});
