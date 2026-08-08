import type { Page } from "@playwright/test";
import { expect } from "@playwright/test";
import { sidecarPort } from "./test-config";

const sidecarUrl = `http://127.0.0.1:${sidecarPort}`;

/**
 * Scrolls a virtualized list container (built with @tanstack/react-virtual) step by step until
 * the row identified by `targetTestId` is mounted in the DOM. Virtualized lists only render rows
 * within the visible window (+ overscan), so a row further down the list simply doesn't exist in
 * the DOM until the container is scrolled toward it — `scrollIntoViewIfNeeded()` can't help here
 * because there's nothing to scroll to yet.
 */
export async function scrollVirtualListIntoView(
  page: Page,
  containerTestId: string,
  targetTestId: string,
) {
  const container = page.getByTestId(containerTestId);
  const target = page.getByTestId(targetTestId);

  for (let i = 0; i < 60; i++) {
    if ((await target.count()) > 0) break;
    const reachedEnd = await container.evaluate((el) => {
      const before = el.scrollTop;
      el.scrollTop = before + el.clientHeight;
      return el.scrollTop === before;
    });
    await page.waitForTimeout(30);
    if (reachedEnd) break;
  }

  await expect(target).toBeVisible();
  return target;
}

/**
 * Mocks POST /api/agent/chat/stream with a single "done" SSE event carrying `reply` — the
 * simplest valid stream a test can fake (no token events first), since AgentPage/ContextualAssistant
 * both treat a "done" event's `result` as the source of truth regardless of what streamed before it.
 * Use this instead of hand-rolling the SSE `data: ...\n\n` framing in every spec.
 */
export async function mockAgentChatStreamDone(
  page: Page,
  reply: {
    text: string;
    elapsedMs?: number;
    status?: string;
    error?: boolean;
    steps?: { type: string; toolName?: string; summary?: string; elapsed?: string }[];
    summarized?: boolean;
    contextUsagePercent?: number;
  },
) {
  const event = {
    kind: "done",
    result: {
      text: reply.text,
      elapsedMs: reply.elapsedMs ?? 1,
      status: reply.status ?? "done",
      error: reply.error ?? false,
      steps: reply.steps ?? [],
      summarized: reply.summarized ?? false,
      contextUsagePercent: reply.contextUsagePercent ?? 0,
    },
  };
  await page.route("**/api/agent/chat/stream", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "text/event-stream",
      body: `data: ${JSON.stringify(event)}\n\n`,
    });
  });
}

async function getSidecarDemoMode(page: Page): Promise<boolean> {
  const res = await page.request.get(`${sidecarUrl}/api/demo-mode`);
  const body = (await res.json()) as { isDemoMode?: boolean };
  return body.isDemoMode === true;
}

async function setSidecarDemoMode(page: Page, enabled: boolean): Promise<void> {
  await page.request.post(`${sidecarUrl}/api/demo-mode?enabled=${enabled}`);
}

async function waitForDemoProfile(page: Page, enabled: boolean, timeout = 10_000): Promise<void> {
  const expected = enabled ? 2 : 0;
  const started = Date.now();
  while (Date.now() - started < timeout) {
    const res = await page.request.get(`${sidecarUrl}/api/config/profiles`);
    const body = (await res.json()) as { serviceBusNamespaces?: unknown[] };
    const count = body.serviceBusNamespaces?.length ?? 0;
    if (count === expected) return;
    await page.waitForTimeout(200);
  }
  throw new Error(`Timed out waiting for demo profile (enabled=${enabled})`);
}

export async function setDemoMode(page: Page, enabled: boolean) {
  // Drive the sidecar directly so tests don't depend on the UI's optimistic
  // mutation state or React Query cache timing.
  const currentlyOn = await getSidecarDemoMode(page);
  if (currentlyOn !== enabled) {
    await setSidecarDemoMode(page, enabled);
    if (enabled) {
      // Demo mode overlays exactly 2 Service Bus namespaces, so this is a
      // reliable signal the sidecar is serving the demo profile.
      await waitForDemoProfile(page, true);
    }
  }

  await page.goto("/");
  const toggle = page.getByTestId("demo-mode-toggle");
  await toggle.waitFor();

  // Force the UI to converge on the correct state. The toggle reads from
  // /api/demo-mode and the dashboard card from /api/config/profiles.
  await expect(toggle).toContainText(enabled ? "Demo" : "Live", { timeout: 10000 });
  if (enabled) {
    await expect(page.getByTestId("service-card-service-bus")).toContainText(
      "2 namespaces",
      { timeout: 10000 },
    );
  }
}

/**
 * Waits for the AKS namespace selector to be populated with "default" and
 * then selects it. The selector is a hidden native `<select>`, so this waits
 * for the option to exist before calling `selectOption`.
 */
export async function selectAksDefaultNamespace(page: Page) {
  const select = page.getByTestId("aks-namespace-select");
  await expect(select.locator("option", { hasText: "default" })).toBeAttached({ timeout: 15000 });
  await select.selectOption({ label: "default" });
}

/**
 * Resets the API client collections store to an empty state through the sidecar.
 * Use in test setup to keep drag-and-drop and ordering specs isolated from earlier tests.
 */
export async function resetCollections(page: Page) {
  await page.request.put(`${sidecarUrl}/api/config/collections`, {
    data: { schemaVersion: 1, collections: [] },
  });
}
