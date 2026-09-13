import { test, expect, type Page } from "@playwright/test";
import { setDemoMode } from "./helpers";

async function createRule(page: Page, name: string) {
  await page.getByTestId("monitoring-add-rule").click();
  await expect(page.getByTestId("alert-rule-dialog")).toBeVisible();
  await page.getByTestId("alert-rule-name").fill(name);
  // Default source is AKS · Pod Health, which (unit 5.3) now requires a namespace before Save
  // is enabled — pick one of the demo namespaces so the helper produces a rule that could
  // actually evaluate, rather than the historical name-only placeholder rule.
  const aksNamespace = page.getByTestId("alert-rule-aks-namespace");
  await expect(aksNamespace.locator("option", { hasText: "ecommerce" })).toBeAttached({ timeout: 10000 });
  await aksNamespace.selectOption("ecommerce");
  await expect(page.getByTestId("alert-rule-dialog-save")).toBeEnabled();
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

  // ── Unit 5.4 — collapse state survives a tab switch ──────────────────────────

  test("a rule group's collapsed state survives switching to the History tab and back", async ({ page }) => {
    await page.goto("/monitoring");
    await createRule(page, `Collapse Persist Alert ${Date.now()}`);

    const groupToggle = page.getByTestId("monitoring-group-toggle-AKS");
    const groupRows = page.getByTestId("monitoring-group-AKS").locator("[data-testid^='monitoring-rule-row-']");
    await expect(groupToggle).toBeVisible();
    await expect(groupRows.first()).toBeVisible();

    await groupToggle.click();
    await expect(groupRows).toHaveCount(0);

    await page.getByTestId("monitoring-tab-history").click();
    await page.getByTestId("monitoring-tab-rules").click();

    // AlertRuleGroups unmounts entirely while History is active — if collapse state lived only
    // as local state inside it (the pre-fix behavior), it would reset to expanded here.
    await expect(groupRows).toHaveCount(0);
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

  // ── Unit 5.1 — row click + confirm/undo ──────────────────────────────────────

  test("clicking a rule's name/detail area (not just the edit icon) opens the editor", async ({ page }) => {
    await page.goto("/monitoring");
    const name = `Row Click Alert ${Date.now()}`;
    const row = await createRule(page, name);

    await row.locator("[data-testid^='monitoring-rule-open-']").click();
    await expect(page.getByTestId("alert-rule-dialog")).toBeVisible();
    await expect(page.getByTestId("alert-rule-name")).toHaveValue(name);
  });

  test("disabling a rule shows an Undo toast that re-enables it", async ({ page }) => {
    await page.goto("/monitoring");
    const row = await createRule(page, `Undo Alert ${Date.now()}`);
    const toggle = row.locator("[data-testid^='monitoring-rule-toggle-']");
    await expect(toggle).toBeChecked();

    await toggle.click();
    await expect(toggle).not.toBeChecked();

    const undo = page.locator("[data-testid^='notification-action-']");
    await expect(undo).toBeVisible();
    await expect(undo).toHaveText("Undo");
    await undo.click();

    await expect(toggle).toBeChecked();
  });

  test("add rule opens editor and saves", async ({ page }) => {
    await page.goto("/monitoring");
    await page.getByTestId("monitoring-add-rule").click();
    await expect(page.getByTestId("alert-rule-dialog")).toBeVisible();
    const name = `Playwright Alert ${Date.now()}`;
    await page.getByTestId("alert-rule-name").fill(name);
    const aksNamespace = page.getByTestId("alert-rule-aks-namespace");
    await expect(aksNamespace.locator("option", { hasText: "ecommerce" })).toBeAttached({ timeout: 10000 });
    await aksNamespace.selectOption("ecommerce");
    await expect(page.getByTestId("alert-rule-dialog-save")).toBeEnabled();
    await page.getByTestId("alert-rule-dialog-save").click();
    await expect(page.getByTestId("alert-rule-dialog")).not.toBeVisible();
    await expect(page.getByText(name)).toBeVisible();
  });

  // ── Unit 5.3 — require working configuration before save ────────────────────

  test("Save stays disabled until the selected source's required fields are filled in", async ({ page }) => {
    await page.goto("/monitoring");
    await page.getByTestId("monitoring-add-rule").click();
    await expect(page.getByTestId("alert-rule-dialog")).toBeVisible();

    const save = page.getByTestId("alert-rule-dialog-save");
    const hint = page.getByTestId("alert-rule-dialog-incomplete-hint");

    // Name alone (the old, only check) is no longer enough — the default source (AKS · Pod
    // Health) also needs a namespace or the rule would silently never evaluate.
    await page.getByTestId("alert-rule-name").fill(`Incomplete Alert ${Date.now()}`);
    await expect(save).toBeDisabled();
    await expect(hint).toBeVisible();

    const aksNamespace = page.getByTestId("alert-rule-aks-namespace");
    await expect(aksNamespace.locator("option", { hasText: "ecommerce" })).toBeAttached({ timeout: 10000 });
    await aksNamespace.selectOption("ecommerce");
    await expect(save).toBeEnabled();
    await expect(hint).toHaveCount(0);

    // Switching source to Service Bus resets which fields are required — the AKS namespace
    // choice no longer satisfies it.
    await page.getByTestId("alert-rule-source").selectOption("ServiceBusDlqDepth");
    await expect(save).toBeDisabled();
    await expect(hint).toBeVisible();

    const sbNamespace = page.getByTestId("alert-rule-sb-namespace");
    await expect(sbNamespace.locator("option").nth(1)).toBeAttached({ timeout: 10000 });
    await sbNamespace.selectOption({ index: 1 });
    await page.getByTestId("alert-rule-sb-entity").fill("orders/queue");
    await expect(save).toBeEnabled();
  });

  test("edit rule opens editor with existing values", async ({ page }) => {
    await page.goto("/monitoring");
    const row = await createRule(page, `Editable Alert ${Date.now()}`);
    await row.locator("[data-testid^='monitoring-rule-edit-']").click();
    await expect(page.getByTestId("alert-rule-dialog")).toBeVisible();
    await expect(page.getByTestId("alert-rule-name")).not.toHaveValue("");
  });

  test("a rule is discoverable and directly editable from the command palette", async ({ page }) => {
    await page.goto("/monitoring");
    const ruleName = `Palette Alert ${Date.now()}`;
    await createRule(page, ruleName);

    // Navigate away, then find and open the same rule from the global command palette —
    // it must land straight on that rule's editor, not just the Monitoring page in general.
    await page.goto("/");
    await page.getByTestId("command-palette-trigger").click();
    await page.getByTestId("command-palette-input").fill(ruleName);
    const paletteItem = page.locator("[data-testid^='command-palette-item-monitoring-rule-']", { hasText: ruleName });
    await expect(paletteItem).toBeVisible();
    await paletteItem.click();

    await expect(page).toHaveURL(/\/monitoring$/);
    await expect(page.getByTestId("alert-rule-dialog")).toBeVisible();
    await expect(page.getByTestId("alert-rule-name")).toHaveValue(ruleName);
  });

  test("delete rule requires confirmation, then removes from table", async ({ page }) => {
    await page.goto("/monitoring");
    const name = `Delete Me ${Date.now()}`;
    const row = await createRule(page, name);

    await row.locator("[data-testid^='monitoring-rule-delete-']").first().click();

    // Unit 5.1: delete no longer fires on a single click — a ConfirmBar naming the rule
    // must appear first, and the row must still be present until it's confirmed. Matched by
    // its message text rather than by testid, since the confirm-bar container, its "Yes" button,
    // and its "Cancel" button all share the same `monitoring-rule-delete-confirm-<id>` prefix.
    await expect(row.getByText(`Delete alert rule "${name}"`)).toBeVisible();
    await expect(row).toBeVisible();

    await row.locator('[data-testid^="monitoring-rule-delete-confirm-"][data-testid$="-yes"]').click();
    await expect(row).not.toBeVisible();
  });

  test("cancelling a delete confirmation leaves the rule in place", async ({ page }) => {
    await page.goto("/monitoring");
    const name = `Keep Me ${Date.now()}`;
    const row = await createRule(page, name);

    await row.locator("[data-testid^='monitoring-rule-delete-']").first().click();
    await row.locator('[data-testid^="monitoring-rule-delete-confirm-"][data-testid$="-cancel"]').click();

    await expect(row).toBeVisible();
    await expect(row.locator("[data-testid^='monitoring-rule-delete-confirm-']")).toHaveCount(0);
  });

  // ── Unit 5.2 — real loading/error states ─────────────────────────────────────

  test("rules tab surfaces a request failure instead of an empty state", async ({ page }) => {
    // Regression shape: a broken backend connection used to render identically to "no rules
    // configured yet" (the isLoading-only check never distinguished isError).
    const authError = "The alert-rules store rejected the request (HTTP 503): backend unavailable.";
    await page.route("**/api/monitoring/rules", async (route) => {
      await route.fulfill({
        status: 503,
        contentType: "application/json",
        body: JSON.stringify({ error: authError }),
      });
    });

    await page.goto("/monitoring");
    await expect(page.getByTestId("monitoring-rules-error")).toBeVisible();
    await expect(page.getByTestId("monitoring-rules-error")).toContainText(authError);
    await expect(page.getByTestId("monitoring-rules-empty")).toHaveCount(0);
  });

  test("history tab surfaces a request failure instead of an empty state", async ({ page }) => {
    const authError = "The alert-history store rejected the request (HTTP 503): backend unavailable.";
    await page.route("**/api/monitoring/history", async (route) => {
      await route.fulfill({
        status: 503,
        contentType: "application/json",
        body: JSON.stringify({ error: authError }),
      });
    });

    await page.goto("/monitoring");
    await page.getByTestId("monitoring-tab-history").click();
    await expect(page.getByTestId("monitoring-history-error")).toBeVisible();
    await expect(page.getByTestId("monitoring-history-error")).toContainText(authError);
    await expect(page.getByTestId("monitoring-history-empty")).toHaveCount(0);
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

  // ── Unit 5.4 — severity filter/sort in History ───────────────────────────────

  test("History tab can filter by severity and sort by severity", async ({ page }) => {
    const events = [
      { ruleId: "r-warn-1", ruleName: "Warn One", source: "AksPodHealth", severity: "Warning", message: "m1", detail: "", firedAt: "2026-01-03T00:00:00Z", profileName: "default" },
      { ruleId: "r-crit-1", ruleName: "Crit One", source: "AksPodHealth", severity: "Critical", message: "m2", detail: "", firedAt: "2026-01-02T00:00:00Z", profileName: "default" },
      { ruleId: "r-warn-2", ruleName: "Warn Two", source: "AksPodHealth", severity: "Warning", message: "m3", detail: "", firedAt: "2026-01-01T00:00:00Z", profileName: "default" },
    ];
    await page.route("**/api/monitoring/history", async (route) => {
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(events) });
    });

    await page.goto("/monitoring");
    await page.getByTestId("monitoring-tab-history").click();

    const panel = page.getByTestId("monitoring-history-panel");
    await expect(panel).toBeVisible();
    await expect(panel.locator("[data-testid^='monitoring-history-row-']")).toHaveCount(3);

    await page.getByTestId("monitoring-history-severity-filter").selectOption("Critical");
    await expect(panel.locator("[data-testid^='monitoring-history-row-']")).toHaveCount(1);
    await expect(panel).toContainText("Crit One");
    await expect(panel).not.toContainText("Warn One");

    await page.getByTestId("monitoring-history-severity-filter").selectOption("Warning");
    await expect(panel.locator("[data-testid^='monitoring-history-row-']")).toHaveCount(2);

    await page.getByTestId("monitoring-history-severity-filter").selectOption("All");
    await page.getByTestId("monitoring-history-sort").selectOption("severity");
    const rows = panel.locator("[data-testid^='monitoring-history-row-']");
    await expect(rows).toHaveCount(3);
    // Critical sorts first regardless of time; Warning entries keep their relative time order.
    await expect(rows.nth(0)).toContainText("Crit One");
    await expect(rows.nth(1)).toContainText("Warn One");
    await expect(rows.nth(2)).toContainText("Warn Two");
  });

  test("filtering History down to a severity with no matches shows a filtered-empty message, not the generic empty state", async ({ page }) => {
    const events = [
      { ruleId: "r-warn-1", ruleName: "Warn One", source: "AksPodHealth", severity: "Warning", message: "m1", detail: "", firedAt: "2026-01-03T00:00:00Z", profileName: "default" },
    ];
    await page.route("**/api/monitoring/history", async (route) => {
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(events) });
    });

    await page.goto("/monitoring");
    await page.getByTestId("monitoring-tab-history").click();
    await page.getByTestId("monitoring-history-severity-filter").selectOption("Critical");

    await expect(page.getByTestId("monitoring-history-filtered-empty")).toBeVisible();
    await expect(page.getByTestId("monitoring-history-empty")).toHaveCount(0);
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

  // ── Unit 5.4 — cap the insights list with "+N more" / "Dismiss all" ─────────

  test("more than the visible cap of proactive insights collapses behind '+N more', and 'Dismiss all' clears them", async ({ page }) => {
    const frames = [1, 2, 3, 4].map((n) => ({
      kind: "proactiveInsightReady",
      event: {
        ruleId: `rule-cap-${n}`,
        firedAt: `2026-08-0${n}T12:00:00Z`,
        ruleName: `Cap Rule ${n}`,
        summary: `Summary ${n}`,
        sessionId: `proactive-cap-${n}`,
      },
    }));
    await page.route("**/api/monitoring/stream", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "text/event-stream",
        body: frames.map((f) => `data: ${JSON.stringify(f)}\n\n`).join(""),
      });
    });

    await page.goto("/monitoring");

    const cardFor = (n: number) => page.getByTestId(`proactive-insight-rule-cap-${n}-2026-08-0${n}T12:00:00Z`);

    // Newest-first, capped at 3: rules 4, 3, 2 show; rule 1 (oldest) is folded behind the toggle.
    await expect(cardFor(4)).toBeVisible();
    await expect(cardFor(3)).toBeVisible();
    await expect(cardFor(2)).toBeVisible();
    await expect(cardFor(1)).toHaveCount(0);

    const toggle = page.getByTestId("proactive-insights-toggle");
    await expect(toggle).toHaveText("+1 more");
    await toggle.click();
    await expect(cardFor(1)).toBeVisible();
    await expect(toggle).toHaveText("Show less");

    await page.getByTestId("proactive-insights-dismiss-all").click();
    await expect(page.getByTestId("proactive-insights-feed")).toHaveCount(0);
    for (const n of [1, 2, 3, 4]) {
      await expect(cardFor(n)).toHaveCount(0);
    }
  });
});
