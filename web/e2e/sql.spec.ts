import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

// Demo mode serves two SQL connections ("orders-dev-sql" / "orders-prod-sql") with canned
// data: 8 customers, 20 orders, 6 products, 12 invoices — and intentional drift between the
// two variants so the compare tab has something to show.
test.describe("SQL", () => {
    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, true);
    });

    test.afterEach(async ({ page }) => {
        await setDemoMode(page, false);
    });

    test("loads with demo connections, schema tree, and read-only badge", async ({
        page,
    }) => {
        await page.goto("/sql");

        await expect(page.getByTestId("sql-page")).toBeVisible();
        await expect(
            page.getByTestId("sql-connection-select").locator("option"),
        ).toHaveCount(2);
        await expect(page.getByTestId("sql-readonly-badge")).toBeVisible();

        // Schema tree populates from the demo client — objects render once a group expands.
        await expect(page.getByTestId("sql-schema-tree")).toBeVisible();
        await expect(page.getByTestId("sql-schema-dbo")).toBeVisible();
        await expect(page.getByTestId("sql-schema-sales")).toBeVisible();
        await page.getByTestId("sql-schema-dbo").click();
        await expect(
            page.getByTestId("sql-object-dbo.customers"),
        ).toBeVisible();
    });

    test("runs a query and shows the result grid", async ({ page }) => {
        await page.goto("/sql");

        await page
            .getByTestId("sql-editor-input")
            .fill("SELECT * FROM dbo.customers");
        await page.getByTestId("sql-run-query").click();

        await expect(page.getByTestId("sql-query-results")).toBeVisible();
        await expect(page.getByTestId("sql-query-results-count")).toContainText(
            "8",
        );
        await expect(page.getByTestId("sql-query-results")).toContainText(
            "customer1@example.com",
        );
    });

    test("rejects a mutating statement on the read-only demo connection", async ({
        page,
    }) => {
        await page.goto("/sql");

        await page
            .getByTestId("sql-editor-input")
            .fill("DELETE FROM customers");
        await page.getByTestId("sql-run-query").click();

        await expect(page.getByTestId("sql-query-error")).toBeVisible({
            timeout: 15000,
        });
        await expect(page.getByTestId("sql-query-error")).toContainText(
            /read-only|not allowed|writes/i,
        );
    });

    test("clicking a table in the schema tree opens the browse tab with rows", async ({
        page,
    }) => {
        await page.goto("/sql");

        await expect(page.getByTestId("sql-schema-tree")).toBeVisible();
        await page.getByTestId("sql-schema-dbo").click();
        await page.getByTestId("sql-object-dbo.orders").click();

        await expect(page.getByTestId("sql-browse-panel")).toBeVisible();
        await expect(page.getByTestId("sql-browse-title")).toContainText(
            "orders",
        );
        // The demo orders table has 20 rows.
        await expect(page.getByTestId("sql-browse-panel")).toContainText(
            "processing",
        );
    });

    test("saves a query and shows it in the saved list", async ({ page }) => {
        await page.goto("/sql");

        await page
            .getByTestId("sql-editor-input")
            .fill("SELECT * FROM dbo.products");
        await page.getByTestId("sql-save-query-open").click();
        await page.getByTestId("sql-save-popover-name").fill("All products");
        await page.getByTestId("sql-save-popover-submit").click();
        await page.getByTestId("sql-tab-saved").click();

        await expect(page.getByTestId("sql-saved-panel")).toContainText("All products");
    });

    test("records executions in history", async ({ page }) => {
        await page.goto("/sql");

        await page
            .getByTestId("sql-editor-input")
            .fill("SELECT * FROM sales.invoices");
        await page.getByTestId("sql-run-query").click();
        await expect(page.getByTestId("sql-query-results-count")).toContainText(
            "12",
        );

        await page.getByTestId("sql-tab-saved").click();
        await expect(page.getByTestId("sql-saved-panel")).toContainText(
            "sales.invoices",
        );
    });

    test("schema compare between the two demo connections reports drift", async ({
        page,
    }) => {
        await page.goto("/sql");
        await page.getByTestId("sql-tab-compare").click();

        await page.getByTestId("sql-compare-mode").selectOption("schema");
        // Target selection is deliberate: compare must never silently choose another live database.
        await expect(page.getByTestId("sql-compare-run")).toBeDisabled();
        await page
            .getByTestId("sql-compare-target")
            .selectOption("demo-sql-2");
        await page.getByTestId("sql-compare-run").click();

        await expect(page.getByTestId("sql-compare-schema-result")).toBeVisible(
            { timeout: 15000 },
        );
        // demo-sql-2 has sales.returns which demo-sql lacks.
        await expect(
            page.getByTestId("sql-compare-schema-result"),
        ).toContainText("returns");
        await expect(
            page.getByTestId("sql-compare-schema-result"),
        ).toContainText("products");
    });
});
