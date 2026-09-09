import { test, expect } from "@playwright/test";
import { setDemoMode, resetCollections } from "./helpers";

const sidecarUrl = `http://127.0.0.1:${process.env.PLAYWRIGHT_SIDECAR_PORT ?? "5198"}`;

const COLLECTION_ID = "11111111-1111-1111-1111-111111111111";
const REQUEST_ID = "22222222-2222-2222-2222-222222222222";

/**
 * Seeds one collection holding a request plus the variables the highlighting is
 * asserted against — going through the sidecar rather than the UI so the tests
 * exercise the highlighting, not the collection-variable editor.
 */
async function seedCollectionWithVariables(
  page: import("@playwright/test").Page,
  url: string,
  body: { mode: string; rawContent: string | null } = { mode: "None", rawContent: null },
) {
  const now = new Date().toISOString();
  await page.request.put(`${sidecarUrl}/api/config/collections`, {
    data: {
      schemaVersion: 1,
      collections: [
        {
          id: COLLECTION_ID,
          name: "Var Highlight Collection",
          variables: [
            { key: "Phone_Api_Url", value: "https://api-dev.example.com/v1", generator: null, isEnabled: true },
            { key: "ApiSecret", value: null, generator: { kind: "Guid" }, isEnabled: true },
          ],
          defaultAuth: null,
          createdAt: now,
          updatedAt: now,
          nodes: [
            {
              id: REQUEST_ID,
              type: "Request",
              name: "Highlighted",
              isExpanded: true,
              children: [],
              defaultAuth: null,
              request: {
                id: REQUEST_ID,
                name: "Highlighted",
                method: "Get",
                url,
                headers: [{ key: "X-Trace", value: "{{Phone_Api_Url}}|{{Nope}}", isEnabled: true }],
                queryParams: [],
                body: { ...body, contentType: null, formFields: [] },
                auth: null,
                captureRules: [],
                preRequestActions: [],
                postRequestActions: [],
                responseExamples: [],
                createdAt: now,
                updatedAt: now,
              },
            },
          ],
        },
      ],
    },
  });
}

