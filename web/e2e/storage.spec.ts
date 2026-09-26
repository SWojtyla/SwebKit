import { test, expect, type Page } from "@playwright/test";
import { setDemoMode } from "./helpers";

async function panelWidth(page: Page, index: number): Promise<number> {
    const panel = page.getByTestId(`panel-${index}`);
    await panel.waitFor();
    return (await panel.boundingBox())?.width ?? 0;
}

test.describe("Storage", () => {
    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, true);
    });

    test.afterEach(async ({ page }) => {
        await setDemoMode(page, false);
    });

    test("displays containers and selects one to show blobs", async ({
        page,
    }) => {
        await page.goto("/storage");

        await expect(page.getByTestId("storage-container-list")).toBeVisible();
        await expect(
            page.getByTestId("storage-container-configs"),
        ).toBeVisible();
        await expect(
            page.getByTestId("storage-container-exports"),
        ).toBeVisible();
        await expect(
            page.getByTestId("storage-container-fixtures"),
        ).toBeVisible();

        await page.getByTestId("storage-container-configs").click();
        await expect(page.getByTestId("storage-blob-browser")).toBeVisible();
        await expect(
            page.getByTestId("storage-item-app-settings.json"),
        ).toBeVisible();
        await expect(
            page.getByTestId("storage-item-feature-flags.json"),
        ).toBeVisible();
    });

    test("lists file shares and browses directories", async ({ page }) => {
        await page.goto("/storage");

        await expect(
            page.getByTestId("storage-share-team-shared"),
        ).toBeVisible();
        await expect(
            page.getByTestId("storage-share-build-artifacts"),
        ).toBeVisible();

        await page.getByTestId("storage-share-team-shared").click();
        await expect(page.getByTestId("storage-share-browser")).toBeVisible();
        await expect(page.getByTestId("share-item-docs")).toBeVisible();
        await expect(page.getByTestId("share-item-media")).toBeVisible();
        await expect(page.getByTestId("share-item-readme.md")).toBeVisible();

        // Into a directory and back via the share breadcrumb.
        await page.getByTestId("share-item-docs").click();
        await expect(
            page.getByTestId("share-item-docs/onboarding.md"),
        ).toBeVisible();
        await page.getByTestId("share-breadcrumb-0").click();
        await expect(page.getByTestId("share-item-readme.md")).toBeVisible();
        await expect(
            page.getByTestId("share-item-docs/onboarding.md"),
        ).not.toBeVisible();
    });

    test("shows share file properties, content preview and SAS action", async ({
        page,
    }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-share-team-shared").click();
        await page.getByTestId("share-item-readme.md").click();

        await expect(page.getByTestId("share-file-name")).toHaveText(
            "readme.md",
        );
        await expect(page.getByTestId("share-file-copy-sas")).toBeVisible();
        await expect(page.getByTestId("share-file-content")).toContainText(
            "Demo content for readme.md",
        );
    });

    test("prettifies a minified XML share file and fills the pane height", async ({
        page,
    }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-share-team-shared").click();
        await page.getByTestId("share-item-docs").click();
        await page.getByTestId("share-item-docs/service-config.xml").click();

        const content = page.getByTestId("share-file-content");

        // Pretty is the default: the stored single-line XML arrives indented.
        await expect(
            page.getByTestId("share-file-pretty-toggle"),
        ).toBeVisible();
        await expect
            .poll(() => content.textContent())
            .toContain('<endpoints>\n');

        // Raw restores the stored minified line verbatim.
        await page.getByTestId("share-file-pretty-toggle").click();
        await expect
            .poll(() => content.textContent())
            .toContain('<service name="swebkit" version="2.4.0"><endpoints>');

        // The preview fills the pane instead of squatting in a 384px box — a large
        // file's scroll area should reach near the bottom of the detail pane.
        await page.getByTestId("share-file-pretty-toggle").click();
        const pre = await content.boundingBox();
        const pane = await page
            .getByTestId("share-file-detail")
            .boundingBox();
        expect(pre!.y + pre!.height).toBeGreaterThan(pane!.y + pane!.height - 60);
    });

    test("shows blob detail with properties and content", async ({ page }) => {
        await page.goto("/storage");

        await page.getByTestId("storage-container-configs").click();
        await page.getByTestId("storage-item-app-settings.json").click();

        await expect(page.getByTestId("storage-blob-name")).toHaveText(
            "app-settings.json",
        );
        await expect(page.getByTestId("storage-blob-type")).toHaveText(
            "application/json",
        );
        await page.getByTestId("storage-blob-tab-content").click();
        await expect(page.getByTestId("storage-blob-content")).toContainText(
            "Logging",
        );
    });

    test("navigates into virtual folder and back via breadcrumb", async ({
        page,
    }) => {
        await page.goto("/storage");

        await page.getByTestId("storage-container-configs").click();
        await expect(page.getByTestId("storage-item-env/")).toBeVisible();

        // Navigate into env/ folder
        await page.getByTestId("storage-item-env/").click();
        await expect(
            page.getByTestId("storage-item-env/prod.json"),
        ).toBeVisible();
        await expect(
            page.getByTestId("storage-item-env/staging.json"),
        ).toBeVisible();

        // Navigate back via breadcrumb
        await page.getByTestId("storage-breadcrumb-0").click();
        await expect(
            page.getByTestId("storage-item-app-settings.json"),
        ).toBeVisible();
        await expect(
            page.getByTestId("storage-item-env/prod.json"),
        ).not.toBeVisible();
    });

    test("shows CSV content in exports container", async ({ page }) => {
        await page.goto("/storage");

        await page.getByTestId("storage-container-exports").click();
        await page.getByTestId("storage-item-2026-03-21-report.csv").click();

        await expect(page.getByTestId("storage-blob-name")).toHaveText(
            "2026-03-21-report.csv",
        );
        await expect(page.getByTestId("storage-blob-type")).toHaveText(
            "text/csv",
        );
        await page.getByTestId("storage-blob-tab-content").click();
        await expect(page.getByTestId("storage-blob-content")).toContainText(
            "OrderId",
        );
    });

    test("shows metadata table when blob has metadata", async ({ page }) => {
        await page.goto("/storage");

        await page.getByTestId("storage-container-configs").click();
        await page.getByTestId("storage-item-app-settings.json").click();

        // Demo blobs have metadata key "demo" = "true"
        const metadataSection = page.locator("text=Metadata");
        await expect(metadataSection).toBeVisible();
    });

    test("metadata edits persist after reload", async ({ page }) => {
        await page.goto("/storage");

        await page.getByTestId("storage-container-configs").click();
        await page.getByTestId("storage-item-app-settings.json").click();

        await expect(
            page.getByTestId("storage-metadata-edit-btn"),
        ).toBeVisible();
        await page.getByTestId("storage-metadata-edit-btn").click();
        await expect(page.getByTestId("storage-metadata-editor")).toBeVisible();

        // Add a new metadata key (default name is "new-key"; the key input is read-only).
        await page.getByTestId("storage-metadata-add-key").click();

        // The editor starts with the existing "demo" key (inputs 0/1), then the new "new-key" row (inputs 2/3).
        const newKeyValueInput = page
            .locator("[data-testid='storage-metadata-editor'] input")
            .nth(3);
        await newKeyValueInput.fill("e2e-persist-value");

        await page.getByTestId("storage-metadata-save").click();

        // The metadata table should now show the new key/value.
        await expect(page.getByText("new-key")).toBeVisible();
        await expect(page.getByText("e2e-persist-value")).toBeVisible();

        // Reload and navigate back to the same blob; the edit must still be present.
        await page.reload();
        await page.getByTestId("storage-container-configs").click();
        await page.getByTestId("storage-item-app-settings.json").click();
        await expect(page.getByText("new-key")).toBeVisible();
        await expect(page.getByText("e2e-persist-value")).toBeVisible();
    });

    test("blob filter narrows the list", async ({ page }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-container-exports").click();
        await expect(page.getByTestId("storage-blob-filter")).toBeVisible();
        await page.getByTestId("storage-blob-filter").fill("report");
        // Wait for filtered items to appear
        await expect(
            page.getByTestId("storage-item-2026-03-21-report.csv"),
        ).toBeVisible();
        // Verify not all items are shown (archive/ prefix should be filtered out)
        await expect(
            page.getByTestId("storage-item-archive/"),
        ).not.toBeVisible();
    });

    test("copy URL and download buttons are visible", async ({ page }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-container-exports").click();
        await page.getByTestId("storage-item-2026-03-21-report.csv").click();
        await expect(page.getByTestId("storage-copy-url-btn")).toBeVisible();
        await expect(page.getByTestId("storage-download-btn")).toBeVisible();
    });

    test("multi-select mode shows checkboxes", async ({ page }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-container-exports").click();
        await page.getByTestId("storage-multi-select-toggle").click();
        await expect(
            page.getByTestId("storage-multi-select-toggle"),
        ).toHaveText("Exit Multi");
        const checkbox = page
            .locator("[data-testid^='storage-blob-checkbox-']")
            .first();
        await expect(checkbox).toBeVisible();
        await page.getByTestId("storage-multi-select-toggle").click();
        await expect(
            page.getByTestId("storage-multi-select-toggle"),
        ).toHaveText("Multi-Select");
    });

    test("metadata editor can be opened", async ({ page }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-container-configs").click();
        await page.getByTestId("storage-item-app-settings.json").click();
        await expect(
            page.getByTestId("storage-metadata-edit-btn"),
        ).toBeVisible();
        await page.getByTestId("storage-metadata-edit-btn").click();
        await expect(page.getByTestId("storage-metadata-editor")).toBeVisible();
        await expect(
            page.getByTestId("storage-metadata-add-key"),
        ).toBeVisible();
    });

    test("uploads a file through the dropzone", async ({ page }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-container-configs").click();
        await page.getByTestId("storage-upload-toggle").click();

        await page.getByTestId("storage-upload-file").setInputFiles({
            name: "e2e-upload.json",
            mimeType: "application/json",
            buffer: Buffer.from('{"source":"e2e"}'),
        });
        await expect(page.getByTestId("storage-upload-name")).toHaveValue(
            "e2e-upload.json",
        );
        await page.getByTestId("storage-upload-confirm").click();

        await expect(
            page.getByTestId("storage-item-e2e-upload.json"),
        ).toBeVisible();
    });

    test("uploading over an existing blob name requires confirmation (unit 6.3)", async ({
        page,
    }) => {
        // Regression: Upload used to silently overwrite a same-named blob with no warning at
        // all, unlike Copy's "Allow overwrite" + confirm guard. Uploads its own throwaway blob
        // first (rather than reusing seeded demo data another test depends on) so the second
        // upload of the same name is a guaranteed collision.
        await page.goto("/storage");
        await page.getByTestId("storage-container-configs").click();
        await page.getByTestId("storage-upload-toggle").click();

        await page.getByTestId("storage-upload-file").setInputFiles({
            name: "e2e-overwrite-target.json",
            mimeType: "application/json",
            buffer: Buffer.from('{"version":1}'),
        });
        await page.getByTestId("storage-upload-confirm").click();
        await expect(
            page.getByTestId("storage-item-e2e-overwrite-target.json"),
        ).toBeVisible();

        await page.getByTestId("storage-upload-toggle").click();
        await page.getByTestId("storage-upload-file").setInputFiles({
            name: "e2e-overwrite-target.json",
            mimeType: "application/json",
            buffer: Buffer.from('{"version":2}'),
        });
        await page.getByTestId("storage-upload-confirm").click();

        // The collision check happens first — the upload must not start until confirmed.
        await expect(
            page.getByTestId("storage-upload-overwrite-confirm"),
        ).toBeVisible();
        await expect(page.getByTestId("storage-upload-panel")).toBeVisible();

        await page.getByTestId("storage-upload-overwrite-confirm-yes").click();
        await expect(
            page.getByTestId("storage-upload-overwrite-confirm"),
        ).not.toBeVisible();
        await expect(
            page.getByTestId("storage-upload-panel"),
        ).not.toBeVisible();
    });

    test("compares and restores blob versions", async ({ page }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-container-configs").click();
        await page.getByTestId("storage-item-app-settings.json").click();
        await page.getByTestId("storage-blob-tab-versions").click();

        await page
            .getByTestId("storage-version-base")
            .selectOption({ index: 1 });
        await page.getByTestId("storage-version-compare-btn").click();
        await expect(
            page.getByTestId("storage-version-diff-pane"),
        ).toBeVisible();
        await expect(
            page.getByTestId("storage-version-text-diff"),
        ).toContainText("version: 2");

        await page
            .getByTestId("storage-version-restore-2026-03-15T08:30:00Z")
            .click();
        await expect(
            page.getByTestId("storage-version-restore-confirm"),
        ).toBeVisible();
        await page.getByTestId("storage-version-restore-confirm-yes").click();
        await expect(
            page.getByTestId("storage-version-restore-confirm"),
        ).not.toBeVisible();
    });

    test("downloads a blob", async ({ page }) => {
        // Regression: the download handler used a relative `/api/...` fetch, which resolves
        // against the frontend host rather than the sidecar's own port — it came back as
        // index.html and failed with "Unexpected token '<'" instead of downloading.
        await page.goto("/storage");
        await page.getByTestId("storage-container-configs").click();
        await page.getByTestId("storage-item-app-settings.json").click();

        const [download] = await Promise.all([
            page.waitForEvent("download"),
            page.getByTestId("storage-download-btn").click(),
        ]);

        expect(download.suggestedFilename()).toBe("app-settings.json");
    });

    test("downloads selected blobs as a single ZIP", async ({ page }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-container-exports").click();
        await page.getByTestId("storage-multi-select-toggle").click();
        await page
            .getByTestId("storage-blob-checkbox-2026-03-21-report.csv")
            .check();

        const [download] = await Promise.all([
            page.waitForEvent("download"),
            page.getByTestId("storage-batch-download").click(),
        ]);

        expect(download.suggestedFilename()).toMatch(/^exports-blobs-.*\.zip$/);
    });

    test("prettifies JSON content and toggles back to raw", async ({
        page,
    }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-container-configs").click();
        await page.getByTestId("storage-item-app-settings.json").click();
        await page.getByTestId("storage-blob-tab-content").click();

        // Asserted on raw textContent, not toContainText: the latter collapses whitespace,
        // which is the only thing that distinguishes prettified output from the raw payload.
        const content = page.getByTestId("storage-blob-content");

        // Pretty is the default, so the demo payload's inline nested objects are expanded.
        await expect(
            page.getByTestId("storage-content-pretty-toggle"),
        ).toBeVisible();
        await expect
            .poll(() => content.textContent())
            .toContain('"Logging": {\n');

        await page.getByTestId("storage-content-raw-toggle").click();
        await expect
            .poll(() => content.textContent())
            .toContain('"Logging": { "LogLevel"');

        await page.getByTestId("storage-content-pretty-toggle").click();
        await expect
            .poll(() => content.textContent())
            .toContain('"Logging": {\n');
    });

    test("prettifies a JSON-string-encoded payload, unescaping it", async ({
        page,
    }) => {
        // Some producers store a JSON payload inside a JSON string — the blob is one
        // big `"..."` literal with escaped quotes. Pretty should unwrap that.
        await page.goto("/storage");
        await page.getByTestId("storage-container-fixtures").click();
        await page.getByTestId("storage-item-escaped-payload.txt").click();
        await page.getByTestId("storage-blob-tab-content").click();

        const content = page.getByTestId("storage-blob-content");

        // Pretty (default) unwraps the string envelope and shows the object.
        await expect(
            page.getByTestId("storage-content-pretty-toggle"),
        ).toBeVisible();
        await expect
            .poll(() => content.textContent())
            .toContain('"orderId": "ORD-9988"');

        // Raw shows the stored text verbatim, escapes and all.
        await page.getByTestId("storage-content-raw-toggle").click();
        await expect
            .poll(() => content.textContent())
            .toContain('\\"orderId\\"');
    });

    test("offers no prettify toggle when the content is not JSON", async ({
        page,
    }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-container-exports").click();
        await page.getByTestId("storage-item-2026-03-21-report.csv").click();
        await page.getByTestId("storage-blob-tab-content").click();

        await expect(page.getByTestId("storage-blob-content")).toContainText(
            "OrderId",
        );
        await expect(
            page.getByTestId("storage-content-pretty-toggle"),
        ).toHaveCount(0);
    });

    test("exposes the full blob name on hover, however long it is", async ({
        page,
    }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-container-configs").click();

        const name = page
            .getByTestId("storage-item-app-settings.json")
            .locator("span[title]");
        await expect(name).toHaveAttribute("title", "app-settings.json");
    });

    test("dragging a resizer moves width between the blob list and the detail pane", async ({
        page,
    }) => {
        await page.setViewportSize({ width: 1920, height: 1080 });
        await page.goto("/storage");
        await page.getByTestId("storage-container-configs").click();

        const before = {
            list: await panelWidth(page, 1),
            detail: await panelWidth(page, 2),
        };

        const resizer = page.getByTestId("resizer-1");
        const box = (await resizer.boundingBox())!;
        await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        await page.mouse.down();
        await page.mouse.move(
            box.x + box.width / 2 + 150,
            box.y + box.height / 2,
            { steps: 10 },
        );
        await page.mouse.up();

        const after = {
            list: await panelWidth(page, 1),
            detail: await panelWidth(page, 2),
        };
        expect(after.list).toBeGreaterThan(before.list);
        expect(after.detail).toBeLessThan(before.detail);
    });

    test("dragged storage widths survive a reload", async ({ page }) => {
        await page.setViewportSize({ width: 1920, height: 1080 });
        await page.goto("/storage");

        const resizer = page.getByTestId("resizer-0");
        const box = (await resizer.boundingBox())!;
        await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        await page.mouse.down();
        await page.mouse.move(
            box.x + box.width / 2 + 120,
            box.y + box.height / 2,
            { steps: 10 },
        );
        await page.mouse.up();

        const widened = await panelWidth(page, 0);
        expect(widened).toBeGreaterThan(300);

        await page.reload();
        await page.getByTestId("resizer-0").waitFor();
        expect(await panelWidth(page, 0)).toBeGreaterThan(280);
    });

    test("uses a container picker and guards overwrite copies", async ({
        page,
    }) => {
        await page.goto("/storage");
        await page.getByTestId("storage-container-configs").click();
        await page.getByTestId("storage-item-app-settings.json").click();
        await page.getByTestId("storage-copy-blob-btn").click();

        await expect(
            page.getByTestId("storage-copy-dest-container"),
        ).toHaveValue("configs");
        await page
            .getByTestId("storage-copy-dest-container")
            .selectOption("exports");
        await page.getByTestId("storage-copy-dest-blob").fill("e2e-copy.json");
        await page.getByTestId("storage-copy-overwrite").check();
        await page.getByTestId("storage-copy-confirm").click();

        await expect(
            page.getByTestId("storage-copy-overwrite-confirm"),
        ).toBeVisible();
        await expect(
            page.getByTestId("storage-copy-overwrite-confirm-yes"),
        ).toBeDisabled();
        await page
            .getByTestId("storage-copy-overwrite-confirm-name")
            .fill("exports/e2e-copy.json");
        await page.getByTestId("storage-copy-overwrite-confirm-yes").click();
        await expect(page.getByTestId("storage-copy-status")).toHaveText(
            "Copied successfully",
        );
    });
});
