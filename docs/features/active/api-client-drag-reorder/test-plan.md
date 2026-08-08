# API Client Collection / Request Drag-and-Drop Reorder — Test Plan

## Unit / integration tests (`web`)

- `collection-tree-utils.ts`:
  - `moveCollection` places a collection at the requested index.
  - `moveCollection` keeps the demo collection at index `0` when it exists.
  - `moveNode` moves a request from root into a folder at the requested index.
  - `moveNode` moves a request out of a folder to the collection root.
  - `moveNode` prevents dropping a folder into its own descendant.
  - `resolveDropTarget` returns `targetIndex` and `targetParentId` correctly for before/after/inside regions.

## End-to-end tests (`web/e2e/api-client.spec.ts`)

| Scenario | Steps | Expected |
|----------|-------|----------|
| Reorder requests via drag | Create collection with `Request A` and `Request B`. Drag `Request B` above `Request A`. | Tree shows `Request B` then `Request A`; persisted order matches after reload. |
| Move request into folder | Create collection with folder `F` and request `R` outside it. Drag `R` onto the middle of `F`. | `R` appears under `F`; folder expansion shows it. |
| Reorder collections via drag | Create two collections `C1` and `C2`. Drag `C2` above `C1`. | Tree shows `C2` then `C1`. |
| Keyboard reorder | Focus a request and press `Alt+ArrowDown`. | Request swaps with the one below it. |
| Demo collection is immovable | Enable demo mode, focus demo collection root, try `Alt+ArrowDown` and drag. | Nothing moves; demo remains first. |
| Search disables drag | Filter tree so only one request matches, try to drag it. | No drag handles visible, `draggable` is false. |

## Accessibility

- Drag handle has `aria-label="Drag to reorder"`.
- Focused row announces move actions via `aria-live` notifications or `useNotification`.
- `Alt+ArrowUp`/`Alt+ArrowDown` works on keyboard-focused rows.
- Drop targets have visible 2px primary-colour indicator lines.

## Demo mode

- Demo collection cannot be dragged or dropped onto.
- Reordering non-demo collections persists correctly after toggling demo mode.

## Backwards compatibility

- Existing `collections.json` without explicit `Order` fields continues to work; order is the JSON array order.
- No changes to `ApiClientModels.cs` required.
