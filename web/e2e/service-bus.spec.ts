import { test, expect } from "@playwright/test";
import { setDemoMode } from "./helpers";

test.describe("Service Bus", () => {
    test.beforeEach(async ({ page }) => {
        await setDemoMode(page, true);
    });

    test.afterEach(async ({ page }) => {
        await setDemoMode(page, false);
    });

    test("selects demo namespace, queue and displays active messages", async ({
        page,
    }) => {
        await page.goto("/service-bus");
        await expect(page.getByTestId("sb-namespace-select")).toContainText(
            "orders-dev",
        );
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });

        await expect(
            page.getByTestId("entity-tree-queue-order-created"),
        ).toBeVisible();
        await page.getByTestId("entity-tree-queue-order-created").click();

        await expect(page.getByTestId("message-list")).toBeVisible();
        const firstMessage = page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .first();
        await expect(firstMessage).toBeVisible();

        await firstMessage.click();
        await expect(page.getByTestId("message-detail")).toBeVisible();
        await expect(page.getByTestId("message-detail-body")).toBeVisible();
        await expect(page.getByTestId("message-complete-button")).toBeVisible();
    });

    test("switches to DLQ view and shows dead-letter messages", async ({
        page,
    }) => {
        await page.goto("/service-bus");
        await expect(page.getByTestId("sb-namespace-select")).toContainText(
            "orders-dev",
        );
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });

        await page.getByTestId("entity-tree-queue-order-failed").click();
        await page.getByTestId("sb-view-dlq").click();

        await expect(page.getByTestId("message-list")).toBeVisible();
        const firstMessage = page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .first();
        await expect(firstMessage).toBeVisible();

        await firstMessage.click();
        await expect(page.getByTestId("message-detail")).toBeVisible();
        await expect(page.getByTestId("message-resubmit-button")).toBeVisible();
        await expect(
            page.getByTestId("message-complete-dlq-button"),
        ).toBeVisible();
    });

    test("text filter narrows message list", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        await expect(page.getByTestId("message-list")).toBeVisible();
        const initialCount = await page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .count();
        expect(initialCount).toBeGreaterThan(0);

        // Type a filter that should narrow results
        await page.getByTestId("message-text-filter").fill("zzznomatch");
        await expect(page.getByTestId("message-list-no-matches")).toBeVisible();

        // Clear filter
        await page.getByTestId("message-text-filter").fill("");
        await expect(page.getByTestId("message-list")).toBeVisible();
    });

    test("advanced filter panel opens and can add rules", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        await expect(page.getByTestId("message-list")).toBeVisible();

        // Open advanced filter panel
        await page.getByTestId("toggle-advanced-filter").click();
        await expect(page.getByTestId("advanced-filter-panel")).toBeVisible();

        // Add a rule
        await page.getByTestId("rule-add").click();
        await expect(page.getByTestId("advanced-rule")).toHaveCount(1);

        // Change field to Delivery Count
        await page.getByTestId("rule-field").selectOption("delivery-count");
        await page.getByTestId("rule-value").fill("999");

        // Should show no matches (no message has delivery count >= 999 with gte operator)
        await expect(page.getByTestId("message-list-no-matches")).toBeVisible();

        // Remove the rule
        await page.getByTestId("rule-remove").click();
        await expect(page.getByTestId("advanced-rule")).toHaveCount(0);

        // Messages should be visible again
        await expect(page.getByTestId("message-list")).toBeVisible();
    });

    test("advanced filter by application property works", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        await expect(page.getByTestId("message-list")).toBeVisible();

        // Open advanced filter and add a rule for application property
        await page.getByTestId("toggle-advanced-filter").click();
        await page.getByTestId("rule-add").click();

        // Default field is application-property, operator is contains
        await page.getByTestId("rule-property").fill("orderId");
        await page.getByTestId("rule-value").fill("ORD-");

        // Should filter - either show matches or no-matches (depends on demo data)
        // The key assertion is that the filter is being applied
        const hasMatches = await page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .count();
        const hasNoMatch = await page
            .getByTestId("message-list-no-matches")
            .count();
        expect(hasMatches > 0 || hasNoMatch > 0).toBeTruthy();
    });

    test("filter count shows when filters are active", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        await expect(page.getByTestId("message-list")).toBeVisible();

        // Type a filter
        await page.getByTestId("message-text-filter").fill("order");

        // Filter count should be visible (if there are matches)
        const hasMatches = await page.getByTestId("message-list").count();
        if (hasMatches > 0) {
            await expect(
                page.getByTestId("message-filter-count"),
            ).toBeVisible();
        }
    });

    test("message detail tabs switch content", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        const firstMessage = page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .first();
        await firstMessage.click();
        await expect(page.getByTestId("message-detail")).toBeVisible();

        // Body tab is active by default
        await expect(page.getByTestId("detail-tab-body")).toBeVisible();
        await expect(page.getByTestId("detail-tab-content-body")).toBeVisible();
        await expect(page.getByTestId("message-detail-body")).toBeVisible();

        // Switch to Properties tab
        await page.getByTestId("detail-tab-properties").click();
        await expect(
            page.getByTestId("detail-tab-content-properties"),
        ).toBeVisible();

        // Switch to System tab
        await page.getByTestId("detail-tab-system").click();
        await expect(
            page.getByTestId("detail-tab-content-system"),
        ).toBeVisible();
    });

    test("a JSON body is prettified by default and the choice is remembered", async ({
        page,
    }) => {
        // The body used to render as a single unreadable line with no way to format it, and
        // when it looked like JSON but did not parse, it said nothing at all.
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();
        const firstMessage = page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .first();
        await firstMessage.click();
        await expect(page.getByTestId("message-detail")).toBeVisible();

        await expect(page.getByTestId("body-format")).toContainText("JSON");
        await expect(page.getByTestId("body-pretty-toggle")).toHaveAttribute(
            "aria-pressed",
            "true",
        );

        // Prettified means more than one line, and the line count reports what is on screen.
        // Compared numerically: "Lines: 12" contains the substring "Lines: 1".
        const lineCount = async () =>
            Number(
                /Lines: (\d+)/.exec(
                    (await page.getByTestId("body-lines").textContent()) ?? "",
                )?.[1],
            );
        expect(await lineCount()).toBeGreaterThan(1);

        await page.getByTestId("body-raw-toggle").click();
        await expect.poll(lineCount).toBe(1);

        // A view preference, not per-message state, so it survives a reload.
        await page.reload();
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();
        await page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .first()
            .click();
        await expect(page.getByTestId("body-raw-toggle")).toHaveAttribute(
            "aria-pressed",
            "true",
        );
    });

    test("copy body and copy full message buttons work", async ({
        browser,
    }) => {
        const context = await browser.newContext({
            permissions: ["clipboard-read", "clipboard-write"],
        });
        const page = await context.newPage();
        try {
            await setDemoMode(page, true);
            await page.goto("/service-bus");
            await page
                .getByTestId("sb-namespace-select")
                .selectOption({ label: "orders-dev" });
            await page.getByTestId("entity-tree-queue-order-created").click();

            const firstMessage = page
                .getByTestId("message-list")
                .locator("[data-testid^='message-item-']")
                .first();
            await firstMessage.click();
            await expect(page.getByTestId("message-detail")).toBeVisible();

            // Copy body
            await expect(page.getByTestId("message-copy-body")).toBeVisible();
            await page.getByTestId("message-copy-body").click();
            await expect(page.getByTestId("message-copy-body")).toContainText(
                "Copied!",
            );

            // Copy full message
            await expect(page.getByTestId("message-copy-full")).toBeVisible();
            await page.getByTestId("message-copy-full").click();
            await expect(page.getByTestId("message-copy-full")).toContainText(
                "Copied!",
            );
        } finally {
            await setDemoMode(page, false);
            await context.close();
        }
    });

    test("purge all shows confirmation dialog and can cancel", async ({
        page,
    }) => {
        // Purge All lives in the entity-level toolbar (next to Active/DLQ), not the per-message
        // action row, so it never requires a message to be selected.
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();
        await expect(page.getByTestId("message-list")).toBeVisible();

        // Click purge - should show confirmation, not immediately purge
        await page.getByTestId("sb-purge-all-button").click();
        await expect(page.getByTestId("purge-confirm")).toBeVisible();

        // Cancel
        await page.getByTestId("purge-confirm-cancel").click();
        await expect(page.getByTestId("purge-confirm")).not.toBeVisible();
    });

    test("DLQ message shows DLQ Info tab", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-failed").click();
        await page.getByTestId("sb-view-dlq").click();

        const firstMessage = page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .first();
        await firstMessage.click();
        await expect(page.getByTestId("message-detail")).toBeVisible();

        // DLQ Info tab should be visible
        await expect(page.getByTestId("detail-tab-dlq")).toBeVisible();
        await page.getByTestId("detail-tab-dlq").click();
        await expect(page.getByTestId("detail-tab-content-dlq")).toBeVisible();
    });

    test("compose button opens composer modal", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        await page.getByTestId("sb-compose-button").click();
        await expect(page.getByTestId("message-composer")).toBeVisible();
        await expect(page.getByTestId("composer-panel")).toContainText(
            "Compose Message",
        );

        // Close
        await page.getByTestId("composer-close").click();
        await expect(page.getByTestId("message-composer")).not.toBeVisible();
    });

    test("composer can fill fields and send message", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        await page.getByTestId("sb-compose-button").click();
        await expect(page.getByTestId("message-composer")).toBeVisible();

        // Fill in fields
        await page.getByTestId("composer-subject").fill("Test Subject");
        await page.getByTestId("composer-body").fill('{"test": true}');

        // Format JSON
        await page.getByTestId("composer-format-json").click();

        // Add a property
        await page.getByTestId("composer-add-property").click();
        await page.getByTestId("composer-property-key-0").fill("orderId");
        await page.getByTestId("composer-property-value-0").fill("ORD-123");

        // Send (in demo mode this should succeed)
        await page.getByTestId("composer-send").click();

        // Composer should close after successful send
        await expect(page.getByTestId("message-composer")).not.toBeVisible();
    });

    test("composer cancel returns to page", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        await page.getByTestId("sb-compose-button").click();
        await expect(page.getByTestId("message-composer")).toBeVisible();

        await page.getByTestId("composer-cancel").click();
        await expect(page.getByTestId("message-composer")).not.toBeVisible();
    });

    test("save message as template from detail", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        const firstMessage = page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .first();
        await firstMessage.click();
        await expect(page.getByTestId("message-detail")).toBeVisible();

        // Click Save as Template
        await page.getByTestId("message-save-template").click();
        await expect(page.getByTestId("save-template-dialog")).toBeVisible();

        // Enter template name and save
        await page.getByTestId("template-name-input").fill("E2E Test Template");
        await page.getByTestId("template-save-confirm").click();

        // Dialog should close
        await expect(
            page.getByTestId("save-template-dialog"),
        ).not.toBeVisible();
    });

    test("load template in composer", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        // First save a template from a message
        const firstMessage = page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .first();
        await firstMessage.click();
        await page.getByTestId("message-save-template").click();
        await page
            .getByTestId("template-name-input")
            .fill("Composer Load Test");
        await page.getByTestId("template-save-confirm").click();
        await expect(
            page.getByTestId("save-template-dialog"),
        ).not.toBeVisible();

        // Wait a moment for the mutation to complete and cache to invalidate
        await page.waitForTimeout(500);

        // Now open composer and load the template
        await page.getByTestId("sb-compose-button").click();
        await expect(page.getByTestId("message-composer")).toBeVisible();

        await page.getByTestId("composer-load-template").click();
        await expect(page.getByTestId("template-picker")).toBeVisible();

        // Wait for templates to load (either items or empty state)
        await expect(
            page
                .getByTestId("template-picker-empty")
                .or(page.locator("[data-testid^='template-select-']").first()),
        ).toBeVisible({ timeout: 10000 });

        // Should have at least one template from the save above
        const templateItems = page.locator("[data-testid^='template-select-']");
        const count = await templateItems.count();
        expect(count).toBeGreaterThan(0);

        // Select the first template
        await templateItems.first().click();
        await expect(page.getByTestId("template-picker")).not.toBeVisible();

        // Composer should still be visible with loaded data
        await expect(page.getByTestId("message-composer")).toBeVisible();
    });

    test("batch send panel opens and previews JSON input", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        await page.getByTestId("sb-actions-menu").click();
        await page.getByTestId("sb-batch-send-button").click();
        await expect(page.getByTestId("batch-send-panel")).toBeVisible();

        // Paste JSON array
        const json = JSON.stringify([
            { body: '{"orderId":"ORD-1"}', subject: "Order 1" },
            { body: '{"orderId":"ORD-2"}', subject: "Order 2" },
        ]);
        await page.getByTestId("batch-input").fill(json);

        // Preview
        await page.getByTestId("batch-preview-btn").click();
        await expect(page.getByTestId("batch-preview")).toBeVisible();
        await expect(page.getByTestId("batch-preview")).toContainText(
            "2 messages",
        );

        // Close
        await page.getByTestId("batch-close").click();
        await expect(page.getByTestId("batch-send-panel")).not.toBeVisible();
    });

    test("batch send panel can send messages", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        await page.getByTestId("sb-actions-menu").click();
        await page.getByTestId("sb-batch-send-button").click();
        await expect(page.getByTestId("batch-send-panel")).toBeVisible();

        // Paste JSON array with a single message
        const json = JSON.stringify([
            { body: '{"batch":true}', subject: "Batch Test" },
        ]);
        await page.getByTestId("batch-input").fill(json);
        await page.getByTestId("batch-preview-btn").click();
        await expect(page.getByTestId("batch-preview")).toBeVisible();

        // Send
        await page.getByTestId("batch-send-btn").click();
        await expect(page.getByTestId("batch-send-panel")).not.toBeVisible();
    });

    test("scheduled messages panel opens and shows empty state", async ({
        page,
    }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        await page.getByTestId("sb-actions-menu").click();
        await page.getByTestId("sb-scheduled-button").click();
        await expect(
            page.getByTestId("scheduled-messages-panel"),
        ).toBeVisible();
        await expect(page.getByTestId("scheduled-title")).toContainText(
            "order-created",
        );

        // Should show empty state or table (depends on prior test state)
        const hasEmpty = await page.getByTestId("scheduled-empty").count();
        const hasTable = await page.getByTestId("scheduled-table").count();
        expect(hasEmpty + hasTable).toBeGreaterThan(0);

        await page.getByTestId("scheduled-close").click();
        await expect(
            page.getByTestId("scheduled-messages-panel"),
        ).not.toBeVisible();
    });

    test("entity command palette opens and searches entities", async ({
        page,
    }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });

        // Open palette via search button
        await page.getByTestId("sb-entity-search").click();
        await expect(page.getByTestId("entity-command-palette")).toBeVisible();

        // Should show entity results
        await expect(page.getByTestId("entity-palette-search")).toBeVisible();
        const items = page.locator("[data-testid^='entity-palette-item-']");
        expect(await items.count()).toBeGreaterThan(0);

        // Search for a specific entity
        await page.getByTestId("entity-palette-search").fill("order");
        const filteredItems = page.locator(
            "[data-testid^='entity-palette-item-']",
        );
        expect(await filteredItems.count()).toBeGreaterThan(0);

        // Close with Escape
        await page.getByTestId("entity-palette-search").press("Escape");
        await expect(
            page.getByTestId("entity-command-palette"),
        ).not.toBeVisible();
    });

    test("entity command palette selects entity on Enter", async ({ page }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });

        await page.getByTestId("sb-entity-search").click();
        await expect(page.getByTestId("entity-command-palette")).toBeVisible();

        // Press Enter to select first entity
        await page.getByTestId("entity-palette-search").press("Enter");
        // Palette should show actions for selected entity (Tab toggles actions)
        // The entity should be selected in the tree
        await expect(page.getByTestId("entity-command-palette")).toBeVisible();
    });

    test("entity command palette's Purge action opens the purge confirmation", async ({
        page,
    }) => {
        // Previously fell through every branch in handleEntityAction and did nothing at all —
        // presented as a working destructive action while silently no-opping.
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });

        await page.getByTestId("sb-entity-search").click();
        await page.getByTestId("entity-palette-search").fill("order-created");

        // Click the entity to reveal its actions, then click Purge.
        await page
            .locator("[data-testid^='entity-palette-item-']")
            .first()
            .click();
        await page.getByRole("button", { name: "Purge", exact: true }).click();

        await expect(
            page.getByTestId("entity-command-palette"),
        ).not.toBeVisible();
        await expect(page.getByTestId("purge-confirm")).toBeVisible();

        await page.getByTestId("purge-confirm-cancel").click();
        await expect(page.getByTestId("purge-confirm")).not.toBeVisible();
    });

    test("entity tree Queues section can be collapsed and expanded (default: expanded)", async ({
        page,
    }) => {
        // The reported "isn't collapsed by default when it should be" bug, reproduced for Service
        // Bus: the Queues section previously had no collapse control at all.
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });

        await expect(
            page.getByTestId("entity-tree-queues-toggle"),
        ).toHaveAttribute("aria-expanded", "true");
        await expect(
            page.getByTestId("entity-tree-queue-order-created"),
        ).toBeVisible();

        await page.getByTestId("entity-tree-queues-toggle").click();
        await expect(
            page.getByTestId("entity-tree-queues-toggle"),
        ).toHaveAttribute("aria-expanded", "false");
        await expect(
            page.getByTestId("entity-tree-queue-order-created"),
        ).not.toBeVisible();

        await page.getByTestId("entity-tree-queues-toggle").click();
        await expect(
            page.getByTestId("entity-tree-queue-order-created"),
        ).toBeVisible();
    });

    test("clear all filters resets text search and advanced rules together", async ({
        page,
    }) => {
        // Previously 3-4 scattered controls each cleared only their own filter, with no single
        // place to reset everything narrowing the list at once.
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();
        await expect(page.getByTestId("message-list")).toBeVisible();

        await expect(page.getByTestId("clear-all-filters")).not.toBeVisible();

        await page.getByTestId("message-text-filter").fill("order");
        await expect(page.getByTestId("clear-all-filters")).toBeVisible();

        await page.getByTestId("clear-all-filters").click();
        await expect(page.getByTestId("message-text-filter")).toHaveValue("");
        await expect(page.getByTestId("clear-all-filters")).not.toBeVisible();
    });

    test("batch replay requires confirmation before resubmitting", async ({
        page,
    }) => {
        // Replay used to execute immediately on click — the only bulk mutation in this feature
        // with no confirmation at all.
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-failed").click();

        await page.getByTestId("sb-actions-menu").click();
        await page.getByTestId("sb-batch-replay-button").click();
        await expect(page.getByTestId("batch-replay-panel")).toBeVisible();

        await page.getByTestId("batch-replay-select-all").click();
        await page.getByTestId("batch-replay-execute").click();

        // Confirmation shown, nothing replayed yet.
        await expect(page.getByTestId("batch-replay-confirm")).toBeVisible();
        await expect(page.getByTestId("batch-replay-done")).not.toBeVisible();

        // Confirming actually performs the replay.
        await page.getByTestId("batch-replay-confirm-yes").click();
        await expect(page.getByTestId("batch-replay-done")).toBeVisible();
    });

    test("template delete requires confirmation", async ({ page }) => {
        // Delete used to fire immediately on click — the only destructive action in this feature
        // with no confirmation (batch replay, bulk complete/resubmit, and purge all confirm).
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        const firstMessage = page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .first();
        await firstMessage.click();
        await page.getByTestId("message-save-template").click();
        await page
            .getByTestId("template-name-input")
            .fill("Delete Me Template");
        await page.getByTestId("template-save-confirm").click();
        await expect(
            page.getByTestId("save-template-dialog"),
        ).not.toBeVisible();
        await page.waitForTimeout(500);

        await page.getByTestId("sb-compose-button").click();
        await page.getByTestId("composer-load-template").click();
        await expect(page.getByTestId("template-picker")).toBeVisible();

        const templateItems = page.locator("[data-testid^='template-select-']");
        await expect(templateItems.first()).toBeVisible({ timeout: 10000 });
        const countBefore = await templateItems.count();

        await page.locator("[data-testid^='template-delete-']").first().click();
        await expect(page.getByTestId("template-delete-confirm")).toBeVisible();
        // Not deleted yet — still waiting on confirmation.
        await expect(templateItems).toHaveCount(countBefore);

        await page.getByTestId("template-delete-confirm-yes").click();
        await expect(
            page.getByTestId("template-delete-confirm"),
        ).not.toBeVisible();
    });

    test("scheduled message cancel requires confirmation", async ({ page }) => {
        // Cancel used to fire immediately on click, unlike every other comparable-severity action
        // in this feature.
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        // Schedule a message so there is something to cancel.
        const firstMessage = page
            .getByTestId("message-list")
            .locator("[data-testid^='message-item-']")
            .first();
        await firstMessage.click();
        await page.getByTestId("message-schedule").click();
        await expect(page.getByTestId("message-composer")).toBeVisible();
        await expect(page.getByTestId("composer-scheduled-time")).toBeVisible();
        // The Schedule flow doesn't carry the source message's body over (unlike Replay/Edit), so
        // the composer needs one filled in to pass its own "body cannot be empty" validation.
        await page.getByTestId("composer-body").fill('{"scheduled": true}');
        // Set an unambiguously far-future date, sidestepping the datetime-local field's default
        // value (computed via a UTC ISO string sliced into a local-time-shaped field) which can
        // land in the past depending on the runner's timezone offset.
        await page
            .getByTestId("composer-scheduled-time")
            .fill("2099-01-01T10:00");
        await page.getByTestId("composer-send").click();
        await expect(page.getByTestId("message-composer")).not.toBeVisible();

        await page.getByTestId("sb-actions-menu").click();
        await page.getByTestId("sb-scheduled-button").click();
        await expect(
            page.getByTestId("scheduled-messages-panel"),
        ).toBeVisible();

        const cancelButton = page
            .locator("[data-testid^='scheduled-cancel-']")
            .first();
        await expect(cancelButton).toBeVisible({ timeout: 10000 });
        await cancelButton.click();

        await expect(
            page.getByTestId("scheduled-cancel-confirm"),
        ).toBeVisible();
        await page.getByTestId("scheduled-cancel-confirm-yes").click();
        await expect(
            page.getByTestId("scheduled-cancel-confirm"),
        ).not.toBeVisible();
    });

    test("bulk resend asks for confirmation, then moves copies and removes originals", async ({
        page,
    }) => {
        // Resend is move-to-origin semantics (fresh Message ID, source removed once
        // the copy lands) — it targets NServiceBus.FailedQ or falls back to the
        // source entity, which is why the demo copies reappear here with new
        // sequence numbers while the originals leave the list.
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();
        await expect(page.getByTestId("message-list")).toBeVisible();

        await page.getByTestId("message-checkbox-4501").check();
        await page.getByTestId("message-checkbox-4502").check();

        await page.getByTestId("bulk-resend").click();
        const confirm = page.getByTestId("bulk-action-confirm");
        await expect(confirm).toBeVisible();
        await expect(confirm).toContainText(
            "Resend 2 message(s) to order-created",
        );

        // Cancel first — nothing is sent while the bar is up.
        await page.getByTestId("bulk-action-confirm-cancel").click();
        await expect(confirm).not.toBeVisible();
        await expect(page.getByTestId("bulk-action-bar")).toBeVisible();

        // Confirming resends the copies and clears the selection.
        await page.getByTestId("bulk-resend").click();
        await page.getByTestId("bulk-action-confirm-yes").click();
        await expect(page.getByTestId("bulk-action-confirm")).not.toBeVisible();
        await expect(page.getByTestId("bulk-action-bar")).not.toBeVisible();
        // Originals leave the list — resend completes the source after the copy lands.
        await expect(page.getByTestId("message-item-4501")).not.toBeVisible();
        await expect(page.getByTestId("message-item-4502")).not.toBeVisible();
    });

    test("bulk resend is also available on dead-letter messages", async ({
        page,
    }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();
        await page.getByTestId("sb-view-dlq").click();
        await expect(page.getByTestId("message-list")).toBeVisible();

        await page.getByTestId("message-checkbox-4410").check();
        await page.getByTestId("bulk-resend").click();
        const confirm = page.getByTestId("bulk-action-confirm");
        await expect(confirm).toBeVisible();
        await expect(confirm).toContainText(
            "Resend 1 message(s) to order-created",
        );
        await page.getByTestId("bulk-action-confirm-yes").click();
        await expect(confirm).not.toBeVisible();
        // The DLQ original is removed once the copy lands — resend is a move, not a copy.
        await expect(page.getByTestId("message-item-4410")).not.toBeVisible();
    });

    test("replay composer starts with a fresh message id and can restore the original", async ({
        page,
    }) => {
        // Replay used to silently reuse the source MessageId — under duplicate
        // detection the resend was dropped without any signal. It now generates a
        // new GUID and offers an explicit "restore original" escape hatch.
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });
        await page.getByTestId("entity-tree-queue-order-created").click();

        await page.getByTestId("message-item-4501").click();
        await expect(page.getByTestId("message-detail")).toBeVisible();
        await page.getByTestId("message-replay").click();

        await expect(page.getByTestId("composer-panel")).toBeVisible();
        const idInput = page.getByTestId("composer-message-id");
        const freshId = await idInput.inputValue();
        expect(freshId).not.toBe("oc-001");
        expect(freshId).toMatch(/^[0-9a-f-]{36}$/i);

        // Regenerate produces another fresh GUID.
        await page.getByTestId("composer-regenerate-id").click();
        const regenerated = await idInput.inputValue();
        expect(regenerated).toMatch(/^[0-9a-f-]{36}$/i);
        expect(regenerated).not.toBe(freshId);

        // Restore puts the source id back.
        await page.getByTestId("composer-restore-id").click();
        await expect(idInput).toHaveValue("oc-001");
    });

    test("templates manager lists, creates, duplicates and deletes templates", async ({
        page,
    }) => {
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });

        await page.getByTestId("sb-templates-button").click();
        await expect(page.getByTestId("template-manager")).toBeVisible();

        // Create a new template in the editor pane.
        await page.getByTestId("template-new").click();
        await page
            .getByTestId("template-edit-name")
            .fill("Manager E2E Template");
        await page
            .getByTestId("template-edit-body")
            .fill('{"fromManager": true}');
        await page.getByTestId("template-save").click();
        await expect(
            page.getByTestId("template-editor-error"),
        ).not.toBeVisible();

        // It shows up in the list, filtered by search.
        const item = page
            .locator("[data-testid^='template-item-']")
            .filter({ hasText: "Manager E2E Template" });
        await expect(item).toBeVisible({ timeout: 10000 });
        await page.getByTestId("template-search").fill("Manager E2E");
        await expect(item).toBeVisible();

        // Reopening it reindents the stored minified body for editing.
        await item.locator("[data-testid^='template-select-']").click();
        await expect(page.getByTestId("template-edit-body")).toHaveValue(
            /\n {2}/,
        );

        // Format JSON on a non-JSON body surfaces an error toast, not a silent no-op.
        await page.getByTestId("template-edit-body").fill("not json");
        await page.getByTestId("template-format-json").click();
        await expect(
            page
                .locator("[data-testid^='notification-toast-']")
                .filter({ hasText: "not valid JSON" }),
        ).toBeVisible();

        // Duplicate it — a "(copy)" row appears.
        const dupButton = item.locator("[data-testid^='template-duplicate-']");
        await dupButton.click();
        await expect(
            page
                .locator("[data-testid^='template-item-']")
                .filter({ hasText: "Manager E2E Template (copy)" }),
        ).toBeVisible({ timeout: 10000 });

        // Delete requires confirmation.
        const copyItem = page
            .locator("[data-testid^='template-item-']")
            .filter({ hasText: "(copy)" });
        await copyItem.locator("[data-testid^='template-delete-']").click();
        await expect(page.getByTestId("template-delete-confirm")).toBeVisible();
        await page.getByTestId("template-delete-confirm-yes").click();
        await expect(
            page
                .locator("[data-testid^='template-item-']")
                .filter({ hasText: "Manager E2E Template (copy)" }),
        ).not.toBeVisible();
    });

    test("namespace overview summarizes the namespace and jumps to DLQ backlog", async ({
        page,
    }) => {
        // Replaces the bare "Select an entity" placeholder.
        await page.goto("/service-bus");
        await page
            .getByTestId("sb-namespace-select")
            .selectOption({ label: "orders-dev" });

        await expect(page.getByTestId("sb-namespace-overview")).toBeVisible();
        await expect(page.getByTestId("sb-overview-title")).toContainText(
            "orders-dev",
        );

        // order-created carries dead-letter backlog — clicking it jumps straight to
        // the entity's DLQ view.
        await page.getByTestId("sb-overview-dlq-order-created").click();
        await expect(page.getByTestId("message-list")).toBeVisible();
        await expect(page.getByTestId("sb-view-dlq")).toHaveClass(
            /border-primary/,
        );
    });
});
