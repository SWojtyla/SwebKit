import { test, expect, type Page } from "@playwright/test";
import { setDemoMode } from "./helpers";

const sidecarBaseUrl = `http://127.0.0.1:${process.env.PLAYWRIGHT_SIDECAR_PORT ?? "5198"}`;

/**
 * Creates a collection + request and opens it.
 *
 * The e2e sidecar uses a throwaway appdata root shared by every test in the file,
 * so collections accumulate across tests — everything must be selected by name,
 * never with `.first()`.
 */
async function openRequest(page: Page, name: string) {
  const collectionName = `${name} Collection`;

  await page.getByTestId("add-collection-button").click();
  await page.getByTestId("name-dialog-input").fill(collectionName);
  await page.getByTestId("name-dialog-confirm").click();

  const collection = page
    .getByTestId(/collection-root-/)
    .filter({ hasText: collectionName })
    .first();
  await collection.waitFor();
  await collection.click();

  await page.getByTestId("add-request-button").click();
  await page.getByTestId("name-dialog-input").fill(name);
  await page.getByTestId("name-dialog-confirm").click();

  await selectRequest(page, name);
}

async function selectRequest(page: Page, name: string) {
  const node = page
    .getByTestId(/collection-node-Request-/)
    .filter({ hasText: name })
    .first();
  await node.waitFor();
  await node.click();
  await expect(page.getByTestId("request-name-heading")).toContainText(name);
}

async function panelWidth(page: Page, index: number): Promise<number> {
  const panel = page.getByTestId(`panel-${index}`);
  await panel.waitFor();
  return (await panel.boundingBox())?.width ?? 0;
}

test.describe("API Client layout", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
    await page.goto("/api-client");
    // Panel widths persist, so each test starts from the defaults. This must not
    // use addInitScript: that re-runs on every navigation, including the
    // `page.reload()` the persistence test performs, wiping what it verifies.
    await page.evaluate(() => window.localStorage.removeItem("panel-widths:api-client-panels"));
    await page.reload();
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("request and response panes share the width on a wide window", async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto("/api-client");

    const request = await panelWidth(page, 1);
    const response = await panelWidth(page, 2);

    // The original complaint: the response pane was the only flex child and
    // absorbed every spare pixel (roughly 1190 vs 540 at this width).
    expect(request).toBeGreaterThan(600);
    expect(Math.abs(request - response) / Math.max(request, response)).toBeLessThan(0.15);
  });

  test("all panes respect their minimums at a narrow width", async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto("/api-client");

    expect(await panelWidth(page, 0)).toBeGreaterThanOrEqual(199);
    expect(await panelWidth(page, 1)).toBeGreaterThanOrEqual(339);
    expect(await panelWidth(page, 2)).toBeGreaterThanOrEqual(319);
  });

  test("dragging a resizer moves width between the two panes", async ({ page }) => {
    const before = { request: await panelWidth(page, 1), response: await panelWidth(page, 2) };

    const resizer = page.getByTestId("resizer-1");
    const box = (await resizer.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 - 200, box.y + box.height / 2, { steps: 10 });
    await page.mouse.up();

    const after = { request: await panelWidth(page, 1), response: await panelWidth(page, 2) };
    expect(after.request).toBeLessThan(before.request);
    expect(after.response).toBeGreaterThan(before.response);
    // Total is conserved.
    expect(Math.abs((after.request + after.response) - (before.request + before.response))).toBeLessThan(3);
  });

  test("dragged widths survive a reload", async ({ page }) => {
    // Wide viewport so the drag is not capped by the request pane's minimum.
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto("/api-client");

    const resizer = page.getByTestId("resizer-0");
    const box = (await resizer.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 + 120, box.y + box.height / 2, { steps: 10 });
    await page.mouse.up();

    const widened = await panelWidth(page, 0);
    expect(widened).toBeGreaterThan(380);

    await page.reload();
    await page.getByTestId("resizer-0").waitFor();
    expect(await panelWidth(page, 0)).toBeGreaterThan(360);
  });

  test("dragging does not select response text", async ({ page }) => {
    const resizer = page.getByTestId("resizer-1");
    const box = (await resizer.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 - 250, box.y + box.height / 2, { steps: 12 });
    await page.mouse.up();

    expect(await page.evaluate(() => window.getSelection()?.toString() ?? "")).toBe("");
  });

  test("resizers are keyboard operable and exposed as separators", async ({ page }) => {
    // Wide viewport so four steps fit before the adjacent pane's minimum caps them.
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto("/api-client");

    const resizer = page.getByTestId("resizer-0");
    await expect(resizer).toHaveAttribute("role", "separator");
    await expect(resizer).toHaveAttribute("aria-orientation", "vertical");

    const before = await panelWidth(page, 0);
    await resizer.focus();
    for (let i = 0; i < 4; i++) await page.keyboard.press("ArrowRight");

    // 4 × 16 px steps.
    expect(await panelWidth(page, 0)).toBeCloseTo(before + 64, -1);
  });

  test("double-click resets a pair to its default proportion", async ({ page }) => {
    const resizer = page.getByTestId("resizer-1");
    const box = (await resizer.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 - 220, box.y + box.height / 2, { steps: 10 });
    await page.mouse.up();
    expect(await panelWidth(page, 1)).toBeLessThan(await panelWidth(page, 2));

    await resizer.dblclick();
    const request = await panelWidth(page, 1);
    const response = await panelWidth(page, 2);
    expect(Math.abs(request - response) / Math.max(request, response)).toBeLessThan(0.15);
  });

  test("the tree resizer clamps rather than collapsing the pane", async ({ page }) => {
    const resizer = page.getByTestId("resizer-0");
    const box = (await resizer.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(0, box.y + box.height / 2, { steps: 12 });
    await page.mouse.up();

    expect(await panelWidth(page, 0)).toBeGreaterThanOrEqual(199);
  });
});

