import { test, expect, type Page } from "@playwright/test";
import { setDemoMode, resetCollections } from "./helpers";

const sidecarBaseUrl = `http://127.0.0.1:${process.env.PLAYWRIGHT_SIDECAR_PORT ?? "5198"}`;

// Chains under test run requests against the sidecar's own /health — no
// external network needed, the same trick api-client-runs.spec.ts uses.

function seedRequest(id: string, name: string) {
    return {
        id,
        type: "Request",
        name,
        isExpanded: true,
        children: [],
        defaultAuth: null,
        request: {
            id,
            name,
            method: "Get",
            url: `${sidecarBaseUrl}/health`,
            headers: [],
            queryParams: [],
            body: {
                mode: "None",
                rawContent: null,
                contentType: null,
                formData: [],
                filePath: null,
            },
            auth: null,
            captureRules: [],
            graphQlQuery: null,
            graphQlVariables: null,
            graphQlSelectedOperation: null,
            savedMessages: [],
            wsSubProtocol: null,
            responseExamples: [],
            createdAt: "2026-01-01T00:00:00Z",
            updatedAt: "2026-01-01T00:00:00Z",
            preRequestActions: [],
            postRequestActions: [],
        },
    };
}

const COLLECTION_ID = "e2e-chain-col";
const REQ_FIRST = "e2e-req-first";
const REQ_SECOND = "e2e-req-second";

async function seedCollection(page: Page) {
    const res = await page.request.put(
        `${sidecarBaseUrl}/api/config/collections`,
        {
            data: {
                schemaVersion: 1,
                collections: [
                    {
                        id: COLLECTION_ID,
                        name: "Chain Collection",
                        nodes: [
                            seedRequest(REQ_FIRST, "First"),
                            seedRequest(REQ_SECOND, "Second"),
                        ],
                        variables: [],
                        defaultAuth: null,
                        createdAt: "2026-01-01T00:00:00Z",
                        updatedAt: "2026-01-01T00:00:00Z",
                    },
                ],
            },
        },
    );
    expect(res.ok()).toBeTruthy();
}

/**
 * True only while the backend's chains endpoints are absent (404 on the list
 * route). Any other failure — a real regression — fails the test honestly.
 */
async function chainsApiMissing(page: Page): Promise<boolean> {
    const res = await page.request
        .get(`${sidecarBaseUrl}/api/api-client/chains`)
        .catch(() => null);
    return res === null || res.status() === 404;
}

test.describe("API Client — request chains", () => {
    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, false);
        await resetCollections(page);
        await seedCollection(page);
    });

    test.afterEach(async ({ page }) => {
        await setDemoMode(page, false);
    });

    test("sidebar chain runs its steps in declared order", async ({ page }) => {
        test.skip(
            await chainsApiMissing(page),
            "chains endpoints not implemented yet",
        );
        const create = await page.request.post(
            `${sidecarBaseUrl}/api/api-client/chains`,
            {
                data: {
                    name: "Health Chain",
                    steps: [
                        {
                            collectionId: COLLECTION_ID,
                            requestId: REQ_FIRST,
                            enabled: true,
                        },
                        {
                            collectionId: COLLECTION_ID,
                            requestId: REQ_SECOND,
                            enabled: true,
                        },
                    ],
                },
            },
        );
        expect(create.ok()).toBeTruthy();
        const chain = (await create.json()) as { id: string };

        await page.goto("/api-client");
        const row = page.getByTestId(`chain-row-${chain.id}`);
        await expect(row).toBeVisible();
        await expect(
            page.getByTestId(`chain-stepcount-${chain.id}`),
        ).toHaveText("2");

        await row.click({ button: "right" });
        await page.getByTestId("chain-ctx-run").click();

        const drawer = page.getByTestId("run-drawer");
        await expect(drawer).toBeVisible();
        // Declared order, not tree/id order — step 0 is "First", step 1 "Second".
        await expect(drawer.getByTestId("run-step-0")).toContainText("First");
        await expect(drawer.getByTestId("run-step-1")).toContainText("Second");
        // Chain plan steps carry their collection badge.
        await expect(
            drawer.getByTestId("run-collection-0"),
        ).toContainText("Chain Collection");
        await expect(
            drawer.getByTestId("run-status-1"),
        ).toHaveAttribute("data-status", "completed", { timeout: 15_000 });
        await expect(drawer.getByTestId("run-progress")).toContainText("2/2");
    });

    test("'Add to chain → New chain' creates a chain with the request as step 1", async ({
        page,
    }) => {
        test.skip(
            await chainsApiMissing(page),
            "chains endpoints not implemented yet",
        );
        await page.goto("/api-client");

        await page
            .getByTestId(`collection-node-Request-${REQ_FIRST}`)
            .click({ button: "right" });
        await page.getByTestId("ctx-add-to-chain").hover();
        const submenu = page.getByTestId("chain-picker-menu");
        await expect(submenu).toBeVisible();
        await submenu.getByTestId("ctx-add-to-chain-new").click();

        await page.getByTestId("name-dialog-input").fill("My Chain");
        await page.getByTestId("name-dialog-confirm").click();

        // The editor opens on the freshly created chain, seeded with step 1.
        const dialog = page.getByTestId("chain-editor-dialog");
        await expect(dialog).toBeVisible();
        await expect(dialog.getByTestId("chain-name-input")).toHaveValue(
            "My Chain",
        );
        await expect(page.getByTestId("chain-steps-list")).toContainText(
            "First",
        );
        await dialog.getByTestId("chain-editor-close").click();

        await expect(page.getByTestId("chain-section")).toContainText(
            "My Chain",
        );
    });

    test("chain editor adds a second request via the picker and saves", async ({
        page,
    }) => {
        test.skip(
            await chainsApiMissing(page),
            "chains endpoints not implemented yet",
        );
        await page.goto("/api-client");

        await page.getByTestId("add-chain-button").click();
        const dialog = page.getByTestId("chain-editor-dialog");
        await expect(dialog).toBeVisible();
        await dialog.getByTestId("chain-name-input").fill("Editor Chain");

        await dialog
            .getByTestId(`chain-picker-option-${REQ_FIRST}`)
            .click();
        await dialog
            .getByTestId(`chain-picker-option-${REQ_SECOND}`)
            .click();
        await expect(page.getByTestId("chain-steps-list")).toContainText(
            "First",
        );
        await expect(page.getByTestId("chain-steps-list")).toContainText(
            "Second",
        );
        // A picked request can't be added twice (dedupe).
        await expect(
            dialog.getByTestId(`chain-picker-option-${REQ_FIRST}`),
        ).toBeDisabled();

        await dialog.getByTestId("chain-save-button").click();
        await expect(dialog).not.toBeVisible();

        // The sidebar shows the new chain with its step count.
        const section = page.getByTestId("chain-section");
        await expect(section).toContainText("Editor Chain");
        await expect(section.getByText("2")).toBeVisible();
    });
});
