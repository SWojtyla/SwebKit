# Storage detail panel overflow fix — technical plan

## Root cause
The detail panel uses `flex-1 overflow-auto` so it scrolls when its children overflow. Long blob names, metadata values, version IDs, and raw content in `<pre>` tags are not constrained, causing the panel to grow wider than its container and show a horizontal scrollbar.

## Changes to `web/src/components/storage/StoragePage.tsx`

1. **Blob name header**
   - Change `<div className="text-lg font-mono font-semibold">` to include `break-all`.

2. **Properties metadata table**
   - Add `table-fixed` to the metadata table.
   - Set key cell to `w-1/3` with `break-all`.
   - Set value cell to `break-all`.

3. **Versions table**
   - Add `table-fixed`.
   - Use `break-all` on version ID cells and `truncate`/`break-all` on action cells.

4. **Content `<pre>`**
   - Replace `overflow-auto` with `whitespace-pre-wrap break-words` and keep vertical scrolling only with `overflow-y-auto`.

5. **Version diff `<pre>`**
   - Add `whitespace-pre-wrap break-words`.

6. **SAS URL / copy URL inputs**
   - Already use `flex-1` and the input truncates; no change needed.

## Verification

- `npm run build`
- `cd web && npx playwright test e2e/storage*.spec.ts`
