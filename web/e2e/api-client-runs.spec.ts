import { test, expect, type Page } from "@playwright/test";
import { setDemoMode, resetCollections } from "./helpers";

const sidecarBaseUrl = `http://127.0.0.1:${process.env.PLAYWRIGHT_SIDECAR_PORT ?? "5198"}`;

// The demo collection's chained pair (DemoApiCollectionFactory): "Get token"
// captures $.uuid into {{demoToken}}, "List orders" depends on it and sends the
// captured value as a bearer token. Both hit real httpbin.org — demo runs need
// network, same as every other demo request.
const DEMO_COLLECTION_ROOT = "collection-root-__demo__samples";
const CHAIN_GET_TOKEN = "collection-node-Request-__demo__chain_get_token";
const CHAIN_LIST_ORDERS = "collection-node-Request-__demo__chain_list_orders";
const HTTPBIN_FOLDER = "collection-node-Folder-__demo__httpbin";

/** The tree is virtualized — offscreen rows don't exist in the DOM at all.
 *  Filtering on a name both shrinks the row list and force-expands the folders
 *  containing a match, which is how deep nodes become clickable. */
async function revealNode(page: Page, filterText: string, testId: string) {
    await page.getByTestId("collection-search").fill(filterText);
    const node = page.getByTestId(testId);
    await expect(node).toBeVisible();
    return node;
}

test.describe("API Client — request runs (demo)", () => {
    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, true);
        await page.goto("/api-client");
        await page.getByTestId(DEMO_COLLECTION_ROOT).waitFor();
    });

    test.afterEach(async ({ page }) => {
        await setDemoMode(page, false);
    });

    test("Send with dependencies runs the chain and shows live steps", async ({
        page,
    }) => {
        await (
            await revealNode(page, "anything/orders", CHAIN_LIST_ORDERS)
        ).click();

        // The persisted dependency shows up as a "Runs after" chip.
        const editor = page.getByTestId("request-editor");
        await expect(editor.getByTestId("runs-after-section")).toBeVisible();
        await expect(
            editor.getByTestId("dep-chip-__demo__chain_get_token"),
        ).toBeVisible();

        // The split button's menu enables "Send with dependencies (1)".
        await editor.getByTestId("send-menu-button").click();
        const menu = page.getByTestId("send-menu");
        await expect(menu).toBeVisible();
        await expect(menu.getByTestId("send-with-deps")).toBeEnabled();
        await menu.getByTestId("send-with-deps").click();

        // Drawer opens and streams both steps to completion.
        const drawer = page.getByTestId("run-drawer");
        await expect(drawer).toBeVisible();
        await expect(drawer.getByTestId("run-step-0")).toBeVisible();
        await expect(drawer.getByTestId("run-step-1")).toBeVisible();
        await expect(drawer.getByTestId("run-status-1")).toHaveAttribute(
            "data-status",
            "completed",
            { timeout: 30_000 },
        );
        await expect(drawer.getByTestId("run-progress")).toContainText("2/2");
        await expect(drawer.getByTestId("run-summary")).toContainText(
            "completed",
        );

        // The final step's response lands in the normal response viewer.
        await expect(page.getByTestId("response-status")).toContainText("200");

        // The captured collection variable is visible on the first step.
        await expect(drawer.getByTestId("run-captured-0")).toContainText(
            "demoToken",
        );
    });

    test("folder context menu 'Run in order' runs the subtree", async ({
        page,
    }) => {
        await page.getByTestId("collection-search").fill("Request chain");
        const folder = page.getByTestId("collection-node-Folder-__demo__chain");
        await expect(folder).toBeVisible();
        await folder.click({ button: "right" });
        await page.getByTestId("ctx-run-in-order").click();

        const drawer = page.getByTestId("run-drawer");
        await expect(drawer).toBeVisible();
        await expect(drawer.getByTestId("run-status-1")).toHaveAttribute(
            "data-status",
            "completed",
            { timeout: 30_000 },
        );
        await expect(drawer.getByTestId("run-progress")).toContainText("2/2");
    });

    test("ctrl+click multi-select then 'Run selection' runs picked requests", async ({
        page,
    }) => {
        const modifier = process.platform === "darwin" ? "Meta" : "Control";
        // The selection survives search edits (it lives in component state), so
        // each node is revealed by its own filter before being toggled in.
        await (
            await revealNode(page, "uuid", CHAIN_GET_TOKEN)
        ).click({ modifiers: [modifier] });
        await (
            await revealNode(page, "anything/orders", CHAIN_LIST_ORDERS)
        ).click({ modifiers: [modifier] });

        // Only the filtered row is mounted (virtualized tree) — the menu label
        // is the authoritative selection count.
        await expect(page.locator("[data-multi-selected='true']")).toHaveCount(
            1,
        );

        await page.getByTestId(CHAIN_LIST_ORDERS).click({ button: "right" });
        const item = page.getByTestId("ctx-run-selection");
        await expect(item).toBeEnabled();
        await expect(item).toContainText("Run selection (2)");
        await item.click();

        const drawer = page.getByTestId("run-drawer");
        await expect(drawer).toBeVisible();
        await expect(drawer.getByTestId("run-status-1")).toHaveAttribute(
            "data-status",
            "completed",
            { timeout: 30_000 },
        );
        await expect(drawer.getByTestId("run-progress")).toContainText("2/2");
    });

    test("abort cancels a running batch", async ({ page }) => {
        // The HTTPBin folder holds several requests (one is a 3s delay), so the
        // run stays in-flight long enough to abort deterministically.
        await page.getByTestId("collection-search").fill("HTTPBin");
        const folder = page.getByTestId(HTTPBIN_FOLDER);
        await expect(folder).toBeVisible();
        await folder.click({ button: "right" });
        await page.getByTestId("ctx-run-in-order").click();

        const drawer = page.getByTestId("run-drawer");
        await expect(drawer).toBeVisible();
        await expect(drawer.getByTestId("run-abort")).toBeVisible();
        await drawer.getByTestId("run-abort").click();

        await expect(drawer.getByTestId("run-summary")).toContainText(
            /Cancelled|Stopped/,
            { timeout: 15_000 },
        );
        await expect(drawer.getByTestId("run-abort")).not.toBeVisible();
    });

    test("run options popover toggles stop-on-error and step delay", async ({
        page,
    }) => {
        await (
            await revealNode(page, "anything/orders", CHAIN_LIST_ORDERS)
        ).click();
        const editor = page.getByTestId("request-editor");

        await editor.getByTestId("send-menu-button").click();
        await page.getByTestId("send-run-options").click();

        const popover = page.getByTestId("run-options-popover");
        await expect(popover).toBeVisible();
        // Stop-on-error defaults to on.
        await expect(
            popover.getByTestId("run-opt-stop-on-error"),
        ).toBeChecked();
        await popover.getByTestId("run-opt-stop-on-error").click();
        await expect(
            popover.getByTestId("run-opt-stop-on-error"),
        ).not.toBeChecked();
        await popover.getByTestId("run-opt-delay-ms").fill("250");
        await expect(popover.getByTestId("run-opt-delay-ms")).toHaveValue(
            "250",
        );

        // Escape closes the popover.
        await page.keyboard.press("Escape");
        await expect(popover).not.toBeVisible();
    });
});