test.describe("API Client variable highlighting", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
    await resetCollections(page);
  });

  test.afterEach(async ({ page }) => {
    await resetCollections(page);
  });

  test("colours URL variables by whether they resolve, with no preview click", async ({ page }) => {
    await seedCollectionWithVariables(page, "{{Phone_Api_Url}}/incomingcall?key={{Nope}}&sig={{ApiSecret}}");
    await page.goto("/api-client");
    await page.getByTestId(/collection-node-Request-/).filter({ hasText: "Highlighted" }).click();

    const highlight = page.getByTestId("request-url-input-highlight");
    await expect(highlight).toBeAttached();

    // The variable-preview panel must still be closed: the point is that the
    // resolution state is visible without opening it.
    await expect(page.getByTestId("variable-preview")).toHaveCount(0);

    await expect(highlight.locator(".var-tok-resolved")).toHaveText(["{{Phone_Api_Url}}"]);
    await expect(highlight.locator(".var-tok-unresolved")).toHaveText(["{{Nope}}"]);
    // Generated / credential-store / Key Vault values are known only at send
    // time — amber, not red, so a correctly configured secret is not an error.
    await expect(highlight.locator(".var-tok-deferred")).toHaveText(["{{ApiSecret}}"]);
  });

  test("keeps the highlight in step with typing and leaves plain URLs alone", async ({ page }) => {
    await seedCollectionWithVariables(page, "https://plain.example.com/a");
    await page.goto("/api-client");
    await page.getByTestId(/collection-node-Request-/).filter({ hasText: "Highlighted" }).click();

    const highlight = page.getByTestId("request-url-input-highlight");
    await expect(highlight).toHaveText("https://plain.example.com/a");
    await expect(highlight.locator(".var-tok-resolved")).toHaveCount(0);
    await expect(highlight.locator(".var-tok-unresolved")).toHaveCount(0);

    await page.getByTestId("request-url-input").fill("{{Phone_Api_Url}}/typed");
    await expect(highlight).toHaveText("{{Phone_Api_Url}}/typed");
    await expect(highlight.locator(".var-tok-resolved")).toHaveText(["{{Phone_Api_Url}}"]);

    await page.getByTestId("request-url-input").fill("{{Typo_Api_Url}}/typed");
    await expect(highlight.locator(".var-tok-unresolved")).toHaveText(["{{Typo_Api_Url}}"]);
    await expect(highlight.locator(".var-tok-resolved")).toHaveCount(0);
  });

  test("highlights header values too", async ({ page }) => {
    await seedCollectionWithVariables(page, "https://plain.example.com/a");
    await page.goto("/api-client");
    await page.getByTestId(/collection-node-Request-/).filter({ hasText: "Highlighted" }).click();
    await page.getByTestId("request-tab-headers").click();

    const highlight = page.getByTestId("request-header-value-0-highlight");
    await expect(highlight.locator(".var-tok-resolved")).toHaveText(["{{Phone_Api_Url}}"]);
    await expect(highlight.locator(".var-tok-unresolved")).toHaveText(["{{Nope}}"]);
  });

  test("highlights variables in the request body, not just the URL", async ({ page }) => {
    // Short body on purpose: CodeMirror only renders the visible viewport, so a
    // long document would put the assertion target outside the DOM.
    await seedCollectionWithVariables(page, "https://plain.example.com/a", {
      mode: "Json",
      rawContent: '{"a":"{{Phone_Api_Url}}","b":"{{Nope}}","c":"{{ApiSecret}}"}',
    });
    await page.goto("/api-client");
    await page.getByTestId(/collection-node-Request-/).filter({ hasText: "Highlighted" }).click();
    await page.getByTestId("request-tab-body").click();

    const editor = page.getByTestId("request-body-codemirror");
    await expect(editor.locator(".var-tok-resolved")).toHaveText(["{{Phone_Api_Url}}"]);
    await expect(editor.locator(".var-tok-unresolved")).toHaveText(["{{Nope}}"]);
    await expect(editor.locator(".var-tok-deferred")).toHaveText(["{{ApiSecret}}"]);
  });

  test("warns about an undefined body variable without opening the preview", async ({ page }) => {
    // The regression this guards: an undefined variable is substituted with its own
    // literal text, so the request goes out containing `{{Nope}}` and the server
    // rejects it. Nothing used to say so before the send.
    await seedCollectionWithVariables(page, "https://plain.example.com/a", {
      mode: "Json",
      rawContent: '{"b":"{{Nope}}"}',
    });
    await page.goto("/api-client");
    await page.getByTestId(/collection-node-Request-/).filter({ hasText: "Highlighted" }).click();

    await expect(page.getByTestId("variable-preview")).toHaveCount(0);
    // `{{Nope}}` also appears in the seeded X-Trace header, so the warning names it once.
    await expect(page.getByTestId("unresolved-variable-warning")).toContainText("Nope");
  });
});

test.describe("API Client response formatting", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
    await resetCollections(page);
  });

  test.afterEach(async ({ page }) => {
    await resetCollections(page);
  });

  test("prettifies the response body by default and remembers a switch to Raw", async ({ page }) => {
    await seedCollectionWithVariables(page, `${sidecarUrl}/health`);
    // A minified payload, so Pretty vs Raw is unambiguous.
    await page.route("**/api/api-client/execute", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          resolvedUrl: "https://example.com/x",
          method: "GET",
          statusCode: 200,
          statusText: "OK",
          errorMessage: null,
          elapsedMs: 3,
          contentLength: 23,
          contentType: "application/json",
          responseBody: '{"a":1,"b":{"c":[1,2]}}',
          responseBodyTruncated: false,
          headers: [],
          captureWarnings: [],
          graphQlErrors: null,
        }),
      });
    });

    await page.goto("/api-client");
    await page.getByTestId(/collection-node-Request-/).filter({ hasText: "Highlighted" }).click();
    await page.getByTestId("request-send-button").click();
    await expect(page.getByTestId("response-pretty-toggle")).toBeVisible({ timeout: 10_000 });

    // Pretty, with no click: a 4000-character single line is not a readable body.
    await expect(page.getByTestId("response-pretty-toggle")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("response-body")).toContainText('"a": 1');

    await page.getByTestId("response-raw-toggle").click();
    await expect(page.getByTestId("response-body")).toContainText('{"a":1,"b":{"c":[1,2]}}');

    // The choice is a view preference, not per-response state, so it survives
    // both the next send and a reload.
    await page.getByTestId("request-send-button").click();
    await expect(page.getByTestId("response-raw-toggle")).toHaveAttribute("aria-pressed", "true");

    await page.reload();
    await page.getByTestId(/collection-node-Request-/).filter({ hasText: "Highlighted" }).click();
    await page.getByTestId("request-send-button").click();
    await expect(page.getByTestId("response-raw-toggle")).toHaveAttribute("aria-pressed", "true", {
      timeout: 10_000,
    });
  });
});
