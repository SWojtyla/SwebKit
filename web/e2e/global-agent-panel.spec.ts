import { test, expect } from "@playwright/test";
import { mockAgentChatStreamDone, setDemoMode } from "./helpers";

test.describe("Global AI Agent panel", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });
  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("toggle button opens the panel from any page, and is hidden on the dedicated /agent page", async ({ page }) => {
    await page.goto("/aks");
    await expect(page.getByTestId("global-agent-panel-toggle")).toBeVisible();

    await page.getByTestId("global-agent-panel-toggle").click();
    await expect(page.getByTestId("global-agent-panel")).toBeVisible();

    await page.getByTestId("global-agent-panel-close").click();
    await expect(page.getByTestId("global-agent-panel")).not.toBeVisible();

    // The /agent page already shows this exact conversation full-page — no need for the toggle
    // (or an already-open panel) to duplicate it there.
    await page.getByTestId("nav-ai-agent").click();
    await expect(page.getByTestId("global-agent-panel-toggle")).not.toBeVisible();
  });

  test("stays open while interacting with the rest of the app — it's a docked panel, not a click-outside-to-close overlay", async ({ page }) => {
    // Regression test: the first version rendered as a `fixed inset-0` overlay with a backdrop
    // that closed the panel on any click outside it — defeating the point of a panel meant to
    // stay open while you keep working elsewhere. It's now a real docked sibling in the layout,
    // like the left nav, and should only close via its own "✕", the toggle, or the shortcut.
    await page.goto("/aks");
    await page.getByTestId("global-agent-panel-toggle").click();
    await expect(page.getByTestId("global-agent-panel")).toBeVisible();

    await page.getByTestId("aks-tab-pods").click();
    await expect(page.getByTestId("global-agent-panel")).toBeVisible();

    await page.getByTestId("context-title").click();
    await expect(page.getByTestId("global-agent-panel")).toBeVisible();
  });

  test("Ctrl+Shift+L toggles the panel open and closed", async ({ page }) => {
    await page.goto("/aks");
    // Wait for the shell (and its keydown listener) to actually mount before sending the shortcut
    // — page.goto() is a hard navigation, so without this the keypress can race React's effects.
    await expect(page.getByTestId("global-agent-panel-toggle")).toBeVisible();

    await page.keyboard.press("Control+Shift+l");
    await expect(page.getByTestId("global-agent-panel")).toBeVisible();

    await page.keyboard.press("Control+Shift+l");
    await expect(page.getByTestId("global-agent-panel")).not.toBeVisible();
  });

  test("conversation survives navigating away from and back to the /agent page", async ({ page }) => {
    // Regression test for the reported bug: AgentPage used to keep its transcript in local
    // component state, which react-router tears down on unmount — the message list vanished on
    // return even though the backend's session (and its historyCount) never actually reset.
    await mockAgentChatStreamDone(page, { text: "Pod is healthy." });

    await page.goto("/agent");
    await page.getByTestId("agent-input").fill("status?");
    await page.getByTestId("agent-send").click();
    await expect(page.getByTestId("agent-messages")).toContainText("Pod is healthy.");

    await page.getByTestId("nav-aks").click();
    await expect(page.getByTestId("aks-page")).toBeVisible();

    await page.getByTestId("nav-ai-agent").click();
    await expect(page.getByTestId("agent-empty")).not.toBeVisible();
    await expect(page.getByTestId("agent-messages")).toContainText("Pod is healthy.");
  });

  test("messages sent from the docked panel appear on the /agent page too — one shared conversation", async ({ page }) => {
    await mockAgentChatStreamDone(page, { text: "42 pods running." });

    await page.goto("/aks");
    await page.getByTestId("global-agent-panel-toggle").click();
    await page.getByTestId("global-agent-panel-input").fill("how many pods?");
    await page.getByTestId("global-agent-panel-send").click();
    await expect(page.getByTestId("global-agent-panel-messages")).toContainText("42 pods running.");

    await page.getByTestId("global-agent-panel-close").click();
    await page.getByTestId("nav-ai-agent").click();

    await expect(page.getByTestId("agent-messages")).toContainText("42 pods running.");
  });
});