test.describe("API Client method badges", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
    await page.goto("/api-client");
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  /**
   * The old `method.toUpperCase().slice(0, 4)` rendered DELETE as DELE, PATCH as
   * PATC, OPTIONS as OPTI — abbreviations that read as rendering bugs.
   */
  test("never renders a truncated method label", async ({ page }) => {
    await openRequest(page, "Badges");

    const truncated = ["DELE", "PATC", "OPTI", "GRAP", "WEBS"];
    const allowed = ["GET", "POST", "PUT", "PATCH", "DEL", "HEAD", "OPT", "GQL", "WS"];

    for (const method of ["Delete", "Patch", "Options", "GraphQl", "WebSocket", "Get"]) {
      await page.getByTestId("request-method-select").selectOption(method);

      const labels = (await page.getByTestId("method-badge").allTextContents()).map((l) => l.trim());
      expect(labels.length, `no badge rendered for ${method}`).toBeGreaterThan(0);
      for (const label of labels) {
        expect(truncated, `${method} rendered as ${label}`).not.toContain(label);
        expect(allowed, `${method} rendered as ${label}`).toContain(label);
      }
    }
  });

  test("method colour follows the theme", async ({ page }) => {
    await openRequest(page, "ThemeBadge");

    const badge = page.getByTestId("method-badge").first();
    await badge.waitFor();
    const colourNow = () => badge.evaluate((el) => getComputedStyle(el).color);

    // Proves the badge reads an Aurora token rather than a fixed Tailwind class.
    const before = await colourNow();
    await page.getByTestId("theme-toggle").click();
    await expect.poll(colourNow, { timeout: 3000 }).not.toBe(before);
  });
});

