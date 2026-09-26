import { test, expect } from "@playwright/test";
import { createServer, type Server, type IncomingMessage, type ServerResponse } from "node:http";
import type { AddressInfo } from "node:net";
import { setDemoMode, resetCollections } from "./helpers";
import { sidecarPort } from "./test-config";

const sidecarBaseUrl = `http://127.0.0.1:${sidecarPort}`;

interface TokenCall {
  body: URLSearchParams;
}

/**
 * OAuth 2.0 authorization-code + PKCE.
 *
 * A real identity provider can't run inside the suite, but the flow's security-relevant parts
 * all live sidecar-side: state validation, the loopback callback, and the code exchange (which
 * runs on the sidecar's HttpClient, invisible to browser request interception). A throwaway
 * Node HTTP stub therefore plays the provider's token endpoint — it sees the real PKCE
 * verifier the sidecar sends, and the app's sign-in UI polls the real result endpoint.
 * `window.open` is stubbed to record the authorize URL instead of navigating a popup to a
 * dead address.
 */
test.describe("API Client OAuth2 authorization-code + PKCE", () => {
  let stub: Server;
  let stubBaseUrl: string;
  let tokenCalls: TokenCall[];
  let tokenStatus: number;
  let tokenPayload: Record<string, unknown>;

  test.beforeEach(async ({ page }) => {
    tokenCalls = [];
    tokenStatus = 200;
    tokenPayload = {
      access_token: "e2e-access-token",
      refresh_token: "e2e-refresh-token",
      expires_in: 3600,
    };
    stub = createServer((req: IncomingMessage, res: ServerResponse) => {
      if (req.method === "POST" && req.url === "/token") {
        let raw = "";
        req.on("data", (c) => (raw += c));
        req.on("end", () => {
          tokenCalls.push({ body: new URLSearchParams(raw) });
          res.writeHead(tokenStatus, { "Content-Type": "application/json" });
          res.end(
            JSON.stringify(
              tokenStatus === 200
                ? tokenPayload
                : { error: "invalid_grant", error_description: "bad code" },
            ),
          );
        });
        return;
      }
      res.writeHead(404).end();
    });
    await new Promise<void>((resolve) => stub.listen(0, "127.0.0.1", resolve));
    stubBaseUrl = `http://127.0.0.1:${(stub.address() as AddressInfo).port}`;

    await page.addInitScript(() => {
      (window as unknown as { __openedUrls: string[] }).__openedUrls = [];
      window.open = (url) => {
        (window as unknown as { __openedUrls: string[] }).__openedUrls.push(String(url));
        return null;
      };
    });

    await setDemoMode(page, false);
    await resetCollections(page);
    await page.goto("/api-client");
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
    await new Promise<void>((resolve) => stub.close(() => resolve()));
  });

  /** Creates a request, opens its Auth tab, and switches to OAuth2 authorization-code. */
  async function openAuthCodeEditor(page: import("@playwright/test").Page) {
    await page.getByTestId("add-collection-button").click();
    await page.getByTestId("name-dialog-input").fill("OAuth Collection");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-root-/).first().click();

    await page.getByTestId("add-request-button").click();
    await page.getByTestId("name-dialog-input").fill("OAuth Request");
    await page.getByTestId("name-dialog-confirm").click();
    await page.getByTestId(/collection-node-Request-/).first().click();

    await page.getByTestId("request-tab-auth").click();
    await page.getByTestId("auth-type-select").selectOption("OAuth2");
    await page.getByTestId("auth-oauth2-grant").selectOption("AuthorizationCode");
  }

  async function fillEndpoints(page: import("@playwright/test").Page) {
    await page.getByTestId("auth-oauth2-auth-url").fill(`${stubBaseUrl}/authorize`);
    await page.getByTestId("auth-oauth2-token-url").fill(`${stubBaseUrl}/token`);
    await page.getByTestId("auth-oauth2-client-id").fill("e2e-client");
    await page.getByTestId("auth-oauth2-scopes").fill("openid profile");
  }

  test("completes the browser sign-in and keeps tokens out of the collection", async ({
    page,
  }) => {
    await openAuthCodeEditor(page);

    const signIn = page.getByTestId("auth-oauth2-signin");
    await expect(signIn).toBeDisabled();
    await fillEndpoints(page);
    await expect(signIn).toBeEnabled();

    await signIn.click();
    await expect(signIn).toHaveText("Waiting for sign-in…");

    // "Waiting for sign-in…" renders before the authorize POST round-trips — wait until
    // the flow actually handed a URL to the browser stub.
    await page.waitForFunction(
      () =>
        (window as unknown as { __openedUrls: string[] }).__openedUrls.length > 0,
    );
    const opened = await page.evaluate(
      () => (window as unknown as { __openedUrls: string[] }).__openedUrls,
    );
    expect(opened).toHaveLength(1);
    const authorize = new URL(opened[0]);
    expect(authorize.origin + authorize.pathname).toBe(`${stubBaseUrl}/authorize`);
    expect(authorize.searchParams.get("client_id")).toBe("e2e-client");
    expect(authorize.searchParams.get("response_type")).toBe("code");
    expect(authorize.searchParams.get("code_challenge_method")).toBe("S256");
    expect(authorize.searchParams.get("code_challenge")).toBeTruthy();
    expect(authorize.searchParams.get("state")).toBeTruthy();
    expect(authorize.searchParams.get("scope")).toBe("openid profile");
    const redirectUri = authorize.searchParams.get("redirect_uri")!;
    expect(redirectUri).toBe(`${sidecarBaseUrl}/api/api-client/oauth/callback`);

    // The browser navigates to the loopback callback as the provider redirect target.
    const callback = await page.request.get(
      `${redirectUri}?code=e2e-auth-code&state=${authorize.searchParams.get("state")}`,
    );
    expect(callback.status()).toBe(200);
    expect(await callback.text()).toContain("Signed in");

    // The app tab polls the result endpoint and flips to the signed-in chip.
    await expect(page.getByTestId("auth-oauth2-signed-in")).toBeVisible({
      timeout: 10_000,
    });

    // The sidecar's token exchange hit the stub with the real PKCE verifier.
    expect(tokenCalls).toHaveLength(1);
    const exchange = tokenCalls[0].body;
    expect(exchange.get("grant_type")).toBe("authorization_code");
    expect(exchange.get("code")).toBe("e2e-auth-code");
    expect(exchange.get("code_verifier")).toBeTruthy();
    expect(exchange.get("redirect_uri")).toBe(redirectUri);
    expect(exchange.get("client_id")).toBe("e2e-client");

    // The persisted collection holds the credential-store key reference — never the tokens.
    await expect
      .poll(async () => {
        const res = await page.request.get(`${sidecarBaseUrl}/api/config/collections`);
        const json = await res.text();
        return json.includes("oAuth2TokenCredentialKey");
      }, { timeout: 10_000 })
      .toBe(true);
    const collections = await (
      await page.request.get(`${sidecarBaseUrl}/api/config/collections`)
    ).text();
    expect(collections).not.toContain("e2e-access-token");
    expect(collections).not.toContain("e2e-refresh-token");
  });

  test("surfaces the provider exchange failure in the editor", async ({ page }) => {
    await openAuthCodeEditor(page);
    await fillEndpoints(page);
    tokenStatus = 400;
    await page.getByTestId("auth-oauth2-signin").click();

    await page.waitForFunction(
      () =>
        (window as unknown as { __openedUrls: string[] }).__openedUrls.length > 0,
    );
    const opened = await page.evaluate(
      () => (window as unknown as { __openedUrls: string[] }).__openedUrls,
    );
    const authorize = new URL(opened[0]);
    const redirectUri = authorize.searchParams.get("redirect_uri")!;

    const callback = await page.request.get(
      `${redirectUri}?code=bad-code&state=${authorize.searchParams.get("state")}`,
    );
    expect(await callback.text()).toContain("Sign-in failed");

    await expect(page.getByTestId("auth-oauth2-signin-error")).toBeVisible({
      timeout: 10_000,
    });
  });

  test("rejects a forged-state callback without killing the real flow", async ({
    page,
  }) => {
    await openAuthCodeEditor(page);
    await fillEndpoints(page);
    await page.getByTestId("auth-oauth2-signin").click();

    await page.waitForFunction(
      () =>
        (window as unknown as { __openedUrls: string[] }).__openedUrls.length > 0,
    );
    const opened = await page.evaluate(
      () => (window as unknown as { __openedUrls: string[] }).__openedUrls,
    );
    const authorize = new URL(opened[0]);
    const redirectUri = authorize.searchParams.get("redirect_uri")!;

    // A callback with a state that doesn't match the pending flow is rejected — and must
    // not terminate the legitimate sign-in (a stray request would otherwise DoS it).
    const forged = await page.request.get(
      `${redirectUri}?code=e2e-auth-code&state=forged-state`,
    );
    expect(await forged.text()).toContain("Sign-in failed");
    expect(tokenCalls).toHaveLength(0);
    await expect(page.getByTestId("auth-oauth2-signin")).toHaveText(
      "Waiting for sign-in…",
    );

    // The real provider redirect still completes the flow.
    const callback = await page.request.get(
      `${redirectUri}?code=e2e-auth-code&state=${authorize.searchParams.get("state")}`,
    );
    expect(await callback.text()).toContain("Signed in");
    await expect(page.getByTestId("auth-oauth2-signed-in")).toBeVisible({
      timeout: 10_000,
    });
    expect(tokenCalls).toHaveLength(1);
  });
});
