# Storage detail panel overflow fix — Test Plan

**Branch:** `devin/storage-detail-overflow` → `main`
**Goal:** Verify the Storage blob detail panel never scrolls horizontally, and that long blob names, metadata, version IDs, content, and diffs wrap within the panel width.

## Preconditions

- Sidecar running on `http://127.0.0.1:5199` (`src-sidecar/Program.cs`).
- Vite dev server running on `http://localhost:1420` (`web/vite.config.ts`).
- Demo mode enabled via the top-right toggle in `web/src/components/layout/AppLayout.tsx`.
- Browser window maximized.

## Evidence Sources

| Area | Files |
|---|---|
| Detail panel layout | `web/src/components/storage/StoragePage.tsx` |
| Existing e2e coverage | `web/e2e/storage.spec.ts`, `web/e2e/storage-deferred.spec.ts`, `web/e2e/storage-recovery.spec.ts` |

## Build / TypeScript

1. In `web/`: `npx tsc -b` and `npm run build`.
2. In `src-sidecar/`: `dotnet build`.
3. In `tests/SwebKit.Sidecar.Tests`: `dotnet test`.

**Pass criteria:** all commands exit 0 with no errors.

---

## Test Case 1: Long blob name wraps in the detail header

**Steps:**

1. Open `http://localhost:1420/storage`.
2. Select a container and open a blob whose path is long (nested prefixes).
3. Narrow the browser window so the detail panel is at its smallest usable width.

**Pass criteria:**

- The blob name wraps onto multiple lines (`break-all`) and stays inside the panel.
- No horizontal scrollbar appears on the detail panel.

---

## Test Case 2: Metadata table stays within panel width

**Steps:**

1. With a blob selected, open the Properties/Metadata tab.
2. Inspect a metadata entry with a long key and a long value (e.g. a URL or token-like value).

**Pass criteria:**

- The table is fixed-layout with a one-third key column.
- Long keys and values wrap; the table never exceeds the panel width.

---

## Test Case 3: Versions table and version IDs

**Steps:**

1. Open the Versions tab for a blob with multiple versions.
2. Inspect the version ID cells and the action buttons.

**Pass criteria:**

- Version IDs wrap instead of widening the table.
- Action buttons remain visible and clickable at narrow widths.

---

## Test Case 4: Content and diff panes wrap

**Steps:**

1. Open the Content tab for a blob containing a single very long line (e.g. minified JSON).
2. Open the version diff view for two versions with long lines.

**Pass criteria:**

- Content wraps (`whitespace-pre-wrap break-words`); only a vertical scrollbar is present.
- The diff pane wraps the same way, and the diff summary labels with long version IDs do not push content off-screen.

---

## Test Case 5: Regression — existing Storage e2e still pass

**Steps:**

1. In `web/`: `npx playwright test e2e/storage.spec.ts e2e/storage-deferred.spec.ts e2e/storage-recovery.spec.ts`.
2. In `web/`: `npx playwright test` (full suite).

**Pass criteria:**

- All Storage Playwright tests pass without modification.
- No `data-testid` values change.
