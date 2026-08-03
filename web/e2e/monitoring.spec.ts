import { test, expect, type Page } from "@playwright/test";
import { setDemoMode } from "./helpers";

async function createRule(page: Page, name: string) {
  await page.getByTestId("monitoring-add-rule").click();
  await expect(page.getByTestId("alert-rule-dialog")).toBeVisible();
  await page.getByTestId("alert-rule-name").fill(name);
  await page.getByTestId("alert-rule-dialog-save").click();

  const row = page.locator("[data-testid^='monitoring-rule-row-']").filter({ hasText: name });
  await expect(row).toBeVisible();
  return row;
}

test.describe("Monitoring", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("page loads with title and tabs", async ({ page }) => {
    await page.goto("/monitoring");
    await expect(page.getByTestId("monitoring-page")).toBeVisible();
    await expect(page.getByTestId("monitoring-title")).toBeVisible();
    await expect(page.getByTestId("monitoring-tab-rules")).toBeVisible();
    await expect(page.getByTestId("monitoring-tab-history")).toBeVisible();
  });

  test("alert rules are rendered in source groups", async ({ page }) => {
    await page.goto("/monitoring");
    await createRule(page, `Grouped Alert ${Date.now()}`);
    await expect(page.getByTestId("monitoring-rules")).toBeVisible();

    const groups = page.getByTestId("monitoring-rule-groups");
    const emptyState = page.getByTestId("monitoring-rules-empty");
    await expect(groups.or(emptyState)).toBeVisible();

    await expect(groups).toBeVisible();
    await expect(groups.locator("[data-testid^='monitoring-group-']").first()).toBeVisible();
    await expect(groups.locator("[data-testid^='monitoring-rule-row-']").first()).toBeVisible();
  });

  test("toggle rule enabled/disabled", async ({ page }) => {
    await page.goto("/monitoring");
    const row = await createRule(page, `Toggle Alert ${Date.now()}`);
    const toggle = row.locator("[data-testid^='monitoring-rule-toggle-']");
    await expect(toggle).toBeVisible();
    const enabled = await toggle.isChecked();
    await toggle.click();
    await expect(toggle).toHaveJSProperty("checked", !enabled);
  });

  test("add rule opens editor and saves", async ({ page }) => {
    await page.goto("/monitoring");
    await page.getByTestId("monitoring-add-rule").click();
    await expect(page.getByTestId("alert-rule-dialog")).toBeVisible();
    const name = `Playwright Alert ${Date.now()}`;
    await page.getByTestId("alert-rule-name").fill(name);
    await page.getByTestId("alert-rule-dialog-save").click();
    await expect(page.getByTestId("alert-rule-dialog")).not.toBeVisible();
    await expect(page.getByText(name)).toBeVisible();
  });

  test("edit rule opens editor with existing values", async ({ page }) => {
    await page.goto("/monitoring");
    const row = await createRule(page, `Editable Alert ${Date.now()}`);
    await row.locator("[data-testid^='monitoring-rule-edit-']").click();
    await expect(page.getByTestId("alert-rule-dialog")).toBeVisible();
    await expect(page.getByTestId("alert-rule-name")).not.toHaveValue("");
  });

  test("delete rule removes from table", async ({ page }) => {
    await page.goto("/monitoring");
    const name = `Delete Me ${Date.now()}`;
    const row = await createRule(page, name);
    await row.locator("[data-testid^='monitoring-rule-delete-']").click();
    await expect(row).not.toBeVisible();
  });

  test("alert history tab renders the current history state", async ({ page }) => {
    await page.goto("/monitoring");
    await page.getByTestId("monitoring-tab-history").click();
    const history = page.getByTestId("monitoring-history-panel");
    const emptyState = page.getByTestId("monitoring-history-empty");
    await expect(history.or(emptyState)).toBeVisible();

    if (await history.isVisible()) {
      await expect(history.locator("[data-testid^='monitoring-history-row-']").first()).toBeVisible();
    }
  });

  test("snoozing a history event removes it for the session", async ({ page }) => {
    await page.goto("/monitoring");
    await page.getByTestId("monitoring-tab-history").click();
    const history = page.getByTestId("monitoring-history-panel");
    if (!(await history.isVisible())) {
      await expect(page.getByTestId("monitoring-history-empty")).toBeVisible();
      return;
    }

    const row = history.locator("[data-testid^='monitoring-history-row-']").first();
    await expect(row).toBeVisible();
    const rowTestId = await row.getAttribute("data-testid");
    if (!rowTestId) throw new Error("History row is missing its test ID");
    await row.locator("[data-testid^='monitoring-history-snooze-']").click();
    await expect(page.getByTestId(rowTestId)).not.toBeVisible();
  });

  // ── Proactive insights (workspace-intelligence Module 4) ────────────────────

  const insightFrame = {
    kind: "proactiveInsightReady",
    event: {
      ruleId: "rule-1",
      firedAt: "2026-08-03T12:00:00Z",
      ruleName: "Pod restart rate",
      summary: "The pod's restarts line up with a recent spike on the linked Service Bus queue.",
      sessionId: "proactive-rule-1-1754222400000",
    },
  };

  async function mockInsightStream(page: Page) {
    await page.route("**/api/monitoring/stream", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "text/event-stream",
        body: `data: ${JSON.stringify(insightFrame)}\n\n`,
      });
    });
  }

  test("a proactive insight card appears, shows its summary, and Investigate opens it in the AI Agent page", async ({ page }) => {
    await mockInsightStream(page);
    await page.goto("/monitoring");

    const card = page.getByTestId(`proactive-insight-${insightFrame.event.ruleId}-${insightFrame.event.firedAt}`);
    await expect(card).toBeVisible();
    await expect(card).toContainText(insightFrame.event.ruleName);
    await expect(card).toContainText(insightFrame.event.summary);

    await page.getByTestId(`proactive-insight-investigate-${insightFrame.event.ruleId}-${insightFrame.event.firedAt}`).click();

    await expect(page).toHaveURL(/\/agent$/);
    await expect(page.getByTestId("agent-messages")).toContainText(insightFrame.event.ruleName);
    await expect(page.getByTestId("agent-messages")).toContainText(insightFrame.event.summary);
  });

  test("dismissing a proactive insight hides it, and it stays hidden across a reload (per-session de-dup)", async ({ page }) => {
    await mockInsightStream(page);
    await page.goto("/monitoring");

    const card = page.getByTestId(`proactive-insight-${insightFrame.event.ruleId}-${insightFrame.event.firedAt}`);
    await expect(card).toBeVisible();

    await page.getByTestId(`proactive-insight-dismiss-${insightFrame.event.ruleId}-${insightFrame.event.firedAt}`).click();
    await expect(card).toHaveCount(0);

    await page.reload();
    // The same event would be re-delivered by a reconnecting mocked stream, but sessionStorage
    // de-dup (keyed by ruleId+firedAt) must keep it from reappearing after an explicit dismiss.
    await expect(page.getByTestId(`proactive-insight-${insightFrame.event.ruleId}-${insightFrame.event.firedAt}`)).toHaveCount(0);
  });
});
