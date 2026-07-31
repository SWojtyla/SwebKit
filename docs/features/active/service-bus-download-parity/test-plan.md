# Service Bus Download, Counts, and Parity — Test Plan

**Branch:** `devin/service-bus-download-parity` → `main`  
**Goal:** Verify single/bulk message download, accurate counts, load-more scrolling, and MAUI/Tauri parity in a running Vite + .NET sidecar dev environment.

## Preconditions

- Sidecar running on `http://127.0.0.1:5199` (`src-sidecar/Program.cs:19`).
- Vite dev server running on `http://localhost:1420` (`web/vite.config.ts:15`).
- Demo mode enabled via the top-right toggle (`title="Toggle demo mode"`, text `Live`; `web/src/components/layout/AppLayout.tsx`).
- Browser window maximized before recording.

## Evidence Sources

| Area | Files / Lines |
|---|---|
| Message counts / invalidation | `web/src/lib/hooks.ts` Service Bus mutations, `web/src/components/service-bus/ServiceBusPage.tsx`, `web/src/components/service-bus/MessageList.tsx` |
| Load more / scroll | `web/src/components/service-bus/ServiceBusPage.tsx`, `web/src/components/service-bus/MessageList.tsx` |
| Single download | `web/src/components/service-bus/MessageDetail.tsx`, `src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor` |
| Bulk ZIP download | `web/src/components/service-bus/MessageList.tsx`, `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`, `web/src/lib/download.ts`, `web/src/lib/zip.ts` |
| MAUI parity notes | `docs/features/active/service-bus-download-parity/technical-plan.md` parity table |

## Build / TypeScript

1. In `web/`: `npx tsc -b` and `npm run build`.
2. In `src-sidecar/`: `dotnet build`.
3. In `tests/SwebKit.Sidecar.Tests`: `dotnet test`.

**Pass criteria:** `tsc` exits 0, `npm run build` produces `dist/` with no errors, `dotnet build` reports `0 Error(s)`, `dotnet test` passes.

---

## Test Case 1: Message counts match across the page

**Steps:**

1. Open `http://localhost:1420/service-bus`.
2. Select namespace `orders-dev` from `sb-namespace-select`.
3. Wait for the entity tree to load.
4. Click a queue (e.g. `order-created`) and note the `activeMessageCount` shown in the entity tree.
5. Verify the `Active` tab count (`sb-view-active`) matches the entity-tree count.
6. Switch to the `DLQ` tab (`sb-view-dlq`) and verify the count matches the queue's `deadLetterMessageCount` in the tree.
7. Click the first message and click `message-complete-button` (or `message-purge-button` for an empty test).
8. After the operation completes, verify the `Active` tab count and the entity-tree count both decrease by the expected amount.
9. Check the footer `message-filter-count` shows `Showing X of Y message(s)` where `Y` equals the active/DLQ total.

**Pass criteria:**

- Entity-tree counts, tab counts, and footer counts are consistent.
- After a mutation, all three update without a manual refresh.

---

## Test Case 2: Load more and large-list scroll

**Steps:**

1. Open `http://localhost:1420/service-bus` and select `orders-dev`.
2. Open a queue whose active message count is greater than the default peek count (50).
3. Scroll the `message-list` container to the bottom.
4. Verify the `load-more-button` appears and is enabled when more messages exist.
5. Click `load-more-button`.
6. Verify `message-filter-count` changes from `Showing 50 of Y` to `Showing 100 of Y` (or the next peek-count window).
7. Scroll again and verify the sentinel auto-fetches the next page when it becomes visible.
8. Continue until `load-more-button` reads `All loaded`.

**Pass criteria:**

- Additional messages are appended, not replacing the existing list.
- The selected message (if any) is preserved while more pages load.
- No duplicate sequence numbers appear after loading more.

---

## Test Case 3: Download a single message

**Steps:**

1. Open `http://localhost:1420/service-bus`, select `orders-dev`, and open a queue.
2. Click a message row to open the detail pane.
3. Click `message-download-json`.
4. Verify a `.json` file is downloaded and contains `messageId`, `body`, `applicationProperties`, `sequenceNumber`, etc.
5. Click `message-download-zip`.
6. Verify a `.zip` file is downloaded and contains one `.json` entry matching the message.

**Pass criteria:**

- Both downloads trigger a success notification.
- The JSON content is formatted and contains the full message object.
- The ZIP can be opened and contains a single JSON file.

---

## Test Case 4: Download selected/filtered messages as ZIP

**Steps:**

1. Open `http://localhost:1420/service-bus`, select `orders-dev`, and open a queue.
2. Click `message-download-zip` in the message-list toolbar (no rows selected).
3. Verify a `.zip` is downloaded with one entry for each visible filtered message.
4. Select two rows via checkboxes and click `message-download-zip` again.
5. Verify the ZIP contains exactly two JSON files.
6. Type a text filter that narrows the list and click `message-download-zip`.
7. Verify the ZIP contains one entry for each filtered message.

**Pass criteria:**

- ZIP scope switches between selected and filtered based on selection state.
- File names are safe and unique.
- Download triggers a success notification.

---

## Test Case 5: MAUI/Blazor download parity (manual)

**Steps:**

1. Run the MAUI/Blazor app and navigate to Service Bus.
2. Open a queue, select a message, and click the new `JSON` / `ZIP` buttons in `MessageDetailPane`.
3. In `MessageListView`, click the new `ZIP` toolbar button.
4. Verify the same file contents and naming conventions as the Tauri/React implementation.

**Pass criteria:**

- Single message JSON and ZIP download work.
- Bulk ZIP of selected/filtered messages works.
- `SwebKit.downloadText` and `SwebKitUi.downloadBinaryFile` are invoked with correct arguments.

---

## Test Case 6: Regression — existing Service Bus e2e still pass

**Steps:**

1. In `web/`: `npx playwright test e2e/service-bus.spec.ts`.
2. In `web/`: `npx playwright test e2e/service-bus-url-state.spec.ts`.

**Pass criteria:**

- All Service Bus Playwright tests pass without modification.
- No `data-testid` values change.
