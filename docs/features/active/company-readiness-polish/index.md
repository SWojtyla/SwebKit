# Feature Overview — company-readiness-polish

---

title: "Company Readiness Polish"
owner: ""
status: "Planned"
jira: ""
created: "2026-07-03"
updated: "2026-07-03"

---

## Goal

Make every screen in the app presentable to colleagues: fix sloppy UI, inconsistent controls, personal easter eggs, broken empty states, mixed component primitives, and performance rough edges — screen by screen in nav order.

Dashboard gets a full overhaul in a **separate feature**. This feature covers everything else.

## Value

The app was built for personal use. Company sharing requires every surface to feel intentional, not half-finished. This is a one-time quality pass, not a feature delivery.

## Screens in scope (nav order)

| #   | Screen                  | Status  |
| --- | ----------------------- | ------- |
| 1   | Service Bus             | Planned |
| 2   | AKS                     | ✅ Done |
| 3   | Redis                   | ✅ Done |
| 4   | Storage                 | ✅ Done |
| 5   | Monitoring              | ✅ Done |
| 6   | AI Agent (Sebski panel) | ✅ Done |
| 7   | Settings                | ✅ Done |

## Screens excluded — full rework required (separate features)

These screens need more than a polish pass. They are excluded from this feature and will each get their own feature plan.

| Screen            | Reason                                                                     |
| ----------------- | -------------------------------------------------------------------------- |
| Dashboard         | Full overhaul — separate feature already planned                           |
| Pipelines         | Full rework needed — layout, tab model, scope picker, UX flow              |
| Observability     | Full rework needed — resource selector, per-tab layout, FluentUI migration |
| Incident Timeline | Full rework needed — scope toolbar, evidence layout, summary section       |
| API Client        | Full rework needed — complex standalone surface, separate feature          |

> These screens show a placeholder "needs rework" state in-app to signal to users that improvement is coming.

## Out of scope

- Dashboard (separate full-overhaul feature)
- New features or behaviour changes — fixes only
- CSS token/primitive system refactor — already done in style-system-polish-9
- Backend logic changes unless directly causing a visible UI bug
- Settings unsaved-changes / production-danger pill styling (current implementation is sufficient)

## Cross-cutting issues (apply to multiple screens)

These show up across many screens and should be addressed consistently:

- **Mixed button primitives** — some surfaces still use raw `<button>`, `FluentButton`, or `<a class="page-header-action-btn">` instead of `AppButton` / `AppIconButton`. Use `AppButton` everywhere a button-shaped control exists in app chrome.
- **Raw `<select>` elements** — Storage account picker still uses a raw `<select>`. Use `AppSelect`.
- **`<a>` tags styled as buttons** — several header action slots use `<a class="page-header-action-btn" href="...">` for navigation links. These are semantically correct anchors; decide once whether to keep them as anchors or convert to buttons with `NavigationManager`. Document the decision.
- **Personal content** — the `"🎉 Everything's fine. Suspiciously fine. — SW"` easter egg in AKS must be removed or neutralised before company sharing.

## Dependencies

- Style primitives (`AppButton`, `AppSelect`, `AppIconButton`, `PageToolbar`, `SegmentedControl`) already exist from style-system work.
- `EmptyState`, `ErrorCallout`, `LoadingSpinner` components exist.
- No new components expected — polish uses what's already there.

## Risks

- CSS bridges (scoped `::deep` from style-system-polish-9) may need revisiting if a control migration changes an ancestor class.
- Do not change behaviour — only visual and UX consistency. Any discovered functional bug should be filed separately unless trivial to fix in the same pass.
