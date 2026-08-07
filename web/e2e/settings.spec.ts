import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Settings", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("all tabs are visible and switch content", async ({ page }) => {
    await page.goto("/settings");
    await expect(page.getByTestId("settings-title")).toHaveText("Settings");

    const tabs = ["general", "service-bus", "aks", "redis", "storage", "agent", "map", "diagnostics", "appearance"];
    for (const id of tabs) {
      await page.getByTestId(`settings-tab-${id}`).click();
      await expect(page.getByTestId("settings-content")).toBeVisible();
    }
  });

  test("general tab shows getting started readiness checklist", async ({ page }) => {
    await page.goto("/settings");
    await expect(page.getByTestId("getting-started-checklist")).toBeVisible();
    await expect(page.getByTestId("getting-started-aks")).toBeVisible();
    await expect(page.getByTestId("getting-started-service-bus")).toBeVisible();
    await expect(page.getByTestId("getting-started-redis")).toBeVisible();
    await expect(page.getByTestId("getting-started-storage")).toBeVisible();
  });

  test("diagnostics tab shows health and logs", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-diagnostics").click();
    await expect(page.getByTestId("diagnostics-settings")).toBeVisible();
    await expect(page.getByTestId("diag-sidecar-status")).toBeVisible();
    await expect(page.getByTestId("diag-log-viewer")).toBeVisible();
  });

  test("diagnostics logging settings persist across reload", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-diagnostics").click();

    const loggingEnabled = page.getByTestId("diagnostics-logging-enabled");
    const loggingLevel = page.getByTestId("diagnostics-logging-level");

    // Default state is enabled/Warning; flip to disabled/Information, wait for the save, and back-check reload.
    await expect(loggingEnabled).toBeChecked();

    const saveUserSettings = (method: string) =>
      page.waitForResponse((r) => r.request().method() === method && r.url().includes("/api/config/user-settings"));

    await Promise.all([saveUserSettings("PUT"), page.getByTestId("diagnostics-logging-enabled").click()]);
    await Promise.all([saveUserSettings("PUT"), loggingLevel.selectOption("Information")]);

    await page.reload();
    await page.getByTestId("settings-tab-diagnostics").click();
    await expect(loggingEnabled).not.toBeChecked();
    await expect(loggingLevel).toHaveValue("Information");
  });

  test("appearance tab shows theme and font options", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-appearance").click();
    await expect(page.getByTestId("appearance-settings")).toBeVisible();
    await expect(page.getByTestId("appearance-theme-dark")).toBeVisible();
    await expect(page.getByTestId("appearance-theme-light")).toBeVisible();
    await expect(page.getByTestId("appearance-theme-fancy")).toBeVisible();
    await expect(page.getByTestId("appearance-font-size")).toBeVisible();
    await expect(page.getByTestId("appearance-density")).toBeVisible();
  });

  test("font size and density persist across reload", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-appearance").click();

    const saveUserSettings = (method: string) =>
      page.waitForResponse((r) => r.request().method() === method && r.url().includes("/api/config/user-settings"));

    await Promise.all([saveUserSettings("PUT"), page.getByTestId("appearance-font-size").selectOption("large")]);
    await Promise.all([saveUserSettings("PUT"), page.getByTestId("appearance-density").selectOption("compact")]);

    await page.reload();
    await page.getByTestId("settings-tab-appearance").click();
    await expect(page.getByTestId("appearance-font-size")).toHaveValue("large");
    await expect(page.getByTestId("appearance-density")).toHaveValue("compact");
  });

  test("agent profile base URL persists across reload", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-agent").click();
    await page.getByTestId("agent-add-profile").click();

    const baseUrlInput = page.getByTestId("agent-profile-base-url-0");
    await baseUrlInput.fill("http://localhost:9999/v1");
    await expect(baseUrlInput).toHaveValue("http://localhost:9999/v1");

    await page.reload();
    await page.getByTestId("settings-tab-agent").click();
    await expect(page.getByTestId("agent-profile-base-url-0")).toHaveValue("http://localhost:9999/v1");
  });

  test("agent profile context window persists across reload, and shows the conservative default when unset", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-agent").click();
    await page.getByTestId("agent-add-profile").click();

    await expect(page.getByTestId("agent-profile-capability-0")).toContainText(
      "unknown context window (using a 4,096-token conservative default)",
    );

    const contextWindowInput = page.getByTestId("agent-profile-context-window-0");
    await contextWindowInput.fill("32000");
    await expect(contextWindowInput).toHaveValue("32000");
    await expect(page.getByTestId("agent-profile-capability-0")).toContainText("32,000-token window");

    await page.reload();
    await page.getByTestId("settings-tab-agent").click();
    await expect(page.getByTestId("agent-profile-context-window-0")).toHaveValue("32000");
  });

  test("Application Insights resource id/name persist across reload — agent-tool-only, no browsing UI", async ({ page }) => {
    // workspace-intelligence: Observability was wired back in as an agent-tool-only capability
    // (get_metrics/query_logs), not a browsing page — this is the one small settings surface it
    // gets, just enough for the agent to know which resource to query.
    await page.goto("/settings");
    await page.getByTestId("settings-tab-agent").click();

    const resourceIdInput = page.getByTestId("observability-resource-id");
    await resourceIdInput.fill("/subscriptions/abc/resourceGroups/rg/providers/microsoft.insights/components/my-app");
    await expect(resourceIdInput).toHaveValue("/subscriptions/abc/resourceGroups/rg/providers/microsoft.insights/components/my-app");

    const resourceNameInput = page.getByTestId("observability-resource-name");
    await resourceNameInput.fill("My App Insights");
    await expect(resourceNameInput).toHaveValue("My App Insights");

    await page.reload();
    await page.getByTestId("settings-tab-agent").click();
    await expect(page.getByTestId("observability-resource-id")).toHaveValue(
      "/subscriptions/abc/resourceGroups/rg/providers/microsoft.insights/components/my-app",
    );
    await expect(page.getByTestId("observability-resource-name")).toHaveValue("My App Insights");
  });

  test("Map tab: a manually-added resource and relationship persist across reload", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-map").click();
    await expect(page.getByTestId("workspace-map-settings")).toBeVisible();

    // Nodes here don't depend on any of the other tabs being configured — the "Add a custom
    // resource" form works even with zero auto-populated candidates, which is the common case for
    // a freshly-provisioned test profile.
    const nodeList = page.getByTestId("workspace-map-nodes");

    await page.getByTestId("workspace-manual-area").selectOption("Aks");
    await page.getByTestId("workspace-manual-key").fill("prod/api");
    await page.getByTestId("workspace-manual-label").fill("api (prod)");
    await page.getByTestId("workspace-manual-add").click();
    await expect(nodeList.getByText("api (prod)")).toBeVisible();

    await page.getByTestId("workspace-manual-area").selectOption("ServiceBus");
    await page.getByTestId("workspace-manual-key").fill("orders.servicebus.windows.net/orders-queue");
    await page.getByTestId("workspace-manual-label").fill("orders queue");
    await page.getByTestId("workspace-manual-add").click();
    await expect(nodeList.getByText("orders queue")).toBeVisible();

    await page.getByTestId("workspace-relationship-from").selectOption({ label: "api (prod)" });
    await page.getByTestId("workspace-relationship-label").fill("consumes");
    await page.getByTestId("workspace-relationship-to").selectOption({ label: "orders queue" });
    await page.getByTestId("workspace-relationship-add").click();

    await expect(page.getByTestId("workspace-map-relationships")).toContainText("api (prod)");
    await expect(page.getByTestId("workspace-map-relationships")).toContainText("consumes");
    await expect(page.getByTestId("workspace-map-relationships")).toContainText("orders queue");

    await page.reload();
    await page.getByTestId("settings-tab-map").click();

    await expect(nodeList.getByText("api (prod)")).toBeVisible();
    await expect(page.getByTestId("workspace-map-relationships")).toContainText("consumes");

    // Removing the node also removes the relationship that referenced it — dangling relationships
    // pointing at a deleted node would be silent, confusing garbage otherwise.
    const nodeRow = page.locator('[data-testid^="workspace-node-"]', { hasText: "api (prod)" });
    await nodeRow.getByRole("button", { name: "Remove" }).click();
    await expect(page.getByTestId("workspace-map-relationships").locator("tbody tr")).toHaveCount(0);
  });

  test("Map tab: a suggested relationship can be confirmed (adds a real relationship) or dismissed (just hides it)", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-map").click();
    const nodeList = page.getByTestId("workspace-map-nodes");

    await page.getByTestId("workspace-manual-area").selectOption("Aks");
    await page.getByTestId("workspace-manual-key").fill("prod/api");
    await page.getByTestId("workspace-manual-label").fill("api (prod)");
    await page.getByTestId("workspace-manual-add").click();
    await expect(nodeList.getByText("api (prod)")).toBeVisible();

    await page.getByTestId("workspace-manual-area").selectOption("ServiceBus");
    // Use a distinct label so this test does not collide with the "orders queue" node
    // left behind by the previous Map tab test, which only removes the AKS node.
    await page.getByTestId("workspace-manual-key").fill("orders.servicebus.windows.net");
    await page.getByTestId("workspace-manual-label").fill("orders queue (suggestion)");
    await page.getByTestId("workspace-manual-add").click();
    await expect(nodeList.getByText("orders queue (suggestion)")).toBeVisible();

    const aksNodeId = await nodeList.locator('[data-testid^="workspace-node-"]', { hasText: "api (prod)" }).getAttribute("data-testid");
    const sbNodeId = await nodeList.locator('[data-testid^="workspace-node-"]', { hasText: "orders queue (suggestion)" }).getAttribute("data-testid");
    const fromNodeId = aksNodeId!.replace("workspace-node-", "");
    const toNodeId = sbNodeId!.replace("workspace-node-", "");

    await page.route("**/api/workspace/topology/suggestions", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            fromNodeId,
            toNodeId,
            reason:
              'Pod config in prod/api contains a value matching "orders queue" — based on matching names in pod configuration; may miss or misidentify real relationships.',
          },
        ]),
      });
    });
    await page.reload();
    await page.getByTestId("settings-tab-map").click();

    const suggestionRow = page.getByTestId(`workspace-suggestion-${fromNodeId}-${toNodeId}`);
    await expect(suggestionRow).toContainText("api (prod)");
    await expect(suggestionRow).toContainText("orders queue");
    await expect(suggestionRow).toContainText("may miss or misidentify real relationships");

    // Dismiss just hides it client-side — no relationship gets added.
    await page.getByTestId(`workspace-suggestion-dismiss-${fromNodeId}-${toNodeId}`).click();
    await expect(suggestionRow).toHaveCount(0);
    await expect(page.getByTestId("workspace-map-relationships").locator("tbody tr")).toHaveCount(0);

    // Reload brings the (still-mocked) suggestion back, since dismissal isn't persisted.
    await page.reload();
    await page.getByTestId("settings-tab-map").click();
    await expect(page.getByTestId(`workspace-suggestion-${fromNodeId}-${toNodeId}`)).toBeVisible();

    // Confirm adds a real, persisted relationship.
    await page.getByTestId(`workspace-suggestion-confirm-${fromNodeId}-${toNodeId}`).click();
    await expect(page.getByTestId("workspace-map-relationships")).toContainText("api (prod)");
    await expect(page.getByTestId("workspace-map-relationships")).toContainText("orders queue");

    await page.reload();
    await page.getByTestId("settings-tab-map").click();
    await expect(page.getByTestId("workspace-map-relationships").locator("tbody tr")).toHaveCount(1);
  });

  test("agent profile no longer exposes temperature/max-tokens, and the History section is gone", async ({ page }) => {
    // Regression coverage for the "AI Agent settings simplification": temperature and max output
    // tokens are the provider's (LM Studio, etc.) job, not a second, silently-conflicting app
    // setting; Max History Messages/Warning Threshold were dead in this app already (the sidecar
    // hardcodes its own history cap) and were removed rather than left as decoration.
    await page.goto("/settings");
    await page.getByTestId("settings-tab-agent").click();
    await page.getByTestId("agent-add-profile").click();

    await expect(page.getByText("Temperature", { exact: true })).not.toBeVisible();
    await expect(page.getByText("Max tokens", { exact: true })).not.toBeVisible();
    await expect(page.getByText("Timeout (s)", { exact: true }).first()).toBeVisible();
    await expect(page.getByText("Max History Messages")).not.toBeVisible();
    await expect(page.getByText("Warning Threshold (%)")).not.toBeVisible();
  });

  test("test connection button reports capability from the sidecar", async ({ page }) => {
    await page.route("**/api/agent/profiles/*/test", async (route) => {
      await route.fulfill({
        json: {
          serverReachable: true,
          modelAvailable: true,
          chatValid: true,
          toolCallingValid: true,
          capability: "ToolCalling",
          diagnostic: "Tool calling supported.",
          availableModels: ["test-model"],
        },
      });
    });

    await page.goto("/settings");
    await page.getByTestId("settings-tab-agent").click();
    await page.getByTestId("agent-add-profile").click();
    await expect(page.getByTestId("agent-profile-capability-0")).toHaveText(/Not tested/);

    await page.getByTestId("agent-profile-test-0").click();
    await expect(page.getByTestId("agent-profile-capability-0")).toHaveText(/Tool calling supported/);
  });

  test("test connection sends the currently-typed field values, not a possibly-stale saved copy", async ({ page }) => {
    // Regression coverage: the settings form auto-saves on every keystroke via a fire-and-forget
    // PUT the UI never awaits, so this test proves "Test connection" sends what's on screen
    // directly in its own request body rather than depending on that save having landed first.
    let capturedBody: Record<string, unknown> | null = null;
    await page.route("**/api/agent/profiles/*/test", async (route) => {
      capturedBody = route.request().postDataJSON();
      await route.fulfill({
        json: {
          serverReachable: true,
          modelAvailable: true,
          chatValid: true,
          toolCallingValid: true,
          capability: "ToolCalling",
          diagnostic: "Tool calling supported.",
          availableModels: ["test-model"],
        },
      });
    });

    await page.goto("/settings");
    await page.getByTestId("settings-tab-agent").click();
    await page.getByTestId("agent-add-profile").click();
    await page.getByTestId("agent-profile-base-url-0").fill("http://localhost:9999/v1");

    await page.getByTestId("agent-profile-test-0").click();
    await expect(page.getByTestId("agent-profile-capability-0")).toHaveText(/Tool calling supported/);

    expect(capturedBody).toMatchObject({ baseUrl: "http://localhost:9999/v1" });
  });

  test("selecting the fancy theme applies it to the document", async ({ page }) => {
    await page.goto("/settings");
    await page.getByTestId("settings-tab-appearance").click();
    await page.getByTestId("appearance-theme-fancy").click();
    await expect(page.locator("html")).toHaveClass(/fancy/);
    await expect(page.locator("html")).not.toHaveClass(/dark/);

    await page.getByTestId("appearance-theme-dark").click();
    await expect(page.locator("html")).toHaveClass(/dark/);
    await expect(page.locator("html")).not.toHaveClass(/fancy/);
  });
});
