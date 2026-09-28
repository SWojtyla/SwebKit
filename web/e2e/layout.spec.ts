import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Layout", () => {
    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, true);
    });

    test.afterEach(async ({ page }) => {
        await setDemoMode(page, false);
    });

    test("top bar and status bar are visible", async ({ page }) => {
        await page.goto("/");
        await expect(page.getByTestId("top-bar")).toBeVisible();
        await expect(page.getByTestId("status-bar")).toBeVisible();
        await expect(page.getByTestId("status-bar-connection")).toBeVisible();
    });

    test("status bar shows live health for each infrastructure area", async ({
        page,
    }) => {
        await page.goto("/");
        const areas = ["service-bus", "aks", "redis", "storage"];

        for (const area of areas) {
            await expect(
                page.getByTestId(`status-bar-health-${area}`),
            ).toBeVisible();
        }

        await expect(
            page.getByTestId("status-bar-health-service-bus"),
        ).toHaveAttribute("aria-label", "Service Bus: Connected");
        await expect(page.getByTestId("status-bar-health-aks")).toHaveAttribute(
            "aria-label",
            "AKS: Connected",
        );
        await expect(
            page.getByTestId("status-bar-health-redis"),
        ).toHaveAttribute("aria-label", "Redis: Connected");
        await expect(
            page.getByTestId("status-bar-health-storage"),
        ).toHaveAttribute("aria-label", "Storage: Connected");
    });

    test("command palette opens via trigger button", async ({ page }) => {
        await page.goto("/");
        await page.getByTestId("command-palette-trigger").click();
        await expect(page.getByTestId("command-palette")).toBeVisible();
        await expect(page.getByTestId("command-palette-input")).toBeVisible();
        // Should show all commands
        await expect(
            page.getByTestId("command-palette-item-dashboard"),
        ).toBeVisible();
        await expect(
            page.getByTestId("command-palette-item-aks"),
        ).toBeVisible();
    });

    test("command palette navigates to selected page", async ({ page }) => {
        await page.goto("/");
        await page.getByTestId("command-palette-trigger").click();
        await page.getByTestId("command-palette-item-redis").click();
        await expect(page).toHaveURL(/\/redis$/);
    });

    test("command palette closes on Escape", async ({ page }) => {
        await page.goto("/");
        await page.getByTestId("command-palette-trigger").click();
        await expect(page.getByTestId("command-palette")).toBeVisible();
        // Focus the input first so it receives the Escape key
        await page.getByTestId("command-palette-input").focus();
        await page.keyboard.press("Escape");
        await expect(page.getByTestId("command-palette")).not.toBeVisible();
    });

    test("command palette opens with Ctrl+K", async ({ page }) => {
        await page.goto("/");
        await page.getByTestId("top-bar").waitFor();
        // Dispatch on document so the window listener receives the bubbling event.
        await page.evaluate(() => {
            document.dispatchEvent(
                new KeyboardEvent("keydown", {
                    key: "k",
                    ctrlKey: true,
                    bubbles: true,
                }),
            );
        });
        await expect(page.getByTestId("command-palette")).toBeVisible();
    });

    test("command palette filters commands by search", async ({ page }) => {
        await page.goto("/");
        await page.getByTestId("command-palette-trigger").click();
        await page.getByTestId("command-palette-input").fill("redis");
        await expect(
            page.getByTestId("command-palette-item-redis"),
        ).toBeVisible();
        await expect(
            page.getByTestId("command-palette-item-dashboard"),
        ).not.toBeVisible();
    });

    test("command palette opens a specific Settings section", async ({
        page,
    }) => {
        await page.goto("/");
        await page.getByTestId("command-palette-trigger").click();
        await page.getByTestId("command-palette-input").fill("AKS Settings");
        await expect(
            page.getByTestId("command-palette-item-settings-aks"),
        ).toBeVisible();
        await page.getByTestId("command-palette-item-settings-aks").click();
        await expect(page).toHaveURL(/\/settings$/);
        // Confirms the palette landed on the AKS sub-section specifically, not just Settings' default.
        await expect(
            page.getByRole("heading", { name: "AKS / Kubernetes" }),
        ).toBeVisible();
    });

    test("notification bell docks in the sidebar and does not overlap pinned items", async ({
        page,
    }) => {
        await page.goto("/");
        // Pin a resource so the rail occupies the corner the bell used to overlay.
        await expect(page.getByTestId("service-cards")).toBeVisible();
        await page.getByTestId("pin-resource-redis").click();
        const pinnedItem = page
            .locator("[data-testid^='pinned-rail-item-']")
            .last();
        await expect(pinnedItem).toBeVisible();

        // The bell must render inside the sidebar slot — the old fixed bottom-left
        // overlay is what covered the last pinned item.
        const slot = page.getByTestId("notification-bell-slot");
        const bell = slot.getByTestId("notification-bell");
        await expect(bell).toBeVisible();
        expect(
            await bell.evaluate((el) => getComputedStyle(el).position),
        ).not.toBe("fixed");

        const bellBox = await bell.boundingBox();
        const itemBox = await pinnedItem.boundingBox();
        expect(bellBox).not.toBeNull();
        expect(itemBox).not.toBeNull();
        const overlap =
            bellBox!.x < itemBox!.x + itemBox!.width &&
            bellBox!.x + bellBox!.width > itemBox!.x &&
            bellBox!.y < itemBox!.y + itemBox!.height &&
            bellBox!.y + bellBox!.height > itemBox!.y;
        expect(overlap).toBe(false);

        // The history popover still opens from its docked position.
        await bell.click();
        await expect(page.getByTestId("notification-history")).toBeVisible();

        // Leave the profile as we found it.
        await page.getByTestId("pin-resource-redis").click();
    });
});
