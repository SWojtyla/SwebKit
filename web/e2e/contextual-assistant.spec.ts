import { test, expect } from "@playwright/test";
import { setDemoMode, scrollVirtualListIntoView } from "./helpers";

/** Captures every /api/agent/chat request body sent while this route is installed. */
function captureChatRequests(page: import("@playwright/test").Page) {
  const bodies: Record<string, unknown>[] = [];
  return {
    bodies,
    install: () =>
      page.route("**/api/agent/chat", async (route) => {
        bodies.push(route.request().postDataJSON());
        await route.fulfill({ json: { text: "ok", elapsedMs: 1, status: "done", error: false } });
      }),
  };
}

test.describe("Contextual assistant entry points", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });
  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("AKS pod context menu opens the assistant scoped to Aks", async ({ page }) => {
    const chat = captureChatRequests(page);
    await chat.install();

    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption({ label: "default" });
    await page.getByTestId("aks-tab-pods").click();
    const firstRow = page.getByTestId("pods-table-body").locator("tr").first();
    await firstRow.click({ button: "right" });
    await page.getByTestId("ctx-item-ask-ai-about-this-pod").click();

    await expect(page.getByTestId("contextual-assistant-panel")).toBeVisible();
    await page.getByTestId("contextual-assistant-input").fill("why is this pod restarting?");
    await page.getByTestId("contextual-assistant-send").click();

    await expect.poll(() => chat.bodies.length).toBe(1);
    expect(chat.bodies[0]).toMatchObject({ mode: "ask", context: { featureArea: "Aks" } });
  });

  test("Redis key detail opens the assistant scoped to Redis with the key in the selection", async ({ page }) => {
    const chat = captureChatRequests(page);
    await chat.install();

    await page.goto("/redis");
    await expect(page.getByTestId("redis-key-browser")).toBeVisible();
    await scrollVirtualListIntoView(page, "redis-key-tree-scroll", "redis-key-user:1001");
    await page.getByTestId("redis-key-user:1001").click();
    await page.getByTestId("redis-ask-ai-btn").click();

    await expect(page.getByTestId("contextual-assistant-panel")).toBeVisible();
    await page.getByTestId("contextual-assistant-input").fill("what is this key used for?");
    await page.getByTestId("contextual-assistant-send").click();

    await expect.poll(() => chat.bodies.length).toBe(1);
    const body = chat.bodies[0] as { context: { featureArea: string; selection: Record<string, string> } };
    expect(body.context.featureArea).toBe("Redis");
    expect(body.context.selection.key).toBeTruthy();
  });

  test("Storage blob detail opens the assistant scoped to Storage", async ({ page }) => {
    const chat = captureChatRequests(page);
    await chat.install();

    await page.goto("/storage");
    await page.getByTestId("storage-container-configs").click();
    await page.getByTestId("storage-item-app-settings.json").click();
    await page.getByTestId("storage-ask-ai-btn").click();

    await expect(page.getByTestId("contextual-assistant-panel")).toBeVisible();
    await page.getByTestId("contextual-assistant-input").fill("when was this last modified?");
    await page.getByTestId("contextual-assistant-send").click();

    await expect.poll(() => chat.bodies.length).toBe(1);
    const body = chat.bodies[0] as { context: { featureArea: string; selection: Record<string, string> } };
    expect(body.context.featureArea).toBe("Storage");
    expect(body.context.selection.blob).toBe("app-settings.json");
  });

  test("Service Bus entity breadcrumb opens the assistant scoped to ServiceBus", async ({ page }) => {
    const chat = captureChatRequests(page);
    await chat.install();

    await page.goto("/service-bus");
    await page.getByTestId("sb-namespace-select").selectOption({ label: "orders-dev" });
    await page.getByTestId("entity-tree-queue-order-created").click();
    await page.getByTestId("sb-ask-ai-btn").click();

    await expect(page.getByTestId("contextual-assistant-panel")).toBeVisible();
    await page.getByTestId("contextual-assistant-input").fill("why is this queue backing up?");
    await page.getByTestId("contextual-assistant-send").click();

    await expect.poll(() => chat.bodies.length).toBe(1);
    const body = chat.bodies[0] as { context: { featureArea: string } };
    expect(body.context.featureArea).toBe("ServiceBus");
  });

  test("Monitoring alert row derives feature area from the rule's signal source, not a literal Monitoring area", async ({ page }) => {
    const chat = captureChatRequests(page);
    await chat.install();

    await page.goto("/monitoring");
    // No rule exists until one is created — the dialog's default source is AksPodHealth.
    await page.getByTestId("monitoring-add-rule").click();
    await expect(page.getByTestId("alert-rule-dialog")).toBeVisible();
    const ruleName = `Ctx Test Alert ${Date.now()}`;
    await page.getByTestId("alert-rule-name").fill(ruleName);
    await page.getByTestId("alert-rule-dialog-save").click();

    const row = page.locator("[data-testid^='monitoring-rule-row-']").filter({ hasText: ruleName });
    await expect(row).toBeVisible();
    await row.locator("[data-testid^='monitoring-rule-ask-ai-']").click();

    await expect(page.getByTestId("contextual-assistant-panel")).toBeVisible();
    await page.getByTestId("contextual-assistant-input").fill("explain this alert");
    await page.getByTestId("contextual-assistant-send").click();

    await expect.poll(() => chat.bodies.length).toBe(1);
    const body = chat.bodies[0] as { context: { featureArea: string } };
    // Never the literal string "Monitoring" — there's no such backend FeatureArea; it must be
    // derived from the rule's source (AksPodHealth here) instead.
    expect(body.context.featureArea).toBe("Aks");
  });

  test("mode toggle switches the request from ask to ask_and_do", async ({ page }) => {
    const chat = captureChatRequests(page);
    await chat.install();

    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption({ label: "default" });
    await page.getByTestId("aks-tab-pods").click();
    const firstRow = page.getByTestId("pods-table-body").locator("tr").first();
    await firstRow.click({ button: "right" });
    await page.getByTestId("ctx-item-ask-ai-about-this-pod").click();

    await page.getByTestId("contextual-assistant-mode-ask-and-do").click();
    await page.getByTestId("contextual-assistant-input").fill("restart this pod");
    await page.getByTestId("contextual-assistant-send").click();

    await expect.poll(() => chat.bodies.length).toBe(1);
    expect(chat.bodies[0]).toMatchObject({ mode: "ask_and_do" });
  });

  test("closing the panel removes it from the DOM", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption({ label: "default" });
    await page.getByTestId("aks-tab-pods").click();
    const firstRow = page.getByTestId("pods-table-body").locator("tr").first();
    await firstRow.click({ button: "right" });
    await page.getByTestId("ctx-item-ask-ai-about-this-pod").click();

    await expect(page.getByTestId("contextual-assistant-panel")).toBeVisible();
    await page.getByTestId("contextual-assistant-close").click();
    await expect(page.getByTestId("contextual-assistant-panel")).not.toBeVisible();
  });
});

