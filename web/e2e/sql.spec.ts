import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

// Demo mode serves three SQL connections ("orders-dev-sql" / "orders-prod-sql" / the restricted
// "orders-prd-sql") with canned data: 8 customers, 20 orders, 6 products, 12 invoices — and
// intentional drift between the first two variants so the compare tab has something to show.
// The third simulates a locked-down PRD login: SELECT/EXECUTE but no VIEW DEFINITION.
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
        ).toHaveCount(3);
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

    test("switching tables resets table-specific browse state", async ({ page }) => {
        await page.goto("/sql");
        await page.getByTestId("sql-schema-dbo").click();
        await page.getByTestId("sql-object-dbo.orders").click();

        await page.getByTestId("sql-browse-filter-column").selectOption("status");
        await page.getByTestId("sql-browse-filter-text").fill("processing");
        await page.getByTestId("sql-object-dbo.customers").click();

        await expect(page.getByTestId("sql-browse-title")).toContainText("customers");
        await expect(page.getByTestId("sql-browse-filter-column")).toHaveValue("");
        await expect(page.getByTestId("sql-browse-filter-text")).toHaveCount(0);
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

    test("restricted connection browses declared objects with lazy columns", async ({
        page,
    }) => {
        await page.goto("/sql");
        await page
            .getByTestId("sql-connection-select")
            .selectOption("demo-sql-prd");

        // demo-sql-prd declares prd.v_orders / exec:prd.p_recalc / prd.v_audit — the
        // catalog is hidden, so the tree is those declarations alone plus a banner
        // saying so (never the misleading "no objects" empty state).
        await expect(page.getByTestId("sql-schema-partial")).toBeVisible();
        await expect(page.getByTestId("sql-schema-hidden")).toHaveCount(0);
        await expect(page.getByTestId("sql-schema-empty")).toHaveCount(0);

        await page.getByTestId("sql-schema-prd").click();
        const orders = page.getByTestId("sql-object-prd.v_orders");
        await expect(orders).toBeVisible();
        await expect(
            page.getByTestId("sql-declared-badge-prd.v_orders"),
        ).toBeVisible();

        // Expanding a declared object lazy-loads columns via SELECT TOP 0 — granted on
        // v_orders, so the columns arrive without any catalog rights.
        await page.getByTestId("sql-object-expand-prd.v_orders").click();
        await expect(page.getByTestId("sql-columns-prd.v_orders")).toContainText(
            "total",
        );

        // v_audit carries an object-level DENY in the demo — the failure surfaces on the
        // object itself, not as a global error.
        await page.getByTestId("sql-object-expand-prd.v_audit").click();
        await expect(
            page.getByTestId("sql-columns-denied-prd.v_audit"),
        ).toBeVisible();

        // A declared procedure lists as runnable and offers no column fetch — selecting
        // it hands the editor an EXEC.
        await expect(
            page.getByTestId("sql-proc-hint-prd.p_recalc"),
        ).toHaveCount(0);
        await page.getByTestId("sql-object-expand-prd.p_recalc").click();
        await expect(
            page.getByTestId("sql-proc-hint-prd.p_recalc"),
        ).toBeVisible();
        await page.getByTestId("sql-object-prd.p_recalc").click();
        await expect(page.getByTestId("sql-editor-input")).toHaveValue(
            "EXEC [prd].[p_recalc]",
        );
    });
});
