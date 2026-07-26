import type { Page } from "@playwright/test";
import { expect } from "@playwright/test";

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
