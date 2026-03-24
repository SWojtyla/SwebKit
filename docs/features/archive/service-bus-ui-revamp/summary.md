# Archive Summary — Service Bus UI Revamp

---

title: "Archive Summary — Service Bus UI Revamp"
owner: ""
completed_date: "2026-03-24"

---

## Goal

Make the message table the dominant element on the Service Bus page by converting the entity tree and detail pane into collapsible/sliding panels, so the message list can expand to fill all available horizontal space.

## Delivered

- Replaced the fixed left-pane / JS-splitter / always-visible detail-pane layout with a flexible three-zone model: collapsible entity panel, flex-1 message list, and conditional detail drawer.
- **Entity panel** collapses from 260 px to a 48 px icon-strip showing namespace initial badges; state persists to localStorage.
- **Detail drawer** opens on message selection in push mode (wide windows) or overlay mode (< 1400 px), using `ResizablePanel` for push and `position: absolute` for overlay. Slide-in animation via CSS keyframes.
- **Density toggle** (compact 28 px / default 36 px / comfort 44 px row heights) on the message list filter bar, persisted to localStorage.
- **Extra columns** (ContentType, SessionId) appear in the message grid when the detail pane is closed, giving visibility to metadata that was previously hidden.
- Removed `SwebKitSplitter` JS interop from `ServiceBusPage.razor` (still used by `DlqView`).
- Added `uiState.js` with `SwebKitUi.getLocalStorage`, `setLocalStorage`, and `getWindowWidth` helpers.
- Updated `docs/architecture/functionalities/service-bus.md` to reflect the new layout.

## Key decisions

- **Dual-mode drawer (push + overlay):** Mode is chosen at open time via `window.innerWidth` threshold (1400 px). Push mode uses the existing `ResizablePanel` component from AKS; overlay avoids reflowing the message grid on narrow windows.
- **Icon-strip over full-hide:** Collapsed entity panel shows namespace badges instead of disappearing, preserving the affordance that namespaces are loaded.
- **Pure CSS + Blazor for resizing:** Replaced the JS-based `SwebKitSplitter` with the existing `ResizablePanel` Blazor component, reducing JS interop surface.
- **localStorage over server state:** Panel collapsed state and density preference use localStorage because they are per-device UI preferences with no cross-session value.

## Validation performed

- Build passes (exit code 0) on `net10.0-windows10.0.19041.0` Debug configuration.
- User confirmed visual layout is correct ("Look good").
- DLQ view retains its own splitter and is unaffected.

## Lessons learned

- Escaped double quotes (`\"`) inside Razor `@onclick` attributes cause parse errors. Use single-quoted HTML attributes (`@onclick='...'`) when the C# expression inside contains string literals.
- Large multi-file layout changes benefit from a build check after each structural file edit, not only at the end.

## Follow-up

- **DLQ view splitter modernization:** `DlqView.razor` still uses the old `SwebKitSplitter` JS interop — a follow-up revamp could align it with the new drawer pattern.
- **ExpiresAt column:** The `SbMessage` domain model does not yet expose `ExpiresAt`; adding it would enable a fourth extra column in the message grid.
- **ResizablePanel CSS audit (task 17):** Confirm `.resizable-panel` / `.resize-handle` styles from AKS do not visually conflict in the Service Bus context.
- **Tab padding regression check (task 18):** Verify the `.tab-bar .tab-item` padding override does not affect AKS or other pages.