test.describe("API Client response rendering", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
    await page.goto("/api-client");
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("highlights a JSON response in more than one colour", async ({ page }) => {
    await openRequest(page, "Highlight");
    await page.getByTestId("request-url-input").fill(`${sidecarBaseUrl}/health`);
    await page.getByTestId("request-send-button").click();
    await expect(page.getByTestId("response-status")).toContainText("200", { timeout: 10_000 });

    // The direct test for "no syntax highlighting for the response".
    const colours = await page
      .locator('[data-testid="response-body"] span')
      .evaluateAll((els) =>
        Array.from(new Set(els.map((el) => getComputedStyle(el).color).filter(Boolean))),
      );
    expect(colours.length).toBeGreaterThanOrEqual(3);
  });

  test("highlights a JSON request body in more than one colour", async ({ page }) => {
    await openRequest(page, "BodyHighlight");
    await page.getByTestId("request-tab-body").click();
    await page.getByTestId("request-body-mode-select").selectOption("Json");
    await page
      .getByTestId("request-body-editor")
      .fill('{"name":"test","count":42,"flag":true,"nothing":null}');

    // Regression test for the light-only defaultHighlightStyle, which rendered
    // near-black tokens against the dark theme's near-black background.
    const colours = await page
      .locator('[data-testid="request-body-codemirror"] .cm-line span')
      .evaluateAll((els) =>
        Array.from(new Set(els.map((el) => getComputedStyle(el).color).filter(Boolean))),
      );
    expect(colours.length).toBeGreaterThanOrEqual(3);
  });

  test("response toolbar exposes wrap and download", async ({ page }) => {
    await openRequest(page, "Toolbar");
    await page.getByTestId("request-url-input").fill(`${sidecarBaseUrl}/health`);
    await page.getByTestId("request-send-button").click();
    await expect(page.getByTestId("response-status")).toContainText("200", { timeout: 10_000 });

    await expect(page.getByTestId("response-wrap-toggle")).toBeVisible();
    await expect(page.getByTestId("response-download-body")).toBeVisible();
    await expect(page.getByTestId("response-pretty-toggle")).toBeVisible();
    await expect(page.getByTestId("response-raw-toggle")).toBeVisible();
  });

  test("downloads the response body", async ({ page }) => {
    await openRequest(page, "Download");
    await page.getByTestId("request-url-input").fill(`${sidecarBaseUrl}/health`);
    await page.getByTestId("request-send-button").click();
    await expect(page.getByTestId("response-status")).toContainText("200", { timeout: 10_000 });

    const download = page.waitForEvent("download");
    await page.getByTestId("response-download-body").click();
    expect((await download).suggestedFilename()).toMatch(/\.json$/);
  });

  test("response size is human-readable, not raw bytes", async ({ page }) => {
    await openRequest(page, "Size");
    await page.getByTestId("request-url-input").fill(`${sidecarBaseUrl}/health`);
    await page.getByTestId("request-send-button").click();
    await expect(page.getByTestId("response-status")).toContainText("200", { timeout: 10_000 });

    const size = (await page.getByTestId("response-size").textContent())?.trim() ?? "";
    expect(size).not.toContain("bytes");
    expect(size).not.toBe("size unknown");
    expect(size).toMatch(/^(—|\d+ B|[\d.]+ (kB|MB|GB))$/);
  });

  test("per-tab history survives switching tabs", async ({ page }) => {
    await openRequest(page, "HistoryA");
    await page.getByTestId("request-url-input").fill(`${sidecarBaseUrl}/health`);

    // The Send button is disabled while a request is in flight, so waiting on the
    // status text alone would let the next click be swallowed — it already reads
    // 200 from the previous response. Wait for the history count to advance.
    const historyTab = page.getByTestId("response-tab-history");
    for (let i = 1; i <= 3; i++) {
      await page.getByTestId("request-send-button").click();
      await expect(historyTab).toContainText(String(i), { timeout: 10_000 });
    }

    // Leaving and returning previously unmounted ResponseViewer and lost history.
    await page.getByTestId("response-tab-headers").click();
    await historyTab.click();
    await expect(historyTab).toContainText("3");
    await expect(page.getByTestId(/response-history-item-/)).toHaveCount(3);
  });

  test("saved examples persist and can be reopened", async ({ page }) => {
    await openRequest(page, "Example");
    await page.getByTestId("request-url-input").fill(`${sidecarBaseUrl}/health`);
    await page.getByTestId("request-send-button").click();
    await expect(page.getByTestId("response-status")).toContainText("200", { timeout: 10_000 });

    await page.getByTestId("response-save-example").click();
    await page.getByTestId("save-example-form").locator("input").fill("happy path");
    await page.getByTestId("save-example-confirm").click();

    await expect(page.getByTestId("saved-example-happy path")).toBeVisible();

    // Reload proves it reached collections.json rather than component state.
    await page.reload();
    await selectRequest(page, "Example");
    await page.getByTestId("request-send-button").click();
    await expect(page.getByTestId("response-status")).toContainText("200", { timeout: 10_000 });
    await expect(page.getByTestId("saved-example-happy path")).toBeVisible();

    await page.getByTestId("saved-example-happy path").click();
    await expect(page.getByTestId("viewing-example-banner")).toBeVisible();
    await page.getByTestId("viewing-example-return").click();
    await expect(page.getByTestId("viewing-example-banner")).toHaveCount(0);
  });
});

test.describe("API Client request pane", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
    await page.goto("/api-client");
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("request name is a heading that becomes an input on click", async ({ page }) => {
    await openRequest(page, "Renamed");

    const heading = page.getByTestId("request-name-heading");
    await expect(heading).toContainText("Renamed");
    // The name used to be a permanently visible full-width form field.
    await expect(page.getByTestId("request-name-input")).toHaveCount(0);

    await heading.click();
    await page.getByTestId("request-name-input").fill("Renamed Twice");
    await page.keyboard.press("Enter");
    await expect(page.getByTestId("request-name-heading")).toContainText("Renamed Twice");
  });

  test("dirty state shows as a dot rather than mutating the Save label", async ({ page }) => {
    await openRequest(page, "Dirty");
    await page.getByTestId("request-url-input").fill("https://example.com/x");

    await expect(page.getByTestId("request-dirty-dot")).toBeVisible();
    await expect(page.getByTestId("request-save-button")).not.toContainText("Save*");
  });
});
