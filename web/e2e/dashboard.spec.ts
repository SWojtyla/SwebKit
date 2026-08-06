import { test, expect } from "@playwright/test";
import type { Page } from "@playwright/test";
import { setDemoMode } from "./helpers";

/**
 * Rewrites the AKS default namespace in the profile response so a test can
 * exercise both the "configured" and "not configured" branches without touching
 * the sidecar's stored config.
 */
async function patchDefaultNamespace(page: Page, defaultNamespace: string) {
  await page.route("**/api/config/profiles", async (route) => {
    try {
      const response = await route.fetch();
      const body = await response.json();
      body.config = { ...body.config, aksConfig: { ...body.config?.aksConfig, defaultNamespace } };
      await route.fulfill({ response, json: body });
    } catch {
      // The page can navigate away mid-flight, disposing the response. Letting
      // the original request through keeps that from failing the test.
      await route.continue().catch(() => {});
    }
  });
}

/** Records the paths of every /api/aks/ request the page makes from now on. */
function collectAksRequests(page: Page): string[] {
  const paths: string[] = [];
  page.on("request", (req) => {
    const { pathname } = new URL(req.url());
    if (pathname.startsWith("/api/aks/")) paths.push(pathname);
  });
  return paths;
}

test.describe("Dashboard", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("loads and shows sidecar connection", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("dashboard-title")).toHaveText("AI Cockpit");
    await expect(page.getByTestId("sidecar-status-text")).toContainText("Connected");
  });

  test("navigates to AKS and Settings from dashboard", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("service-card-aks").click();
    await expect(page).toHaveURL(/\/aks$/);
    await page.getByTestId("nav-dashboard").click();
    await expect(page).toHaveURL(/\/$/);
    await page.getByTestId("settings-quick-link").click();
    await expect(page).toHaveURL(/\/settings$/);
  });

  test("toggles demo mode and updates service bus namespace count", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByTestId("service-card-service-bus")).toContainText("0 namespaces");
    await setDemoMode(page, true);
    await page.goto("/");
    await expect(page.getByTestId("service-card-service-bus")).toContainText("2 namespaces");
  });

  test("shows health tiles for each service", async ({ page }) => {
    await setDemoMode(page, true);
    await page.goto("/");
    await expect(page.getByTestId("health-tiles")).toBeVisible();
    await expect(page.getByTestId("health-tile-service-bus")).toBeVisible();
    await expect(page.getByTestId("health-tile-aks")).toBeVisible();
    await expect(page.getByTestId("health-tile-redis")).toBeVisible();
    await expect(page.getByTestId("health-tile-storage")).toBeVisible();
  });

  // Regression: the dashboard used to query a hardcoded "default" namespace.
  // Most users have no RBAC there, so those calls returned 403 after ~37s while
  // holding browser connections, stalling every other page. It must use the
  // namespace from the profile instead, and must not pay for the slow
  // cluster-scoped namespace listing when it already knows which one to use.
  test("queries the configured AKS namespace instead of a hardcoded default", async ({ page }) => {
    await patchDefaultNamespace(page, "team-alpha");
    await setDemoMode(page, true);

    // Collect only after demo mode is settled, so this measures one clean
    // dashboard load rather than the toggle's navigation as well.
    const aksPaths = collectAksRequests(page);
    await page.goto("/");
    await expect(page.getByTestId("watch-tile-deployments")).toBeVisible();

    await expect
      .poll(() => aksPaths.some((p) => p.startsWith("/api/aks/team-alpha/")))
      .toBe(true);
    expect(aksPaths.filter((p) => p.startsWith("/api/aks/default/"))).toEqual([]);
    expect(aksPaths).not.toContain("/api/aks/namespaces");
  });

  // The opposite branch: with no namespace configured the dashboard still has to
  // discover one, so the listing call must not be gated away entirely.
  test("falls back to listing namespaces when none is configured", async ({ page }) => {
    await patchDefaultNamespace(page, "");
    // The demo namespace list starts with "default", which would make this
    // indistinguishable from the old hardcoded behaviour. Serve a list that
    // contains no "default" so the assertion can only pass if the dashboard
    // really is using the discovered namespaces.
    await page.route("**/api/aks/namespaces", async (route) => {
      try {
        const response = await route.fetch();
        await route.fulfill({ response, json: ["zeta-ns", "beta-ns"] });
      } catch {
        await route.continue().catch(() => {});
      }
    });
    await setDemoMode(page, true);

    const aksPaths = collectAksRequests(page);
    await page.goto("/");
    await expect(page.getByTestId("watch-tile-deployments")).toBeVisible();

    await expect.poll(() => aksPaths.includes("/api/aks/namespaces")).toBe(true);
    await expect
      .poll(() => aksPaths.some((p) => p.startsWith("/api/aks/zeta-ns/")))
      .toBe(true);
    expect(aksPaths.filter((p) => p.startsWith("/api/aks/default/"))).toEqual([]);
  });

  test("shows watch tiles with metrics", async ({ page }) => {
    await setDemoMode(page, true);
    await page.goto("/");
    await expect(page.getByTestId("watch-tiles")).toBeVisible();
    await expect(page.getByTestId("watch-tile-deployments")).toBeVisible();
    await expect(page.getByTestId("watch-tile-pods")).toBeVisible();
    await expect(page.getByTestId("watch-tile-containers")).toBeVisible();
    await expect(page.getByTestId("watch-tile-cache-hit-rate")).toBeVisible();
  });

  test("pins a resource and shows it in the pinned list", async ({ page }) => {
    await setDemoMode(page, true);
    await page.goto("/");
    await expect(page.getByTestId("dashboard-resource-rows")).toBeVisible();
    await page.getByTestId("pin-resource-redis").click();
    await expect(page.getByTestId("pinned-resource-redis")).toBeVisible();
    await expect(page.getByTestId("pin-resource-redis")).toHaveAttribute("aria-label", "Unpin Redis");
  });

  test("starts demo tour and navigates to the next stop", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("demo-tour-start").click();
    await expect(page.getByTestId("demo-tour-card")).toBeVisible();
    await expect(page.getByTestId("demo-tour-step-title")).toHaveText("AI Cockpit");
    await page.getByTestId("demo-tour-next").click();
    await expect(page).toHaveURL(/\/aks$/);
    await expect(page.getByTestId("demo-tour-step-title")).toHaveText("Kubernetes");
    await page.getByTestId("demo-tour-stop").click();
    await expect(page.getByTestId("demo-tour-card")).toHaveCount(0);
  });
});
