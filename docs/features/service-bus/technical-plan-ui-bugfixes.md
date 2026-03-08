# Technical Plan - Service Bus: UI Bug Fix Pack (2026-03)

## Status

- Plan state: In progress (SB-UI-BUG-01 complete; SB-UI-BUG-02 complete; SB-UI-BUG-03 complete; SB-UI-BUG-04 complete)
- Scope: UI-focused fixes for 4 reported Service Bus defects
- Product code changes: Not in this document (planning only)

## Goal

Address four high-impact usability and correctness issues in the Service Bus inspector UI:

1. DLQ message count mismatch and missing DLQ visual distinction.
2. Static table columns causing truncation and unnecessary horizontal scroll.
3. Horizontal scroll interaction blocked by fixed left panel footprint.
4. Topic labels rendering encoded/garbled characters (for example `&#9660; bundle-1`).

## Out of Scope

- Service Bus backend API redesign.
- New feature work unrelated to the four bugs.
- Non-Service Bus UI refinements.

## Sequencing

1. **Fix data/render correctness first (Bug 1, Bug 4)**
   - Ensures users trust list contents and labels before layout tuning.
2. **Apply table layout refactor (Bug 2)**
   - Introduce responsive column behavior and overflow policy.
3. **Resolve horizontal scroll ergonomics (Bug 3)**
   - Adjust panel/content layout once table behavior is stable.
4. **Run integrated validation across queue/topic/DLQ views**
   - Verify no regressions in tab interactions and selection state.

## Bug Breakdown

### SB-UI-BUG-01: DLQ count says 1483, only 3 rows render; no DLQ visual distinction

- Implementation status: Complete (2026-03-08)
- Validation note:
  - `SB-UI-101`: Passed (component test coverage added)
  - `SB-UI-102`: Passed (component test coverage added)
  - `SB-UI-103`: Pending manual smoke validation with large DLQ dataset
  - `SB-UI-104`: Passed (explicit mode-selection actions and dual-count visibility)

- Reported behavior:
  - Selecting an error queue indicates many DLQ messages (example: 1483), but only a few rows render.
  - DLQ content is not clearly distinguishable from normal message view.

- Root-cause hypotheses:
  - View model tracks total count separately from paged/limited payload and does not expose load-more or paging state.
  - Hard limit/default page size is applied in `MessageListView` without an explicit indicator.
  - Tab/header style token for DLQ mode is reused from normal peek mode, removing contextual differentiation.

- Likely impacted files/areas:
  - `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
  - `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
  - `src/SwebKit.App/Components/ServiceBus/ServiceBusPage.razor`
  - `src/SwebKit.Core/Models/SbMessage.cs` (only if a mode/tag field is needed)

- Implementation steps:
  - Make fetch mode explicit in UI state (`Peek` vs `DLQ`) and show it in list header/badge.
  - Surface pagination or load-window status (`showing X of Y`) near row count.
  - Align count source and rendered data source, or make intentional sampling explicit.
  - Add DLQ-only visual markers (tab accent, badge, or row treatment) consistent with current design language.

- Acceptance criteria:
  - DLQ view always communicates whether it is full, paged, or sampled.
  - When total count exceeds current render window, UI states `showing X of Y`.
  - DLQ tab/list has a clear visual distinction from normal queue/topic peek list.
  - No mismatch between label semantics and actual rendered subset.
  - Users can explicitly choose Active vs DLQ at entity-row level and see both counts simultaneously.

- Test coverage mapping:
  - `SB-UI-101`: Component test for count label + render window text.
  - `SB-UI-102`: Component test verifies DLQ mode badge/style class is present.
  - `SB-UI-103`: Manual smoke test with large DLQ (>1000) validates clarity and navigation.
  - `SB-UI-104`: Component tests verify entity-tree mode selection callback and dual count semantics.

### Extra Feature: Explicit Active vs DLQ Mode Selection in Entity Tree

- Status: Implemented (2026-03-08)
- Summary:
  - Queue and subscription rows now include explicit `Active <count>` and `DLQ <count>` actions.
  - Default row click behavior remains Active mode for quick access.
  - Mode-selection callback is wired into tab opening so DLQ and Active tabs are unambiguous.
- Documentation:
  - `docs/features/service-bus/MODE_SELECTION_ACTIVE_DLQ.md`

### SB-UI-BUG-02: Static columns truncate data and force unnecessary horizontal scroll

- Implementation status: Complete (final polish applied 2026-03-08)
- Delivered this pass:
  - Tuned key column bounds to reduce unnecessary horizontal overflow while preserving readability (message id, correlation id, subject, DLQ reason).
  - Kept utility columns compact (`Delivery`) and retained per-cell wrap/truncate behavior to avoid blanket clipping.
  - Preserved `message-grid-scroll` as the list scroll owner with table sizing policy (`min-width: 100%` + `width: max-content`) so scrollbar appears only when content truly exceeds space.
  - Added/updated component coverage for responsive class hooks including correlation-id column.

- Reported behavior:
  - Table columns are fixed and truncate useful values.
  - Horizontal scrollbar appears even when information could fit with responsive resizing.

- Root-cause hypotheses:
  - Table uses fixed pixel widths and `white-space: nowrap` across all columns.
  - Container and table min-width values exceed viewport by default.
  - Metadata-heavy columns are not prioritized or collapsible.