test.describe("API Client generate-request flow", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("submitting a description sends an ask_and_do message targeting the open request", async ({ page }) => {
    const chat = captureChatRequests(page);
    await chat.install();
    await page.route("**/api/agent/pending-approvals", async (route) => {
      await route.fulfill({ json: [] });
    });

    await page.goto("/api-client");
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Test Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().waitFor();
    await page.getByTestId(/collection-root-/).first().click();
    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("New Request");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-node-Request-/).first().click();

    await page.getByTestId("request-ask-ai-button").click();
    await expect(page.getByTestId("generate-api-request-panel")).toBeVisible();

    await page.getByTestId("generate-api-request-input").fill("POST to /login with a JSON body");
    await page.getByTestId("generate-api-request-submit").click();

    await expect.poll(() => chat.bodies.length).toBe(1);
    const body = chat.bodies[0] as { mode: string; message: string; context: { featureArea: string } };
    expect(body.mode).toBe("ask_and_do");
    expect(body.context.featureArea).toBe("ApiClient");
    expect(body.message).toContain("propose_api_request_change");
  });

  test("shows the confirm card once a proposal comes back", async ({ page }) => {
    await page.route("**/api/agent/chat", async (route) => {
      await route.fulfill({ json: { text: "Proposed a change.", elapsedMs: 1, status: "done", error: false } });
    });
    await page.route("**/api/agent/pending-approvals", async (route) => {
      await route.fulfill({
        json: [{
          id: "action-1",
          type: "UpdateRequest",
          summary: "Update request to POST /login",
          target: "Request 'New Request' (r1)",
          risk: "Low",
          preview: "method: Get -> Post\nurl: -> /login",
          expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
        }],
      });
    });

    await page.goto("/api-client");
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("Test Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().waitFor();
    await page.getByTestId(/collection-root-/).first().click();
    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("New Request");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-node-Request-/).first().click();

    await page.getByTestId("request-ask-ai-button").click();
    await page.getByTestId("generate-api-request-input").fill("POST to /login");
    await page.getByTestId("generate-api-request-submit").click();

    await expect(page.getByTestId("pending-action-action-1")).toBeVisible();
  });
});
