import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";
import { sidecarPort } from "./test-config";

const sidecarUrl = `http://127.0.0.1:${sidecarPort}`;

async function openContextDropdown(page: import("@playwright/test").Page) {
  await page.getByTestId("aks-context-select").click();
}

test.describe("AKS context switching", () => {
  test.beforeEach(async ({ page }) => {
    await setDemoMode(page, true);
    await page.goto("/aks");
  });

  test.afterEach(async ({ page }) => {
    await setDemoMode(page, false);
  });

  test("demo mode lists the demo contexts in the picker", async ({ page }) => {
    await openContextDropdown(page);

    for (const name of [
      "aks-ecommerce-dev",
      "aks-ecommerce-staging",
      "aks-ecommerce-prod",
      "aks-platform-dev",
      "minikube",
    ]) {
      await expect(page.getByRole("option", { name })).toBeVisible();
    }
  });

  test("the context picker closes on Escape", async ({ page }) => {
    await openContextDropdown(page);
    await expect(page.getByRole("option", { name: "minikube" })).toBeVisible();

    await page.keyboard.press("Escape");
    await expect(page.getByRole("option", { name: "minikube" })).toHaveCount(0);
  });

  test("switching context restores that context's remembered namespace", async ({ page }) => {
    // minikube's kubeconfig namespace hint is "default".
    await openContextDropdown(page);
    await page.getByRole("option", { name: "minikube" }).click();
    await expect(page.getByTestId("notification-toasts")).toContainText("AKS context switched");
    await expect(page.getByTestId("aks-context-select")).toContainText("minikube");
    await expect(page.getByTestId("aks-namespace-dropdown")).toContainText("default");

    // Remember "payments" for minikube.
    await page.getByTestId("aks-namespace-select").selectOption("payments");
    await expect(page.getByTestId("aks-namespace-dropdown")).toContainText("payments");

    // aks-ecommerce-prod has no remembered selection — falls back to its kubeconfig hint.
    await openContextDropdown(page);
    await page.getByRole("option", { name: "aks-ecommerce-prod" }).click();
    await expect(page.getByTestId("aks-namespace-dropdown")).toContainText("ecommerce");

    // Switching back restores minikube's remembered "payments" — not prod's list or selection.
    await openContextDropdown(page);
    await page.getByRole("option", { name: "minikube" }).click();
    await expect(page.getByTestId("aks-namespace-dropdown")).toContainText("payments");
  });

  test("a failed connection test reports the failure and keeps the previous selection", async ({ page }) => {
    // Fail only the switch to prod — other contexts still connect.
    await page.route("**/api/aks/context", async (route) => {
      const body = route.request().postDataJSON() as { context?: string };
      if (body.context === "aks-ecommerce-prod") {
        await route.fulfill({
          status: 200,
          json: { connected: false, context: body.context, error: "Cluster unreachable" },
        });
      } else {
        await route.fallback();
      }
    });

    await openContextDropdown(page);
    await page.getByRole("option", { name: "minikube" }).click();
    await page.getByTestId("aks-namespace-select").selectOption("payments");
    await expect(page.getByTestId("aks-namespace-dropdown")).toContainText("payments");

    await openContextDropdown(page);
    await page.getByRole("option", { name: "aks-ecommerce-prod" }).click();

    await expect(page.getByTestId("notification-toasts")).toContainText("Couldn't switch AKS context");
    await expect(page.getByTestId("notification-toasts")).toContainText("Cluster unreachable");
    // No fake success, and the previous context + selection are still in place.
    await expect(page.getByTestId("notification-toasts")).not.toContainText("AKS context switched");
    await expect(page.getByTestId("aks-context-select")).toContainText("minikube");
    await expect(page.getByTestId("aks-namespace-dropdown")).toContainText("payments");
  });

  test("a failing context request reports the error and keeps the previous selection", async ({ page }) => {
    await page.route("**/api/aks/context", async (route) => {
      const body = route.request().postDataJSON() as { context?: string };
      if (body.context === "aks-ecommerce-prod") {
        await route.fulfill({ status: 500, json: { error: "sidecar exploded" } });
      } else {
        await route.fallback();
      }
    });

    await openContextDropdown(page);
    await page.getByRole("option", { name: "minikube" }).click();
    await expect(page.getByTestId("aks-context-select")).toContainText("minikube");

    await openContextDropdown(page);
    await page.getByRole("option", { name: "aks-ecommerce-prod" }).click();

    await expect(page.getByTestId("notification-toasts")).toContainText("Couldn't switch AKS context");
    await expect(page.getByTestId("aks-context-select")).toContainText("minikube");
  });

  test("the switching stage is labelled while the request is in flight", async ({ page }) => {
    // Hold the POST so the pending state is observable.
    await page.route("**/api/aks/context", async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 800));
      await route.fallback();
    });

    await openContextDropdown(page);
    await page.getByRole("option", { name: "minikube" }).click();

    await expect(page.getByTestId("aks-loading-indicator")).toContainText("Switching context");
    await expect(page.getByTestId("aks-context-select")).toContainText("Switching to minikube");
    await expect(page.getByTestId("aks-switching-state")).toBeVisible();

    await expect(page.getByTestId("aks-context-select")).toContainText("minikube", { timeout: 10_000 });
    await expect(page.getByTestId("aks-namespace-dropdown")).toContainText("default");
  });
});

test.describe("AKS first-run state", () => {
  test("unconfigured AKS shows an empty state with a settings CTA", async ({ page }) => {
    await setDemoMode(page, false);

    // Snapshot the profile, then save it with aksConfig stripped so the page hits the
    // not-configured branch. Restored in the finally so the dev's real config survives.
    const profileRes = await page.request.get(`${sidecarUrl}/api/config/profiles`);
    const original = (await profileRes.json()) as Record<string, unknown>;
    const stripped = structuredClone(original) as {
      config: { aksConfig?: unknown };
    };
    stripped.config.aksConfig = null;

    try {
      await page.request.put(`${sidecarUrl}/api/config/profiles`, { data: stripped });

      await page.goto("/aks");
      await expect(page.getByTestId("aks-first-run")).toBeVisible();
      await expect(page.getByTestId("aks-first-run")).toContainText("No AKS cluster configured");

      await page.getByTestId("aks-configure-cta").click();
      await expect(page).toHaveURL(/\/settings/);
    } finally {
      await page.request.put(`${sidecarUrl}/api/config/profiles`, { data: original });
    }
  });
});
