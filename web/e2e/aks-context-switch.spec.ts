import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";
import { sidecarPort } from "./test-config";

const sidecarUrl = `http://127.0.0.1:${sidecarPort}`;

async function openContextDropdown(page: import("@playwright/test").Page) {
    await page.getByTestId("aks-context-select").click();
}

async function checkContext(
    page: import("@playwright/test").Page,
    name: string,
) {
    await openContextDropdown(page);
    const checkbox = page.getByTestId(`aks-context-check-${name}`);
    await checkbox.click();
    await expect(checkbox).toBeChecked();
    await page.keyboard.press("Escape");
}

test.describe("AKS context selection", () => {
    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, true);
        await page.goto("/aks");
    });

    test.afterEach(async ({ page }) => {
        await setDemoMode(page, false);
    });

    test("demo mode lists the demo contexts in the picker", async ({
        page,
    }) => {
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
        await expect(
            page.getByRole("option", { name: "minikube" }),
        ).toBeVisible();

        await page.keyboard.press("Escape");
        await expect(
            page.getByRole("option", { name: "minikube" }),
        ).toHaveCount(0);
    });

    test("selecting a context restores that cluster's remembered namespace", async ({
        page,
    }) => {
        await checkContext(page, "minikube");
        await page.getByTestId("aks-namespace-dropdown").click();
        await expect(page.getByTestId("aks-ns-group-minikube")).toBeVisible();
        await page.keyboard.press("Escape");

        // Remember "payments" for minikube — hidden native select encodes
        // non-default contexts as `ctx:ns`. Wait for the option so the namespace
        // query has resolved before selecting.
        const nsSelect = page.getByTestId("aks-namespace-select");
        await expect(
            nsSelect.locator('option[value="minikube:payments"]'),
        ).toBeAttached();
        await nsSelect.selectOption("minikube:payments");

        // Drop and re-select minikube — its remembered "payments" comes back.
        await openContextDropdown(page);
        const minikubeCheck = page.getByTestId("aks-context-check-minikube");
        await minikubeCheck.click();
        await expect(minikubeCheck).not.toBeChecked();
        await minikubeCheck.click();
        await expect(minikubeCheck).toBeChecked();
        await page.keyboard.press("Escape");

        await page.getByTestId("aks-namespace-dropdown").click();
        const paymentsRow = page
            .getByTestId("aks-ns-group-minikube")
            .locator("label", { hasText: "payments" });
        try {
            await expect(
                paymentsRow.locator('input[type="checkbox"]'),
            ).toBeChecked();
        } catch (e) {
            const prefs = await page.evaluate(() =>
                Object.entries(localStorage).filter(([k]) =>
                    k.includes("view-pref"),
                ),
            );
            console.log(
                "diagnostics:",
                JSON.stringify({ url: page.url(), prefs }),
            );
            throw e;
        }
    });

    test("the last selected context cannot be deselected", async ({ page }) => {
        await openContextDropdown(page);
        const checkbox = page.getByTestId(
            "aks-context-check-aks-ecommerce-dev",
        );
        await expect(checkbox).toBeChecked();
        await expect(checkbox).toBeDisabled();
    });
});

test.describe("AKS first-run state", () => {
    test("unconfigured AKS shows an empty state with a settings CTA", async ({
        page,
    }) => {
        await setDemoMode(page, false);

        // Snapshot the profile, then save it with aksConfig stripped so the page hits the
        // not-configured branch. Restored in the finally so the dev's real config survives.
        const profileRes = await page.request.get(
            `${sidecarUrl}/api/config/profiles`,
        );
        const original = (await profileRes.json()) as Record<string, unknown>;
        const stripped = structuredClone(original) as {
            config: { aksConfig?: unknown };
        };
        stripped.config.aksConfig = null;

        try {
            await page.request.put(`${sidecarUrl}/api/config/profiles`, {
                data: stripped,
            });

            await page.goto("/aks");
            await expect(page.getByTestId("aks-first-run")).toBeVisible();
            await expect(page.getByTestId("aks-first-run")).toContainText(
                "No AKS cluster configured",
            );

            await page.getByTestId("aks-configure-cta").click();
            await expect(page).toHaveURL(/\/settings/);
        } finally {
            await page.request.put(`${sidecarUrl}/api/config/profiles`, {
                data: original,
            });
        }
    });
});
