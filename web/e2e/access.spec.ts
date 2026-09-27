import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

// Access-awareness Phase 2/3a — the Settings "Access" tab shows the per-connection
// access report; demo mode guarantees a denied row (demo-sql-prd has SELECT/EXECUTE
// but no VIEW DEFINITION, so sql.metadata is red by construction).
test.describe("Settings → Access report", () => {
    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, true);
    });

    test.afterEach(async ({ page }) => {
        await setDemoMode(page, false);
    });

    test("access tab shows the report with per-capability statuses", async ({
        page,
    }) => {
        await page.goto("/settings");
        await page.getByTestId("settings-tab-access").click();
        await expect(page.getByTestId("access-settings")).toBeVisible();

        // Demo connections render as entries with capability rows.
        await expect(
            page.getByTestId("access-entry-Sql-demo-sql-prd"),
        ).toBeVisible({ timeout: 15_000 });

        // The synthesized denied row: red status + remedy text.
        const deniedRow = page.getByTestId(
            "access-entry-Sql-demo-sql-prd-cap-sql-metadata",
        );
        await expect(deniedRow).toBeVisible();
        await expect(deniedRow.getByTestId(/-status$/)).toHaveText("Denied");
        await expect(
            deniedRow.getByTestId(/-remedy$/),
        ).toContainText("VIEW DEFINITION");
    });

    test("denied row offers a copyable request artifact", async ({ page }) => {
        await page.goto("/settings");
        await page.getByTestId("settings-tab-access").click();
        await expect(
            page.getByTestId("access-entry-Sql-demo-sql-prd"),
        ).toBeVisible({ timeout: 15_000 });

        const deniedRow = page.getByTestId(
            "access-entry-Sql-demo-sql-prd-cap-sql-metadata",
        );
        await deniedRow.getByTestId(/-request$/).click();

        // The dialog shows the exact block that gets copied — never a silent copy.
        const dialog = page.getByTestId("access-request-dialog");
        await expect(dialog).toBeVisible();
        await expect(
            page.getByTestId("access-request-summary"),
        ).toContainText("VIEW DEFINITION");
        await expect(
            page.getByTestId("access-request-grant"),
        ).toContainText("GRANT VIEW DEFINITION");
        // No ARM scope is known for the demo connection — the dialog says so
        // rather than fabricating one, and no az command is shown.
        await expect(
            page.getByTestId("access-request-scope"),
        ).toContainText("ask your admin");
        await expect(
            page.getByTestId("access-request-copy"),
        ).toBeVisible();
        // Phase 4's webhook isn't wired — no Send button yet.
        await expect(page.getByTestId("access-request-send")).toHaveCount(0);

        await page.getByTestId("access-request-close").click();
        await expect(dialog).not.toBeVisible();
    });

    test("connection-string rows carry the auth badge and no request button", async ({
        page,
    }) => {
        await page.goto("/settings");
        await page.getByTestId("settings-tab-access").click();
        // Demo Service Bus namespaces default to connection-string auth.
        const entry = page.getByTestId(
            "access-entry-ServiceBus-00000000-0000-0000-0000-000000000001",
        );
        await expect(entry).toBeVisible({ timeout: 15_000 });
        const row = entry.getByTestId(/-cap-servicebus-peek$/);
        await expect(row.getByTestId(/-auth-mode$/)).toHaveText(
            "connection string",
        );
        await expect(row.getByTestId(/-request$/)).toHaveCount(0);
    });

    test("refresh re-probes and the report stays honest about un-probeable rows", async ({
        page,
    }) => {
        await page.goto("/settings");
        await page.getByTestId("settings-tab-access").click();
        await page.getByTestId("access-refresh").click();
        await expect(page.getByTestId("access-generated-at")).toBeVisible();

        // servicebus.send is never probed (it would write a message) — it stays
        // Unknown with the reason shown, not a fake denial.
        const sendRow = page.getByTestId(
            "access-entry-ServiceBus-00000000-0000-0000-0000-000000000001-cap-servicebus-send",
        );
        await expect(sendRow.getByTestId(/-status$/)).toHaveText("Unknown");
        await expect(
            sendRow.getByTestId(/-unknown-detail$/),
        ).toContainText("Not probed");
    });
});
