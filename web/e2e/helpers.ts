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
