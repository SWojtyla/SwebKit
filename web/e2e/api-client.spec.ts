import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

const sidecarBaseUrl = `http://127.0.0.1:${process.env.PLAYWRIGHT_SIDECAR_PORT ?? "5198"}`;

test.describe("API Client", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
    await page.goto("/api-client");
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
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

    await page.getByTestId("request-url-input").fill(`${sidecarBaseUrl}/health`);
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

    // Filter so the virtualized tree renders our specific collection
    await page.getByTestId("collection-search").fill(uniqueName);
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

    await page.getByTestId("request-url-input").fill(`${sidecarBaseUrl}/health`);
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

  test("environment variable source picker switches fields and lists configured key vaults", async ({ page }) => {
    const uniqueVaultName = `Test Vault ${Date.now()}`;

    // Each field here mutates the whole profile independently (no debounce, no optimistic cache
    // update), so firing two edits back-to-back races: the second mutate can read a profile
    // snapshot from before the first one's round trip landed and silently overwrite it. Wait for
    // each save to land before starting the next edit.
    const saveProfile = () =>
      page.waitForResponse((r) => r.url().includes("/api/config/profiles") && r.request().method() === "PUT");

    // Configure a Key Vault in Settings so the picker has something to list.
    await page.goto("/settings");
    await expect(page.getByTestId("key-vaults-section")).toBeVisible();
    const existingVaultCount = await page.locator('[data-testid^="kv-name-"]').count();
    await Promise.all([saveProfile(), page.getByTestId("kv-add").click()]);
    await Promise.all([
      saveProfile(),
      page.getByTestId(`kv-name-${existingVaultCount}`).fill(uniqueVaultName),
    ]);
    await Promise.all([
      saveProfile(),
      page.getByTestId(`kv-url-${existingVaultCount}`).fill("https://test-vault.vault.azure.net/"),
    ]);

    // Reload and confirm the vault persisted before moving on, so the environment editor's fetch
    // below can't race the save.
    await page.reload();
    await expect(page.getByTestId(`kv-name-${existingVaultCount}`)).toHaveValue(uniqueVaultName);

    await page.goto("/api-client");
    await page.getByTestId("env-manager-button").click();
    await page.getByTestId("env-add-button").click();
    await page.getByTestId("env-name-input").fill("Source Picker Test Env");
    await page.getByTestId("env-add-variable").click();
    await page.getByTestId("env-var-key-0").fill("apiKey");

    // Plain (default): a single value input, no vault picker or preview button.
    await expect(page.getByTestId("env-var-value-0")).toHaveAttribute("placeholder", "Value");
    await expect(page.getByTestId("env-var-vault-0")).toHaveCount(0);
    await expect(page.getByTestId("env-var-preview-btn-0")).toHaveCount(0);

    // Windows Credential Store: a credential-key input.
    await page.getByTestId("env-var-source-0").selectOption("WindowsCredentialStore");
    await expect(page.getByTestId("env-var-value-0")).toHaveAttribute("placeholder", "Credential key");

    // Azure Key Vault: the configured vault appears in the dropdown, plus a secret-name input and Preview.
    await page.getByTestId("env-var-source-0").selectOption("AzureKeyVault");
    await expect(page.getByTestId("env-var-value-0")).toHaveAttribute("placeholder", "Secret name");
    await expect(page.getByTestId("env-var-vault-0")).toBeVisible();
    await expect(page.getByTestId("env-var-vault-0").locator("option", { hasText: uniqueVaultName })).toHaveCount(1);
    await expect(page.getByTestId("env-var-preview-btn-0")).toBeDisabled();

    await page.getByTestId("env-var-value-0").fill("my-secret-name");
    await expect(page.getByTestId("env-var-preview-btn-0")).toBeEnabled();

    // Switching back to Plain restores the plain value input and hides the vault picker.
    await page.getByTestId("env-var-source-0").selectOption("Plain");
    await expect(page.getByTestId("env-var-value-0")).toHaveAttribute("placeholder", "Value");
    await expect(page.getByTestId("env-var-vault-0")).toHaveCount(0);

    // Removing the vault in Settings takes it out of the list.
    await page.goto("/settings");
    await expect(page.getByTestId("key-vaults-section")).toBeVisible();
    const countBeforeRemove = await page.locator('[data-testid^="kv-name-"]').count();
    await page.getByTestId(`kv-remove-${existingVaultCount}`).click();
    await expect(page.locator('[data-testid^="kv-name-"]')).toHaveCount(countBeforeRemove - 1);
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

    // Select it (filter so the virtualized tree renders it)
    await page.getByTestId("collection-search").fill("Col Var Test Collection");
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

  test("collection export dialog opens from context menu", async ({ page }) => {
    // Create a collection
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Export Test Collection");
    await page.getByTestId("name-dialog-confirm").click();

    // Right-click on the collection to open context menu
    const collectionNode = page.getByTestId(/collection-root-/).first();
    await collectionNode.click({ button: "right" });

    // Click Export in context menu
    await page.getByTestId("ctx-export").click();

    // Export dialog should be visible
    await expect(page.getByTestId("collection-export-dialog")).toBeVisible();

    // Should have format options
    await expect(page.getByTestId("export-format-sweb")).toBeVisible();
    await expect(page.getByTestId("export-format-postman")).toBeVisible();
    await expect(page.getByTestId("export-format-json")).toBeVisible();

    // Should have download button
    await expect(page.getByTestId("export-download-button")).toBeVisible();

    // Close dialog
    await page.getByTestId("export-download-button").click();
    await expect(page.getByTestId("collection-export-dialog")).not.toBeVisible();
  });

  test("bearer token is saved to the secure store and only an opaque key is persisted in collections.json", async ({ page, request }) => {
    const token = `my-secret-bearer-token-${Date.now()}`;
    const collectionName = `Secret Store Collection ${Date.now()}`;
    const requestName = `Secret Store Request ${Date.now()}`;

    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill(collectionName);
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId("collection-search").fill(collectionName);
    await page.getByTestId(/collection-root-/).filter({ hasText: collectionName }).first().click();
    await page.getByTestId("collection-search").fill("");

    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill(requestName);
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId("collection-search").fill(requestName);
    await page.getByTestId(/collection-node-Request-/).filter({ hasText: requestName }).first().click();

    await page.getByTestId("request-tab-auth").click();
    await page.getByTestId("auth-type-select").selectOption("BearerToken");
    await page.getByTestId("auth-bearer-input").fill(token);
    await page.getByTestId("auth-bearer-input").blur();

    await page.getByTestId("request-url-input").fill(`${sidecarBaseUrl}/health`);
    await page.getByTestId("request-save-button").click();
    await page.waitForTimeout(500);

    const sidecarPort = process.env.PLAYWRIGHT_SIDECAR_PORT ?? "5198";
    const response = await request.get(`http://127.0.0.1:${sidecarPort}/api/config/collections`);
    expect(response.ok()).toBeTruthy();
    const collections = await response.json();
    const collection = collections.find((c: any) => c.name === collectionName);
    expect(collection).toBeTruthy();

    function findRequest(nodes: any[]): any | undefined {
      for (const node of nodes) {
        if (node.type === "Request" && node.request) return node.request;
        if (node.children) {
          const found = findRequest(node.children);
          if (found) return found;
        }
      }
      return undefined;
    }

    const req = findRequest(collection.nodes);
    expect(req).toBeTruthy();
    expect(req.auth.type).toBe("BearerToken");
    expect(req.auth.credentialKey).not.toBe(token);
    expect(req.auth.credentialKey).toMatch(/^sw-secret:/);
    expect(JSON.stringify(collections)).not.toContain(token);
  });
});