test.describe("API Client — request runs (internal)", () => {
    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, false);
        await resetCollections(page);
        await page.goto("/api-client");
    });

    test.afterEach(async ({ page }) => {
        await setDemoMode(page, false);
    });

    test("prerequisite picker chains two requests and runs them in order", async ({
        page,
    }) => {
        // Requests target the sidecar itself — no external network needed, so
        // this spec stays green offline.
        await page.getByTestId("add-collection-button").click();
        await page.getByTestId("name-dialog-input").fill("Runs Collection");
        await page.getByTestId("name-dialog-confirm").click();
        await page
            .getByTestId(/collection-root-/)
            .first()
            .click();

        await page.getByTestId("add-request-button").click();
        await page.getByTestId("name-dialog-input").fill("First Request");
        await page.getByTestId("name-dialog-confirm").click();
        const first = page.getByTestId(/collection-node-Request-/).first();
        await first.waitFor();
        await first.click();
        await page
            .getByTestId("request-url-input")
            .fill(`${sidecarBaseUrl}/health`);
        await page.getByTestId("request-save-button").click();

        await page.getByTestId("add-request-button").click();
        await page.getByTestId("name-dialog-input").fill("Second Request");
        await page.getByTestId("name-dialog-confirm").click();
        // The new request's tab activates asynchronously — the URL input still
        // shows the previous draft until React commits the switch, so wait for
        // it to go empty (a fresh request starts with a blank URL) before
        // filling, otherwise the fill lands on the previous request's editor.
        const urlInput = page.getByTestId("request-url-input");
        await expect(urlInput).toHaveValue("");
        await urlInput.fill(`${sidecarBaseUrl}/health`);

        // Pick "First Request" as a prerequisite through the Runs after picker.
        const editor = page.getByTestId("request-editor");
        await editor.getByTestId("dep-add-button").click();
        const picker = page.getByTestId("dep-picker");
        await expect(picker).toBeVisible();
        await picker
            .locator("button")
            .filter({ hasText: "First Request" })
            .click();
        const chip = editor.locator('[data-testid^="dep-chip-"]');
        await expect(chip).toContainText("First Request");

        // Save so the dependency lands in the persisted collection.
        await page.getByTestId("request-save-button").click();

        await editor.getByTestId("send-menu-button").click();
        await page.getByTestId("send-with-deps").click();

        const drawer = page.getByTestId("run-drawer");
        await expect(drawer).toBeVisible();
        await expect(drawer.getByTestId("run-status-1")).toHaveAttribute(
            "data-status",
            "completed",
            { timeout: 15_000 },
        );
        await expect(drawer.getByTestId("run-progress")).toContainText("2/2");
        await expect(page.getByTestId("response-status")).toContainText("200");
    });
});
