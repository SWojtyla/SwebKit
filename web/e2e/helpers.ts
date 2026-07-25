import type { Page } from "@playwright/test";
import { expect } from "@playwright/test";

export async function setDemoMode(page: Page, enabled: boolean) {
  await page.goto("/");
  const toggle = page.getByTestId("demo-mode-toggle");
  await toggle.waitFor();
  const text = (await toggle.textContent())?.trim() ?? "";
  const isOn = text === "Demo Mode ON";
  if (enabled && !isOn) {
    await toggle.click();
    await expect(toggle).toContainText("Demo Mode ON");
  } else if (!enabled && isOn) {
    await toggle.click();
    await expect(toggle).toContainText("Enable Demo Mode");
  }
}
