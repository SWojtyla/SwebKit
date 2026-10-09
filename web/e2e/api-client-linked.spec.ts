import { test, expect, type Page } from "@playwright/test";
import {
    existsSync,
    mkdtempSync,
    readdirSync,
    readFileSync,
    rmSync,
    writeFileSync,
} from "fs";
import { tmpdir } from "os";
import { join } from "path";
import { setDemoMode, resetCollections } from "./helpers";

const sidecarBaseUrl = `http://127.0.0.1:${process.env.PLAYWRIGHT_SIDECAR_PORT ?? "5198"}`;

/**
 * Every `*.swebreq.json` under `dir`, recursively — the linked-root layout is
 * `<root>/.swebkit-api/collections/<collection-dir>/[folder-dirs/]*.swebreq.json`.
 */
function findRequestFiles(dir: string): string[] {
    const found: string[] = [];
    const walk = (d: string) => {
        for (const entry of readdirSync(d, { withFileTypes: true })) {
            const full = join(d, entry.name);
            if (entry.isDirectory()) walk(full);
            else if (entry.name.endsWith(".swebreq.json")) found.push(full);
        }
    };
    if (existsSync(dir)) walk(dir);
    return found;
}

test.describe("API Client linked roots", () => {
    const tempDirs: string[] = [];

    const makeTempRoot = () => {
        const dir = mkdtempSync(join(tmpdir(), "swebkit-e2e-"));
        tempDirs.push(dir);
        return dir;
    };

    /** Registers `dir` as a linked root through the sidecar and returns its summary. */
    const createLinkedRoot = async (page: Page, dir: string) => {
        const res = await page.request.post(
            `${sidecarBaseUrl}/api/linked-roots`,
            { data: { path: dir, name: "E2E Linked" } },
        );
        expect(
            res.ok(),
            `link root failed: ${res.status()} ${await res.text()}`,
        ).toBeTruthy();
        return (await res.json()) as { id: string; name: string; path: string };
    };

    /** Unregisters every root so specs don't leak roots (or temp paths) between runs. */
    const deleteAllLinkedRoots = async (page: Page) => {
        const res = await page.request.get(
            `${sidecarBaseUrl}/api/linked-roots`,
        );
        if (!res.ok()) return; // e.g. demo mode leaves linked roots disabled
        const body = (await res.json()) as { roots?: { id: string }[] };
        for (const r of body.roots ?? []) {
            await page.request.delete(
                `${sidecarBaseUrl}/api/linked-roots/${r.id}`,
            );
        }
    };

    /** Creates a collection + request through the UI inside the linked-root section. */
    const createLinkedRequest = async (
        page: Page,
        rootId: string,
        requestName: string,
        collectionName = "Linked Collection",
    ) => {
        await page.getByTestId(`linked-root-add-collection-${rootId}`).click();
        await expect(page.getByTestId("name-dialog")).toBeVisible();
        await page.getByTestId("name-dialog-input").fill(collectionName);
        await page.getByTestId("name-dialog-confirm").click();
        const collection = page
            .getByTestId(/collection-root-/)
            .filter({ hasText: collectionName })
            .first();
        await collection.waitFor();
        await collection.click();

        await page.getByTestId("add-request-button").click();
        await page.getByTestId("name-dialog-input").fill(requestName);
        await page.getByTestId("name-dialog-confirm").click();
        const node = page
            .getByTestId(/collection-node-Request-/)
            .filter({ hasText: requestName })
            .first();
        await node.waitFor();
        return node;
    };

    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, false);
        await resetCollections(page);
        await deleteAllLinkedRoots(page);
        await page.goto("/api-client");
    });

    test.afterEach(async ({ page }) => {
        await deleteAllLinkedRoots(page);
        await setDemoMode(page, false);
    });

    test.afterAll(() => {
        for (const dir of tempDirs)
            rmSync(dir, { recursive: true, force: true });
    });

    test("creates a request in a linked collection and saves it as a file on disk", async ({
        page,
    }) => {
        const dir = makeTempRoot();
        const root = await createLinkedRoot(page, dir);
        // The root was registered behind the UI's back — reload so the store
        // query picks up the new linkedRoots array.
        await page.reload();
        await expect(page.getByTestId(`linked-root-${root.id}`)).toBeVisible();
        await expect(page.getByTestId("collection-store-footer")).toContainText(
            "Internal store",
        );

        const node = await createLinkedRequest(
            page,
            root.id,
            "Disk Saved Request",
        );
        await node.click();

        await page
            .getByTestId("request-url-input")
            .fill(`${sidecarBaseUrl}/health`);
        await page.getByTestId("request-save-button").click();

        // The create POST already wrote a file — poll on its *content* so we
        // don't read it before the save PUT lands.
        await expect
            .poll(
                () =>
                    findRequestFiles(dir).some((f) => {
                        try {
                            return (
                                (
                                    JSON.parse(readFileSync(f, "utf-8")) as {
                                        url?: string;
                                    }
                                ).url === `${sidecarBaseUrl}/health`
                            );
                        } catch {
                            return false;
                        }
                    }),
                { timeout: 10_000 },
            )
            .toBe(true);
        const [file] = findRequestFiles(dir);
        expect(file.replace(/\\/g, "/")).toContain(".swebkit-api/");
    });

    test("renames a linked request and deletes it — files follow on disk", async ({
        page,
    }) => {
        const dir = makeTempRoot();
        const root = await createLinkedRoot(page, dir);
        await page.reload();

        // The create POST already wrote `Rename Me.swebreq.json` — no save
        // needed (an in-flight save PUT would race the rename anyway).
        const node = await createLinkedRequest(page, root.id, "Rename Me");
        await node.click();
        await expect
            .poll(() => findRequestFiles(dir).length, { timeout: 10_000 })
            .toBe(1);
        const [original] = findRequestFiles(dir);

        // Double-click rename. The row testid changes after rename (the node's
        // id derives from its file name), so resolve it once before editing.
        const rowTestId = await node.getAttribute("data-testid");
        await node.dblclick();
        const renameInput = page.getByTestId(rowTestId!).locator("input");
        await renameInput.waitFor();
        await renameInput.fill("Renamed Request");
        await page.keyboard.press("Enter");

        const renamed = page
            .getByTestId(/collection-node-Request-/)
            .filter({ hasText: "Renamed Request" })
            .first();
        await expect(renamed).toBeVisible();
        await expect
            .poll(
                () =>
                    findRequestFiles(dir).filter(
                        (f) =>
                            f !== original &&
                            /renamed/i.test(f.split(/[\\/]/).pop() ?? ""),
                    ).length,
                { timeout: 10_000 },
            )
            .toBe(1);
        await expect
            .poll(() => existsSync(original), { timeout: 10_000 })
            .toBe(false);

        // Delete it through the context menu — the file goes with the node.
        await renamed.click({ button: "right" });
        await page.getByTestId("ctx-delete").click();
        await page.getByTestId("confirm-dialog-confirm").click();
        await expect
            .poll(() => findRequestFiles(dir).length, { timeout: 10_000 })
            .toBe(0);
    });

    test("a disk edit produces a save conflict; Reload restores the on-disk request", async ({
        page,
    }) => {
        const dir = makeTempRoot();
        const root = await createLinkedRoot(page, dir);
        await page.reload();

        const node = await createLinkedRequest(
            page,
            root.id,
            "Conflict Request",
        );
        await node.click();
        await page
            .getByTestId("request-url-input")
            .fill(`${sidecarBaseUrl}/health`);
        await page.getByTestId("request-save-button").click();
        // Wait for the save PUT to actually land before touching the file —
        // otherwise the in-flight write can clobber the disk edit below.
        const file = await expect
            .poll(
                () =>
                    findRequestFiles(dir).find((f) => {
                        try {
                            return (
                                (
                                    JSON.parse(readFileSync(f, "utf-8")) as {
                                        url?: string;
                                    }
                                ).url === `${sidecarBaseUrl}/health`
                            );
                        } catch {
                            return false;
                        }
                    }) ?? null,
                { timeout: 10_000 },
            )
            .not.toBeNull()
            .then(() => findRequestFiles(dir)[0]);
        const diskContent = JSON.parse(readFileSync(file, "utf-8")) as Record<
            string,
            unknown
        >;
        diskContent.url = `${sidecarBaseUrl}/edited-on-disk`;
        writeFileSync(file, JSON.stringify(diskContent, null, 2));

        await page
            .getByTestId("request-url-input")
            .fill(`${sidecarBaseUrl}/ui-edit`);
        await page.getByTestId("request-save-button").click();

        await expect(page.getByTestId("conflict-banner")).toBeVisible();
        await expect(page.getByTestId("conflict-banner")).toContainText(
            "changed on disk",
        );

        await page.getByTestId("conflict-reload").click();
        await expect(page.getByTestId("request-url-input")).toHaveValue(
            `${sidecarBaseUrl}/edited-on-disk`,
        );
        // The disk version loads clean — and the editor's ~2s auto-save settles
        // without re-conflicting (the reload hands it a fresh stamp).
        await page.waitForTimeout(2500);
        await expect(page.getByTestId("conflict-banner")).toHaveCount(0);
    });
});
