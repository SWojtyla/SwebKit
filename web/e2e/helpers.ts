import type { Page } from "@playwright/test";
import { expect } from "@playwright/test";

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
  reply: { text: string; elapsedMs?: number; status?: string; error?: boolean },
) {
  const event = {
    kind: "done",
    result: {
      text: reply.text,
      elapsedMs: reply.elapsedMs ?? 1,
      status: reply.status ?? "done",
      error: reply.error ?? false,
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

export async function setDemoMode(page: Page, enabled: boolean) {
  await page.goto("/");
  const toggle = page.getByTestId("demo-mode-toggle");
  await toggle.waitFor();

  // Wait for the demo mode query to resolve and render one of the two stable labels.
  // The top-bar toggle (AppLayout) reads "Demo" when on, "Live" when off, or "..."
  // while the mutation is in flight.
  await expect(toggle).toContainText(/Demo|Live/);

  const text = (await toggle.textContent())?.trim() ?? "";
  const isOn = text.includes("Demo");
  if (enabled && !isOn) {
    await toggle.click();
    await expect(toggle).toContainText("Demo");
  } else if (!enabled && isOn) {
    await toggle.click();
    await expect(toggle).toContainText("Live");
  }
}
