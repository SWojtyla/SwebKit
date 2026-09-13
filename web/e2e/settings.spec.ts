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

  test("Service Bus auth mode switches to Entra ID and survives a reload", async ({ page }) => {
    // The reported bug: clicking Entra ID appeared to do nothing. Profile saves were not
    // serialized, so a refetch triggered by an earlier keystroke landed after this click's
    // PUT and overwrote the cache with pre-click state — the radio snapped back.
    //
    // Scoped to the row this test adds: the e2e sidecar's appdata is shared by every test
    // in the file, so namespaces left by earlier tests are still present.
    const saveProfile = (method: string) =>
      page.waitForResponse((r) => r.request().method() === method && r.url().includes("/api/config/profiles"));

    await page.goto("/settings");
    await page.getByTestId("settings-tab-service-bus").click();
    await Promise.all([saveProfile("PUT"), page.getByRole("button", { name: "Add Namespace" }).click()]);

    const entra = page.locator('[data-testid^="sb-auth-entra-"]').last();
    await expect(entra).toBeVisible();
    const testId = await entra.getAttribute("data-testid");
    await expect(entra).not.toBeChecked();

    // Click rather than `check()`: the state change round-trips through a save, and
    // `check()` asserts synchronously right after clicking. Wait for that save's PUT
    // explicitly rather than only polling the checked state, so a slow CI runner can't
    // time out the UI poll before the round-trip that actually flips it has landed.
    await Promise.all([saveProfile("PUT"), entra.click()]);
    await expect(entra).toBeChecked();

    await page.reload();
    await page.getByTestId("settings-tab-service-bus").click();
    await expect(page.getByTestId(testId!)).toBeChecked();
  });

  test("Service Bus text fields commit on blur rather than per keystroke", async ({ page }) => {
    // Typing used to fire a whole-profile PUT, an atomic rewrite of profiles.json and a
    // refetch for every character, which is what made the page sluggish.
    await page.goto("/settings");
    await page.getByTestId("settings-tab-service-bus").click();
    await page.getByRole("button", { name: "Add Namespace" }).click();

    let saves = 0;
    await page.route("**/api/config/profiles", async (route) => {
      if (route.request().method() === "PUT") saves += 1;
      await route.fallback();
    });

    const fqdn = page
      .getByPlaceholder("e.g. sb-dev-shared-sb-weu.servicebus.windows.net")
      .last();
    await fqdn.click();
    await fqdn.pressSequentially("sb-demo.servicebus.windows.net");
    expect(saves).toBe(0);

    await fqdn.blur();
    await expect.poll(() => saves).toBe(1);

    await page.reload();
    await page.getByTestId("settings-tab-service-bus").click();
    await expect(
      page.getByPlaceholder("e.g. sb-dev-shared-sb-weu.servicebus.windows.net").last(),
    ).toHaveValue("sb-demo.servicebus.windows.net");
  });

  test("agent profile base URL persists across reload", async ({ page }) => {
    // Batch 8.4 migrated this field to DraftInput (commit on blur/Enter), matching every
    // other settings section — so the edit must be committed with a blur before reloading.
    // `blur()` alone starts the save but doesn't wait for it — reloading before that PUT
    // actually reaches the sidecar loses the edit, so wait for the real round trip, same as
    // the Service Bus "commit on blur" test below. Agent profiles live under user settings
    // (`/api/config/user-settings`), not the project profile endpoint.
    const saveUserSettings = (method: string) =>
      page.waitForResponse((r) => r.request().method() === method && r.url().includes("/api/config/user-settings"));

    await page.goto("/settings");
    await page.getByTestId("settings-tab-agent").click();
    // Wait for "Add Profile"'s own save too — both it and the field commit below share one
    // `scope: { id: "user-settings" }`-serialized mutation, so committing the field before
    // this one settles would just queue behind it rather than run concurrently.
    await Promise.all([saveUserSettings("PUT"), page.getByTestId("agent-add-profile").click()]);

    const baseUrlInput = page.getByTestId("agent-profile-base-url-0");
    await baseUrlInput.fill("http://localhost:9999/v1");
    await expect(baseUrlInput).toHaveValue("http://localhost:9999/v1");
    await Promise.all([saveUserSettings("PUT"), baseUrlInput.blur()]);

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
    await contextWindowInput.blur();
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

    // Batch 8.4 migrated these to DraftInput (commit on blur/Enter). Both fields share one
    // `scope: { id: "profile" }`-serialized mutation, so the ID field's commit and the Name
    // field's commit run one after another rather than concurrently — the second one is
    // queued behind the first until it settles, and its PUT is only actually sent once that
    // happens. Reloading before that queued request goes out would lose it, so — like the
    // "Service Bus text fields commit on blur" test above — wait for each save's real PUT
    // before moving on, rather than assuming `blur()` alone means it's landed.
    const saveProfile = (method: string) =>
      page.waitForResponse((r) => r.request().method() === method && r.url().includes("/api/config/profiles"));

    const resourceIdInput = page.getByTestId("observability-resource-id");
    await resourceIdInput.fill("/subscriptions/abc/resourceGroups/rg/providers/microsoft.insights/components/my-app");
    await expect(resourceIdInput).toHaveValue("/subscriptions/abc/resourceGroups/rg/providers/microsoft.insights/components/my-app");
    await Promise.all([saveProfile("PUT"), resourceIdInput.blur()]);

    const resourceNameInput = page.getByTestId("observability-resource-name");
    await resourceNameInput.fill("My App Insights");
    await expect(resourceNameInput).toHaveValue("My App Insights");
    await Promise.all([saveProfile("PUT"), resourceNameInput.blur()]);

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
    // pointing at a deleted node would be silent, confusing garbage otherwise. Batch 8.7 added a
    // confirm step (map removals are always "configured" — there's no blank-placeholder state to
    // skip it for), so the removal only takes effect after confirming.
    const nodeRow = page.locator('[data-testid^="workspace-node-"]', { hasText: "api (prod)" });
    const nodeTestId = await nodeRow.getAttribute("data-testid");
    const nodeId = nodeTestId!.replace("workspace-node-", "");
    await nodeRow.getByRole("button", { name: "Remove" }).click();
    await expect(
      page.getByTestId(`workspace-node-remove-confirm-${nodeId}`),
    ).toContainText("1 relationship(s)");
    await page.getByTestId(`workspace-node-remove-confirm-${nodeId}-yes`).click();
    await expect(page.getByTestId("workspace-map-relationships").locator("tbody tr")).toHaveCount(0);
  });

  test("Map tab: a suggested relationship can be confirmed (adds a real relationship) or dismissed (just hides it)", async ({ page }, testInfo) => {
    // A failed attempt leaves its manually-added nodes in the sidecar appdata (which
    // resets per run, not per test), so a retry needs distinct labels or every
    // getByText below becomes a strict-mode violation.
    const sfx = testInfo.retry > 0 ? ` r${testInfo.retry}` : "";
    const aksLabel = `api (prod)${sfx}`;
    const sbLabel = `orders queue (suggestion)${sfx}`;

    await page.goto("/settings");
    await page.getByTestId("settings-tab-map").click();
    const nodeList = page.getByTestId("workspace-map-nodes");

    await page.getByTestId("workspace-manual-area").selectOption("Aks");
    await page.getByTestId("workspace-manual-key").fill(`prod/api${sfx.replace(" ", "-")}`);
    await page.getByTestId("workspace-manual-label").fill(aksLabel);
    await page.getByTestId("workspace-manual-add").click();
    await expect(nodeList.getByText(aksLabel)).toBeVisible();

    await page.getByTestId("workspace-manual-area").selectOption("ServiceBus");
    // Use a distinct label so this test does not collide with the "orders queue" node
    // left behind by the previous Map tab test, which only removes the AKS node.
    await page.getByTestId("workspace-manual-key").fill(`orders.servicebus.windows.net${sfx}`);
    await page.getByTestId("workspace-manual-label").fill(sbLabel);
    await page.getByTestId("workspace-manual-add").click();
    await expect(nodeList.getByText(sbLabel)).toBeVisible();

    const aksNodeId = await nodeList.locator('[data-testid^="workspace-node-"]', { hasText: aksLabel }).getAttribute("data-testid");
    const sbNodeId = await nodeList.locator('[data-testid^="workspace-node-"]', { hasText: sbLabel }).getAttribute("data-testid");
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
    await expect(suggestionRow).toContainText(aksLabel);
    await expect(suggestionRow).toContainText("orders queue");
    await expect(suggestionRow).toContainText("may miss or misidentify real relationships");

    // Dismiss just hides it client-side — no relationship gets added.
    await page.getByTestId(`workspace-suggestion-dismiss-${fromNodeId}-${toNodeId}`).click();
    await expect(suggestionRow).toHaveCount(0);
    const relRows = page.getByTestId("workspace-map-relationships").locator("tbody tr");
    const pairRow = relRows.filter({ hasText: sbLabel });
    await expect(pairRow).toHaveCount(0);

    // Reload brings the (still-mocked) suggestion back, since dismissal isn't persisted.
    await page.reload();
    await page.getByTestId("settings-tab-map").click();
    await expect(page.getByTestId(`workspace-suggestion-${fromNodeId}-${toNodeId}`)).toBeVisible();

    // Confirm adds a real, persisted relationship. Assert on the table row itself —
    // the mocked endpoint keeps returning the suggestion and the From/To options
    // echo both labels, so container text can't prove the profile PUT settled
    // before the reload.
    await page.getByTestId(`workspace-suggestion-confirm-${fromNodeId}-${toNodeId}`).click();
    await expect(pairRow).toHaveCount(1);
    await expect(pairRow).toContainText(aksLabel);

    await page.reload();
    await page.getByTestId("settings-tab-map").click();
    await expect(pairRow).toHaveCount(1);
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

  // ── Batch 8 (Settings) ───────────────────────────────────────────────────

  test("Service Bus Connection String mode exposes a credential-key field that persists (unit 8.1)", async ({ page }) => {
    // Previously `credentialKey` was initialized on a new namespace but never bound to any
    // input, so Connection String auth could not actually be configured through the UI.
    const saveProfile = (method: string) =>
      page.waitForResponse((r) => r.request().method() === method && r.url().includes("/api/config/profiles"));

    await page.goto("/settings");
    await page.getByTestId("settings-tab-service-bus").click();
    await Promise.all([saveProfile("PUT"), page.getByRole("button", { name: "Add Namespace" }).click()]);

    const connString = page.locator('[data-testid^="sb-auth-connstring-"]').last();
    await expect(connString).toBeChecked();

    const credKeyTestId = await page.locator('[data-testid^="sb-credential-key-"]').last().getAttribute("data-testid");
    const credKey = page.getByTestId(credKeyTestId!);
    await expect(credKey).toBeVisible();
    await credKey.fill("sb-conn-my-namespace");
    // Wait for the real round trip, not just the local `blur()` call, before reloading —
    // reloading before the PUT actually reaches the sidecar would lose the edit.
    await Promise.all([saveProfile("PUT"), credKey.blur()]);

    await page.reload();
    await page.getByTestId("settings-tab-service-bus").click();
    await expect(page.getByTestId(credKeyTestId!)).toHaveValue("sb-conn-my-namespace");
  });

  test("Test connection is available for AKS, Service Bus, Redis, and Storage (unit 8.2)", async ({ page }) => {
    await page.route("**/api/aks/test", (route) => route.fulfill({ json: { connected: true } }));
    await page.route("**/api/servicebus/*/test", (route) => route.fulfill({ json: { connected: true } }));
    await page.route("**/api/redis/*/test", (route) => route.fulfill({ json: { connected: false, error: "timeout" } }));
    await page.route("**/api/storage/*/test", (route) => route.fulfill({ json: { connected: true } }));

    await page.goto("/settings");

    await page.getByTestId("settings-tab-aks").click();
    await page.getByTestId("aks-test-connection").click();
    await expect(page.getByTestId("aks-test-result")).toHaveText("Connected");

    await page.getByTestId("settings-tab-service-bus").click();
    await page.getByRole("button", { name: "Add Namespace" }).click();
    await page.locator('[data-testid^="sb-test-connection-"]').last().click();
    await expect(page.locator('[data-testid^="sb-test-result-"]').last()).toHaveText("Connected");

    await page.getByTestId("settings-tab-redis").click();
    await page.getByRole("button", { name: "Add Cache" }).click();
    await page.locator('[data-testid^="redis-test-connection-"]').last().click();
    await expect(page.locator('[data-testid^="redis-test-result-"]').last()).toHaveText("Failed: timeout");

    await page.getByTestId("settings-tab-storage").click();
    await page.getByRole("button", { name: "Add Account" }).click();
    await page.locator('[data-testid^="storage-test-connection-"]').last().click();
    await expect(page.locator('[data-testid^="storage-test-result-"]').last()).toHaveText("Connected");
  });

  test("AKS auto-refresh interval and Redis database index clamp out-of-range values (unit 8.5)", async ({ page }) => {
    // `parseInt(v) || default` only caught falsy results, so a negative number passed through
    // unchanged and only surfaced later as an opaque connection failure.
    await page.goto("/settings");

    await page.getByTestId("settings-tab-aks").click();
    const interval = page.getByTestId("aks-auto-refresh-interval");
    await interval.fill("-5");
    await interval.blur();
    await expect(interval).toHaveValue("5");
    await expect(page.getByTestId("notification-toasts")).toContainText("out of range");

    await page.getByTestId("settings-tab-redis").click();
    await page.getByRole("button", { name: "Add Cache" }).click();
    const database = page.locator('[data-testid^="redis-database-"]').last();
    await database.fill("99");
    await database.blur();
    await expect(database).toHaveValue("15");
  });

  test("removing a configured Redis cache requires confirmation; an untouched one does not (unit 8.7)", async ({ page }) => {
    // The confirm-or-not decision reads the saved profile, not just the local input value —
    // wait for the connection-string save to actually land before asking for its removal.
    const saveProfile = (method: string) =>
      page.waitForResponse((r) => r.request().method() === method && r.url().includes("/api/config/profiles"));

    await page.goto("/settings");
    await page.getByTestId("settings-tab-redis").click();

    // A freshly-added, still-blank cache has nothing to lose — no confirm needed.
    await Promise.all([saveProfile("PUT"), page.getByRole("button", { name: "Add Cache" }).click()]);
    const blankRemove = page.locator('[data-testid^="redis-remove-"]').last();
    const blankTestId = await blankRemove.getAttribute("data-testid");
    const blankCacheId = blankTestId!.replace("redis-remove-", "");
    await blankRemove.click();
    await expect(page.getByTestId(`redis-cache-${blankCacheId}`)).toHaveCount(0);

    // A cache with a real connection string is worth confirming before it's gone. Wait for
    // "Add Cache"'s own save too — it and the connection-string commit below share one
    // `scope: { id: "profile" }`-serialized mutation, so committing the field before this
    // one settles would just queue behind it rather than run concurrently.
    await Promise.all([saveProfile("PUT"), page.getByRole("button", { name: "Add Cache" }).click()]);
    const connInput = page.locator('[data-testid^="redis-cache-"] input[placeholder="localhost:6379"]').last();
    await connInput.fill("localhost:6379");
    await Promise.all([saveProfile("PUT"), connInput.blur()]);

    const configuredRemove = page.locator('[data-testid^="redis-remove-"]').last();
    const configuredTestId = await configuredRemove.getAttribute("data-testid");
    const configuredCacheId = configuredTestId!.replace("redis-remove-", "");
    await configuredRemove.click();
    await expect(page.getByTestId(`redis-cache-${configuredCacheId}`)).toBeVisible();
    await expect(page.getByTestId(`redis-remove-confirm-${configuredCacheId}`)).toBeVisible();

    await page.getByTestId(`redis-remove-confirm-${configuredCacheId}-cancel`).click();
    await expect(page.getByTestId(`redis-cache-${configuredCacheId}`)).toBeVisible();

    await page.getByTestId(`redis-remove-${configuredCacheId}`).click();
    await page.getByTestId(`redis-remove-confirm-${configuredCacheId}-yes`).click();
    await expect(page.getByTestId(`redis-cache-${configuredCacheId}`)).toHaveCount(0);
  });

  test("settings tabs show a configured/not-configured readiness signal (unit 8.9)", async ({ page }) => {
    await page.goto("/settings");
    for (const id of ["aks", "service-bus", "redis", "storage"]) {
      await expect(page.getByTestId(`settings-tab-readiness-${id}`)).toBeVisible();
    }
    // General/Agent/Map/Diagnostics/Appearance have no "configured" concept and show no dot.
    await expect(page.getByTestId("settings-tab-readiness-general")).toHaveCount(0);
  });
});
