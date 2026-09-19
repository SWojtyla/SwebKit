import { test, expect } from "@playwright/test";
import { setDemoMode, resetCollections } from "./helpers";

const sidecarUrl = `http://127.0.0.1:${process.env.PLAYWRIGHT_SIDECAR_PORT ?? "5198"}`;

// Keys the tests wrote into the OS credential store — cleaned up in afterEach so the
// e2e run doesn't leave secrets behind in the user's credential manager.
const createdCredentialKeys: string[] = [];

test.describe("API Client Secret Store variables", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
    await resetCollections(page);
    await page.goto("/api-client");
  });

  test.afterEach(async ({ page }) => {
    for (const key of createdCredentialKeys.splice(0)) {
      await page.request.delete(`${sidecarUrl}/api/api-client/credentials/${encodeURIComponent(key)}`).catch(() => {});
    }
    await setDemoMode(page, false);
  });

  test("typing a secret stores it in the credential store and preview shows it masked", async ({ page }) => {
    await page.getByTestId("env-manager-button").click();
    await page.getByTestId("env-add-button").click();
    await page.getByTestId("env-name-input").fill("Credential Store Env");
    await page.getByTestId("env-add-variable").click();
    await page.getByTestId("env-var-key-0").fill("apiKey");
    await page.getByTestId("env-var-source-0").selectOption("WindowsCredentialStore");

    // The key field stays editable so a pre-existing OS credential can be referenced.
    await expect(page.getByTestId("env-var-value-0")).toHaveAttribute("placeholder", "Credential key");
    await expect(page.getByTestId("env-var-secret-0")).toBeVisible();
    await expect(page.getByTestId("env-var-preview-btn-0")).toBeDisabled();

    // Typing a secret generates a storage key and saves after the debounce.
    await page.getByTestId("env-var-secret-0").fill("e2e-secret-value");
    const generatedKey = await page.getByTestId("env-var-value-0").inputValue();
    expect(generatedKey).toMatch(/^sw-secret:/);
    createdCredentialKeys.push(generatedKey);

    await expect(page.getByTestId("env-var-secret-state-0")).toContainText("Stored in credential store", {
      timeout: 5_000,
    });

    // Preview reports existence and a masked value — never the raw secret.
    await page.getByTestId("env-var-preview-btn-0").click();
    await expect(page.getByTestId("env-var-preview-0")).toContainText("Present");
    await expect(page.getByTestId("env-var-preview-0")).not.toContainText("e2e-secret-value");
  });

  test("previewing a key with no stored credential reports it missing", async ({ page }) => {
    await page.getByTestId("env-manager-button").click();
    await page.getByTestId("env-add-button").click();
    await page.getByTestId("env-add-variable").click();
    await page.getByTestId("env-var-key-0").fill("apiKey");
    await page.getByTestId("env-var-source-0").selectOption("WindowsCredentialStore");

    await page.getByTestId("env-var-value-0").fill("definitely-missing-e2e-key");
    await page.getByTestId("env-var-preview-btn-0").click();
    await expect(page.getByTestId("env-var-preview-0")).toContainText("No credential found");
  });

  test("clearing the secret removes the credential from the store", async ({ page }) => {
    await page.getByTestId("env-manager-button").click();
    await page.getByTestId("env-add-button").click();
    await page.getByTestId("env-add-variable").click();
    await page.getByTestId("env-var-key-0").fill("apiKey");
    await page.getByTestId("env-var-source-0").selectOption("WindowsCredentialStore");

    await page.getByTestId("env-var-secret-0").fill("temporary-secret");
    const generatedKey = await page.getByTestId("env-var-value-0").inputValue();
    await expect(page.getByTestId("env-var-secret-state-0")).toContainText("Stored in credential store", {
      timeout: 5_000,
    });

    // Clearing the input deletes the stored credential — verified through the preview endpoint.
    await page.getByTestId("env-var-secret-0").fill("");
    await page.getByTestId("env-var-secret-0").blur();
    await expect
      .poll(async () => {
        const res = await page.request.post(`${sidecarUrl}/api/api-client/preview-credential`, {
          data: { key: generatedKey },
        });
        return (await res.json()).status;
      })
      .toBe("error");
  });
});

test.describe("API Client bounded faker dates", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
    await resetCollections(page);
    await page.goto("/api-client");
  });

  test("date.between shows both bound pickers and requires them", async ({ page }) => {
    await page.getByTestId("env-manager-button").click();
    await page.getByTestId("env-add-button").click();
    await page.getByTestId("env-add-variable").click();
    await page.getByTestId("env-var-key-0").fill("orderDate");
    await page.getByTestId("env-var-source-0").selectOption("Generated");
    await page.getByTestId("env-var-0-generator-kind").selectOption("Faker");

    // Non-date categories don't show bound pickers.
    await page.getByTestId("env-var-0-generator-input").selectOption("person.firstName");
    await expect(page.getByTestId("env-var-0-generator-date-after")).toHaveCount(0);

    // date.between shows both pickers and tells the user both are required.
    await page.getByTestId("env-var-0-generator-input").selectOption("date.between");
    await expect(page.getByTestId("env-var-0-generator-date-after")).toBeVisible();
    await expect(page.getByTestId("env-var-0-generator-date-before")).toBeVisible();
    await expect(page.getByTestId("env-var-0-generator-help")).toContainText("Both bounds are required");

    // Other date categories offer the pickers with a defaults hint instead.
    await page.getByTestId("env-var-0-generator-input").selectOption("date.past");
    await expect(page.getByTestId("env-var-0-generator-date-after")).toBeVisible();
    await expect(page.getByTestId("env-var-0-generator-help")).toContainText("Leave a bound empty");
  });
});
