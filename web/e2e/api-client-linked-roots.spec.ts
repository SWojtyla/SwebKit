import { test, expect, type Page } from "@playwright/test";
import { mkdtempSync, rmSync, writeFileSync, existsSync, readFileSync } from "fs";
import { tmpdir } from "os";
import { join } from "path";
import { setDemoMode, resetCollections } from "./helpers";
import { sidecarPort } from "./test-config";

/// Linked API project folders: a directory on disk (typically inside a git repo)
/// that holds the collection as `.swebkit-api/` files — one .swebreq.json per
/// request — merged into the API Client next to app-storage collections. Saves
/// partition back to the right store, and external edits surface as conflicts.

const sidecarUrl = `http://127.0.0.1:${sidecarPort}`;

interface LinkedRootSummary {
  id: string;
  path: string;
  displayName: string;
  apiRootPath?: string | null;
}

async function linkedRoots(page: Page): Promise<LinkedRootSummary[]> {
  const res = await page.request.get(`${sidecarUrl}/api/api-client/linked-roots`);
  return (await res.json()) as LinkedRootSummary[];
}

async function removeAllLinkedRoots(page: Page) {
  for (const root of await linkedRoots(page)) {
    await page.request.delete(`${sidecarUrl}/api/api-client/linked-roots/${root.id}`);
  }
}

test.describe("API Client — linked project folders", () => {
  let dir: string;

  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
    // No leftover roots from a crashed run — they would merge foreign
    // collections into the store and resetCollections would wipe their files.
    await removeAllLinkedRoots(page);
    await resetCollections(page);
    dir = mkdtempSync(join(tmpdir(), "swebkit-linked-e2e-"));
    await page.goto("/api-client");
  });

  test.afterEach(async ({ page }) => {
    await removeAllLinkedRoots(page);
    await resetCollections(page);
    rmSync(dir, { recursive: true, force: true });
  });

  test("links a folder through the dialog and lists it with diagnostics", async ({ page }) => {
    await page.getByTestId("linked-projects-button").click();
    await expect(page.getByTestId("linked-projects-dialog")).toBeVisible();
    await expect(page.getByTestId("linked-roots-empty")).toBeVisible();

    // Browser/dev mode has no Tauri folder picker — the manual path input is the fallback.
    await page.getByTestId("linked-root-add-open").click();
    await page.getByTestId("linked-root-path-input").fill(dir);
    await page.getByTestId("linked-root-name-input").fill("E2E APIs");
    await page.getByTestId("linked-root-add-submit").click();

    await expect(page.getByTestId("linked-roots-empty")).toBeHidden();
    await expect(page.getByTestId("linked-projects-dialog")).toContainText("E2E APIs");
    await expect(page.getByTestId("linked-projects-dialog")).toContainText(dir);

    // The .swebkit-api structure was created inside the picked folder.
    expect(existsSync(join(dir, ".swebkit-api", "swebkit.json"))).toBe(true);
    expect(existsSync(join(dir, ".swebkit-api", "collections"))).toBe(true);
  });

  test("creates a collection in the linked folder and persists requests as files", async ({ page }) => {
    await page.request.post(`${sidecarUrl}/api/api-client/linked-roots`, {
      data: { path: dir, name: "E2E APIs" },
    });
    await page.reload();

    // New Collection → pick the linked folder as storage.
    await page.getByTestId("add-collection-button").click();
    await expect(page.getByTestId("new-collection-dialog")).toBeVisible();
    await page.getByTestId("new-collection-name").fill("Linked Orders");
    const storage = page.getByTestId("new-collection-storage");
    await expect(storage.locator("option")).toHaveCount(2);
    await storage.selectOption({ index: 1 });
    await page.getByTestId("new-collection-create").click();

    // The collection shows in the tree carrying the storage badge.
    const collection = page.getByTestId(/collection-root-/).filter({ hasText: "Linked Orders" }).first();
    await collection.waitFor();
    await expect(collection.getByTestId(/storage-badge-/)).toBeVisible();
    await collection.click();

    // Selecting it shows the linked storage chip in the toolbar.
    await expect(page.getByTestId("storage-chip-linked")).toContainText("E2E APIs");

    // Add a request — the save must write a .swebreq.json into the linked folder.
    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("Get Order");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-node-Request-/).first().click();

    const requestFile = join(dir, ".swebkit-api", "collections", "linked-orders", "get-order.swebreq.json");
    await expect.poll(() => existsSync(requestFile), { timeout: 10_000 }).toBe(true);
    expect(readFileSync(requestFile, "utf-8")).toContain("Get Order");

    // And nothing leaked into app storage.
    const store = await (await page.request.get(`${sidecarUrl}/api/config/collections/store`)).json();
    expect(store.collections).toHaveLength(1);
    expect(store.collections[0].linkedRootId).toBeTruthy();
  });

  test("external file edit surfaces a conflict; overwrite resolves it", async ({ page }) => {
    await page.request.post(`${sidecarUrl}/api/api-client/linked-roots`, {
      data: { path: dir, name: "E2E APIs" },
    });
    await page.reload();

    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("new-collection-name").fill("Linked Orders");
    await page.getByTestId("new-collection-storage").selectOption({ index: 1 });
    await page.getByTestId("new-collection-create").click();
    const collection = page.getByTestId(/collection-root-/).filter({ hasText: "Linked Orders" }).first();
    await collection.click();
    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("Get Order");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-node-Request-/).first().click();

    const requestFile = join(dir, ".swebkit-api", "collections", "linked-orders", "get-order.swebreq.json");
    await expect.poll(() => existsSync(requestFile), { timeout: 10_000 }).toBe(true);

    // Someone edited the file outside SwebKit (teammate, IDE, git checkout) —
    // a normal edit keeps the persisted id, so the file stays the same request.
    const original = JSON.parse(readFileSync(requestFile, "utf-8")) as Record<string, unknown>;
    writeFileSync(requestFile, JSON.stringify({ ...original, url: "/orders/external-edit" }));

    // Editing the request in the UI triggers a save that must conflict, not clobber.
    await page.getByTestId("request-url-input").fill("/orders/mine");
    await page.getByTestId("request-save-button").click();

    await expect(page.getByTestId("conflict-banner")).toBeVisible();
    await expect(page.getByTestId("conflict-files")).toContainText("get-order.swebreq.json");
    // The external edit is still on disk — nothing was written.
    expect(readFileSync(requestFile, "utf-8")).toContain("external-edit");

    // Explicit overwrite wins the file.
    await page.getByTestId("conflict-overwrite").click();
    await expect(page.getByTestId("conflict-banner")).toBeHidden();
    await expect.poll(() => readFileSync(requestFile, "utf-8"), { timeout: 10_000 }).toContain("/orders/mine");
  });
});
