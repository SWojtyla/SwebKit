import { test, expect, type Page } from "@playwright/test";

/**
 * Rewrites the profile response to expose two storage accounts, the same way
 * dashboard.spec.ts's patchDefaultNamespace rewrites aksConfig — demo mode always
 * overlays a single hardcoded storage account, so it can't exercise the selector.
 */
async function withTwoStorageAccounts(page: Page) {
  await page.route("**/api/config/profiles", async (route) => {
    if (route.request().method() !== "GET") {
      await route.fallback();
      return;
    }
    try {
      const response = await route.fetch();
      const body = await response.json();
      body.config = {
        ...body.config,
        storageAccounts: [
          { id: "acct-a", displayName: "Account A", accountName: "accta", connectionStringRef: null, useAad: true, allowMutations: false },
          { id: "acct-b", displayName: "Account B", accountName: "acctb", connectionStringRef: null, useAad: true, allowMutations: false },
        ],
      };
      await route.fulfill({ response, json: body });
    } catch {
      // The page can navigate away mid-flight, disposing the response.
    }
  });
}

test.describe("Storage account switching", () => {
  test.beforeEach(async ({ page }) => {
    await withTwoStorageAccounts(page);
    await page.route("**/api/storage/acct-a/containers", (route) =>
      route.fulfill({ json: [{ name: "a-container", lastModified: null, publicAccess: null, leaseStatus: null }] }),
    );
    await page.route("**/api/storage/acct-b/containers", (route) =>
      route.fulfill({ json: [{ name: "b-container", lastModified: null, publicAccess: null, leaseStatus: null }] }),
    );
  });

  test("shows an account selector and switches container list on change", async ({ page }) => {
    await page.goto("/storage");

    const select = page.getByTestId("storage-account-select");
    await expect(select).toBeVisible();
    await expect(select).toHaveValue("acct-a");
    await expect(page.getByTestId("storage-container-a-container")).toBeVisible();

    await select.selectOption("acct-b");
    await expect(select).toHaveValue("acct-b");
    await expect(page.getByTestId("storage-container-b-container")).toBeVisible();
    await expect(page.getByTestId("storage-container-a-container")).not.toBeVisible();
  });

  test("switching accounts clears the previously selected container", async ({ page }) => {
    await page.goto("/storage");

    await page.getByTestId("storage-container-a-container").click();
    await expect(page.getByTestId("storage-view-recovery")).toBeEnabled();

    await page.getByTestId("storage-account-select").selectOption("acct-b");
    await expect(page.getByTestId("storage-view-recovery")).toBeDisabled();
  });
});