- Likely impacted files/areas:
  - `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
  - `src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor`
  - `src/SwebKit.App/wwwroot/css/` (Service Bus table styles)

- Implementation steps:
  - Introduce responsive width strategy: fixed width only for utility columns, flexible width for payload/meta columns.
  - Replace blanket truncation with per-column behavior (`wrap`, `truncate`, or expandable cell).
  - Remove unnecessary table min-width constraints and validate at common breakpoints.
  - Preserve readability with tooltip or expand-on-focus where truncation remains necessary.

- Acceptance criteria:
  - Horizontal scrollbar appears only when content genuinely cannot fit.
  - Key columns (message id, subject, enqueue time, delivery count) remain readable without excessive truncation.
  - Column behavior is consistent between normal and DLQ list modes.

- Test coverage mapping:
  - `SB-UI-201`: Component style regression test for responsive column class usage.
  - `SB-UI-202`: Manual viewport checks at 1280, 1024, and 768 widths.
  - `SB-UI-203`: Manual validation that table still supports keyboard navigation.

### SB-UI-BUG-03: Horizontal scrollbar cannot be used because left settings panel consumes space

- Implementation status: Complete (collapse-control pass 2026-03-08)
- Delivered this pass:
  - Added explicit namespace panel collapse/expand control in `ServiceBusPage` so users can quickly reclaim horizontal workspace.
  - Added class-based collapsed layout state (`left-pane-collapsed`) so left pane shrinks to a compact rail without taking content width.
  - Kept right pane and message list as explicit scroll owners (`service-bus-right-pane`, `service-bus-message-pane`, `message-grid-scroll`).
  - Added component test coverage for collapse/expand interactions and collapsed state class behavior.

- Reported behavior:
  - Even when horizontal scroll exists, users cannot effectively interact with it because the left panel dominates available width.

- Root-cause hypotheses:
  - Main layout grid does not allow content region to reclaim width while scrolling.
  - Scroll container is nested under an overflow-hidden parent.
  - Pointer/gesture area for horizontal scrollbar is clipped by panel layering.

- Likely impacted files/areas:
  - `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
  - `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
  - `src/SwebKit.App/wwwroot/css/` (page split layout and overflow rules)

- Implementation steps:
  - Revisit split-layout constraints and define explicit min/max widths for left panel.
  - Ensure message table owns its own horizontal scroll container in the right pane.
  - Confirm scrollbars remain reachable with panel expanded and collapsed states.
  - Add responsive behavior for narrow windows (panel collapse/overlay mode if already supported).

- Acceptance criteria:
  - Users can drag or wheel horizontal scroll in the message area with left panel visible.
  - Scrollbar interaction works in both expanded and collapsed panel states.
  - No clipping of message content region caused by parent overflow settings.

- Test coverage mapping:
  - `SB-UI-301`: Manual panel-expanded horizontal scroll interaction test.
  - `SB-UI-302`: Manual panel-collapsed horizontal scroll interaction test.
  - `SB-UI-303`: Component layout assertion for right-pane overflow container.

### SB-UI-BUG-04: Topic names show encoded artifacts like `&#9660; bundle-1`

- Implementation status: Complete (2026-03-08)
- Delivered this pass:
  - Removed encoded glyph entities from entity tree topic/subscription rows.
  - Switched to explicit plain glyph rendering (`▼`, `▶`, `↳`) and simple queue marker text.
  - Added regression tests to ensure encoded artifact patterns (`&#...;`) do not appear.

- Reported behavior:
  - Topic/label text renders with encoded HTML entity fragments instead of expected glyph/text.

- Root-cause hypotheses:
  - UI is rendering encoded strings directly from source labels without decoding.
  - Tree item label composes icon marker as encoded text instead of markup or icon component.
  - Double-encoding introduced between data mapping and component binding.

- Likely impacted files/areas:
  - `src/SwebKit.App/Components/ServiceBus/EntityTree.razor`
  - `src/SwebKit.App/Components/ServiceBus/NamespacePanel.razor`
  - `src/SwebKit.Core/Models/SbEntityInfo.cs` (if display-name normalization is introduced)

- Implementation steps:
  - Standardize tree label rendering to use plain text labels plus explicit icon markup/component.
  - Remove entity-like encoded glyph prefixes from bound data where presentation should own iconography.
  - Add sanitization rule to prevent raw encoded artifacts in visible names.

- Acceptance criteria:
  - Topic and queue names render as plain readable names.
  - Visual expand/collapse affordance is shown via UI iconography, not encoded text prefixes.
  - No `&#...;` style artifacts are visible in entity tree labels.

- Test coverage mapping:
  - `SB-UI-401`: Component test for entity label rendering without encoded artifacts.
  - `SB-UI-402`: Manual tree expansion/collapse regression check.

## Integrated Validation Checklist

- [ ] Validate all four bug scenarios in queue and topic contexts.
- [ ] Validate tab switching between normal view and DLQ view after layout/style changes.
- [ ] Validate mouse, touchpad, and keyboard interactions for horizontal navigation (manual).
- [ ] Validate UI behavior in MAUI desktop host at narrow and wide window sizes.
- [ ] Validate no regressions in existing selection, filtering, and message detail pane behavior.

## Traceability Backlinks

- `docs/features/service-bus/index.md`
- `docs/features/service-bus/technical-plan-ui.md`
- `docs/features/service-bus/test-plan.md`
- `docs/features/service-bus/technical-plan-backend.md`
- `docs/features/service-bus/technical-plan.md`
