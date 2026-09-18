import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("shared SearchableSelect", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("service bus namespace picker filters and selects via keyboard", async ({ page }) => {
    await page.goto("/service-bus");

    await page.getByTestId("sb-namespace-button").click();
    const filter = page.getByTestId("sb-namespace-filter");
    await expect(filter).toBeFocused();

    // Both demo namespaces are listed with role=option and their FQDN subtitle.
    await expect(page.getByRole("option", { name: "orders-dev" })).toBeVisible();
    await expect(page.getByRole("option", { name: "payments-dev" })).toBeVisible();

    await filter.fill("payments");
    await expect(page.getByRole("option")).toHaveCount(1);

    // Enter picks the highlighted option and applies the namespace.
    await page.keyboard.press("Enter");
    await expect(page.getByRole("listbox")).not.toBeVisible();
    await expect(page.getByTestId("sb-namespace-button")).toContainText("payments-dev");
  });

  test("escape closes the dropdown and returns focus to the trigger", async ({ page }) => {
    await page.goto("/service-bus");
    const button = page.getByTestId("sb-namespace-button");
    await button.click();
    await expect(page.getByRole("listbox")).toBeVisible();

    await page.keyboard.press("Escape");
    await expect(page.getByRole("listbox")).not.toBeVisible();
    await expect(button).toBeFocused();
  });

  test("sql connection picker shows server subtitles and switches connection", async ({ page }) => {
    await page.goto("/sql");
    await page.getByTestId("sql-connection-button").click();

    // The optgroup label from the old native select is now a subtitle on each option.
    const prod = page.getByRole("option", { name: "orders-prod-sql" });
    await expect(prod).toContainText("orders-prod-sql.database.windows.net");

    await prod.click();
    await expect(page).toHaveURL(/connection=demo-sql-2/);
    // The label is database || displayName, same as the old native options.
    await expect(page.getByTestId("sql-connection-button")).toContainText("orders");
  });
});
