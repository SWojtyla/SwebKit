import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

const NAMESPACE = "ecommerce";

/**
 * A .NET failure of exactly the shape that motivated log highlighting: an
 * exception header, a JSON payload, an inner-exception arrow, a stack frame with
 * a source location, and a trace separator.
 */
const LOG_LINES = [
  "Portima.PhoneNotification.Api.Repository.Clients.CreateTokenException: Not able to create a token for users",
  '  { "OfficeId": "29998", "StatusCode": 400, "retry": false }',
  " ---> System.AggregateException: One or more errors occurred.",
  "   at Portima.Brio.Security.TokenCreator.CallTokenApiAsync(Uri uri) in /src/Clients/TokenCreator.cs:line 141",
  "--- End of inner exception stack trace ---",
];

async function stubPodLogStream(page: import("@playwright/test").Page) {
  await page.route("**/logs/stream*", async (route) => {
    const body = `${LOG_LINES.map((line) => `data: ${line}`).join("\n\n")}\n\nevent: done\ndata: \n\n`;
    await route.fulfill({ status: 200, contentType: "text/event-stream", body });
  });
}

test.describe("AKS workspace UX", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("auto-refresh is on by default and the interval persists across a reload", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption(NAMESPACE);

    await expect(page.getByTestId("aks-auto-refresh-checkbox")).toBeChecked();
    await expect(page.getByTestId("aks-refresh-interval")).toBeEnabled();
    await expect(page.getByTestId("aks-refresh-interval")).toHaveValue("10");

    await page.getByTestId("aks-refresh-interval").selectOption("30");
    await page.reload();
    await expect(page.getByTestId("aks-refresh-interval")).toHaveValue("30");
    await expect(page.getByTestId("aks-auto-refresh-checkbox")).toBeChecked();

    await page.getByTestId("aks-auto-refresh-checkbox").uncheck();
    await page.reload();
    await expect(page.getByTestId("aks-auto-refresh-checkbox")).not.toBeChecked();
    await expect(page.getByTestId("aks-refresh-interval")).toBeDisabled();
  });

  test("refresh actually refetches and reports when the data was last updated", async ({ page }) => {
    let deploymentCalls = 0;
    await page.route(`**/api/aks/${NAMESPACE}/deployments`, async (route) => {
      deploymentCalls += 1;
      await route.fallback();
    });

    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption(NAMESPACE);
    await expect(page.getByTestId("deployments-table-body")).toBeVisible();

    // Auto-refresh would also fire eventually; pin the assertion to the explicit
    // click by turning it off first.
    await page.getByTestId("aks-auto-refresh-checkbox").uncheck();
    const before = deploymentCalls;

    await page.getByTestId("aks-refresh-btn").click();

    await expect.poll(() => deploymentCalls).toBeGreaterThan(before);
    await expect(page.getByTestId("aks-last-refreshed")).toContainText(/updated \d+s ago/);
  });

  test("scaling a deployment opens a dialog instead of moving the table", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption(NAMESPACE);
    await expect(page.getByTestId("deployments-table-body")).toBeVisible();

    // Auto-refresh off so a background refetch cannot be blamed for a moved row.
    await page.getByTestId("aks-auto-refresh-checkbox").uncheck();

    const firstRow = page.getByTestId("deployments-table-body").locator("tr").first();
    const boxBefore = await firstRow.boundingBox();

    await firstRow.getByRole("button", { name: "Scale" }).click();
    await expect(page.getByTestId("aks-scale-dialog")).toBeVisible();

    const boxAfter = await firstRow.boundingBox();
    expect(boxAfter?.x).toBe(boxBefore?.x);
    expect(boxAfter?.y).toBe(boxBefore?.y);
    expect(boxAfter?.width).toBe(boxBefore?.width);

    // The dialog names what it is about to act on, namespace included — the inline
    // row editor showed only a bare number box.
    const rowName = (await firstRow.locator("td").first().innerText()).trim();
    await expect(page.getByTestId("aks-scale-dialog")).toContainText(rowName);
    await expect(page.getByTestId("aks-scale-dialog")).toContainText(/\S+ \/ /);

    await page.getByTestId("aks-scale-preset-5").click();
    await expect(page.getByTestId("aks-scale-input")).toHaveValue("5");
    await expect(page.getByTestId("aks-scale-confirm")).toContainText("Scale to 5");

    await page.getByTestId("aks-scale-increment").click();
    await expect(page.getByTestId("aks-scale-input")).toHaveValue("6");

    await page.getByTestId("aks-scale-cancel").click();
    await expect(page.getByTestId("aks-scale-dialog")).toHaveCount(0);
  });

  test("the scale dialog closes on Escape and refuses a no-op", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption(NAMESPACE);
    await expect(page.getByTestId("deployments-table-body")).toBeVisible();

    const firstRow = page.getByTestId("deployments-table-body").locator("tr").first();
    await firstRow.getByRole("button", { name: "Scale" }).click();

    // Opens at the current replica count, so there is nothing to apply yet.
    await expect(page.getByTestId("aks-scale-confirm")).toBeDisabled();
    await expect(page.getByTestId("aks-scale-summary")).toContainText("unchanged");

    await page.keyboard.press("Escape");
    await expect(page.getByTestId("aks-scale-dialog")).toHaveCount(0);
  });

  test("auto-refresh holds while a detail panel is open", async ({ page }) => {
    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption(NAMESPACE);
    await page.getByTestId("aks-tab-pods").click();
    await expect(page.getByTestId("pods-table-body")).toBeVisible();
    await expect(page.getByTestId("aks-auto-refresh-checkbox")).toBeChecked();

    await page.getByTestId("pods-table-body").locator("tr").first().click();
    await expect(page.getByTestId("pod-detail-panel")).toBeVisible();

    // Refetching under an open panel re-lays out the table behind it, so the
    // timer holds — and says so rather than looking broken.
    await expect(page.getByTestId("aks-last-refreshed")).toContainText("auto paused");

    await page.getByTestId("aks-refresh-btn").click();
    await expect(page.getByTestId("pod-detail-panel")).toBeVisible();
  });

  test("pod logs are syntax highlighted", async ({ page }) => {
    await stubPodLogStream(page);

    await page.goto("/aks");
    await page.getByTestId("aks-namespace-select").selectOption(NAMESPACE);
    await page.getByTestId("aks-tab-pods").click();
    await page.getByTestId("pods-table-body").locator("tr").first().click();

    const output = page.getByTestId("log-output");
    await expect(output).toContainText("CreateTokenException");

    await expect(output.locator(".log-tok-exception").first()).toContainText("CreateTokenException");
    await expect(output.locator(".log-tok-key").first()).toContainText('"OfficeId"');
    await expect(output.locator(".log-tok-number").first()).toContainText("400");
    await expect(output.locator(".log-tok-bool").first()).toContainText("false");
    await expect(output.locator(".log-tok-location").first()).toContainText("TokenCreator.cs:line 141");
    await expect(output.locator(".log-tok-keyword").first()).toBeVisible();

    // Stack frames stay dimmed so the exception header they belong to stands out.
    await expect(output.locator(".log-level-frame").first()).toBeVisible();
  });
});
