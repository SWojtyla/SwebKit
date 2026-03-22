# Service Bus UI Revamp

## Goal

Make the message table the dominant element on the Service Bus page. The entity tree (left panel) and message detail pane (right panel) should both be collapsible/slide-able so that the message list can expand to fill all available horizontal space. The result should feel like a modern developer tool: dense, information-rich, and keyboard-friendly.

## Value

- Power users working large queues can see 30–50% more messages at once without horizontal scrolling.
- Fewer columns are hidden or truncated when the detail pane is closed.
- The detail pane can overlay or dock based on window width, so the experience degrades gracefully on narrower developer screens.
- Row density control lets users choose between data density and readability.

## Scope

Layout restructuring only. Specifically:

- `ServiceBusPage.razor` — outer shell layout, workspace container, splitter wiring.
- `EntityTree.razor` — collapsed icon-strip mode (CSS width transition, collapsed prop).
- `MessageDetailPane.razor` — converted from always-visible fixed-width side panel to a slide-in drawer.
- `MessageListView.razor` — additional column visibility when detail pane is closed; density toggle.
- `app.css` — new layout tokens and CSS classes.
- Optional new JS: localStorage helpers for panel state persistence.

**No changes to:**

- Business logic, message operations, or error handling.
- `IServiceBusClient`, `SbMessage`, data models, or domain services.
- `MessageComposer`, `DlqView` (DLQ splitter is a separate, lower-priority sub-task).
- `ScheduledMessages` view.
- Any backend/Azure integration code.

## Non-Goals

- New protocol support or new message operations.
- Adding new message fields to the domain model.
- Virtualization improvements (already present via `Virtualize="true"`).
- Touch/mobile layout (this is a desktop developer tool).

## Key Design Decisions to Make

### 1. Entity tree collapse mode: icon-strip vs full hide?

**Options:**
- **Icon-strip (~48px):** Collapsed left pane shows namespace initial letter badges and entity type icons. Clicking an icon opens the full panel. Mirrors the pattern already used by the main left-nav (which collapses from 240px to 56px via `.nav-collapsed`).
- **Full hide (0px):** Panel disappears completely. Simplest implementation, but loses the visual affordance that namespaces are loaded.

**Recommendation:** Icon-strip. The existing `service-bus-page-shell.left-pane-collapsed` already collapses to 44px — extend this with actual icon content rather than leaving it blank.

### 2. Detail pane: slide-over drawer vs push layout vs bottom split?

**Options:**
- **Slide-over (overlay):** Detail pane slides in above the message table with a semi-transparent gap on the left; the table itself does not reflow. Best for narrow screens where push would squeeze the table too much.
- **Push (docked):** Detail pane pushes the message table left; table shrinks. Matches the current always-visible behavior but now toggled. Best for wide screens where the table can afford 340px loss.
- **Bottom split:** Detail pane appears below the message table. Maximizes column count but reduces visible row count.

**Recommendation:** Dual-mode responsive: push on wide (>=1400px), overlay on narrow (<1400px). Mode is determined at open time based on `window.innerWidth` via a JS interop call.

### 3. Column visibility: which new columns to expose with extra space?

Currently all columns are always rendered (`MessageId 220px`, `CorrelationId 180px`, `Subject flex`, `Delivery 72px`). With the detail pane closed, the table gains ~345px (340px pane + 5px splitter). With the entity panel collapsed it gains another ~215px.

**Proposed additional columns when detail pane is closed:**
- `ExpiresAt` (from `SbMessage.ExpiresAt` if exposed) — 130px
- `ContentType` — 110px
- `SessionId` — 130px

These columns are already shown in the detail pane System tab, so they are already in the model. Adding them to the grid reduces need to open the detail pane for quick inspection.

### 4. Tab bar: keep multi-tab model or simplify?

The current tab bar is already scrollable (`overflow-x: auto; scrollbar-width: none`). The main improvement needed is reducing tab item padding from `6px 12px` to `4px 10px` so more tabs fit before overflow. No architectural change required.

## Dependencies

- `src/SwebKit.App/Components/Aks/ResizablePanel.razor` — exists and can be referenced or extended for the push-mode drag handle on the detail pane.
- `SwebKitSplitter` JS interop — currently used by `ServiceBusPage.razor` and `DlqView.razor`. The revamp replaces this with the Blazor-native `ResizablePanel` approach (no JS required for drag).
- `wwwroot/js/` — may need a small localStorage helper module if not already present.

## Risks

- **CSS regression on DLQ view:** `DlqView.razor` has its own `pane-splitter` + `details-pane` + `SwebKitSplitter.init` call. The revamp targets the active-message workspace only. DLQ layout changes are a separate task.
- **`SwebKitSplitter` JS interop disposal:** `ServiceBusPage.razor` currently initializes a `_splitterHandle` in `OnAfterRenderAsync` (lines 261–273) and disposes it in `SetActive` (lines 511–518) and `Dispose` (lines 553–556). Replacing the JS splitter with `ResizablePanel` requires removing this JS interop entirely and cleaning up the `_splitterRef` / `_detailPaneRef` `ElementReference` fields.
- **FluentDataGrid column width recalculation:** `ResizableColumns="true"` on the `FluentDataGrid` in `MessageListView.razor` (line 129) manages column widths internally. Adding/removing columns at runtime may cause the grid to not re-measure correctly. Test with column add/remove triggered by a parameter change rather than a CSS class toggle.
- **`window.innerWidth` breakpoint**: The overlay vs push decision made at pane-open time will not re-evaluate if the user resizes the window while the pane is open. This is acceptable for a desktop MAUI app where window resizing is rare mid-session.

## Quick Links

- [Frontend technical plan](frontend.md)
- [Status](status.md)
- Architecture reference: `docs/architecture/functionalities/service-bus.md`
