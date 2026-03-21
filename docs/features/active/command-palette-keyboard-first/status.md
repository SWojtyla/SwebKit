# Status — Command Palette & Keyboard-First Navigation

---

title: "Status - Command Palette & Keyboard-First Navigation"
owner: ""
state: "Planned"
branch: ""
started: ""
last_updated: "2026-03-21"

---

## Quick summary

Current state: Planned — feature scoped, awaiting implementation start. This is the largest-scope item in the current planned set.

## Progress checklist

- [x] Planning complete
- [ ] Design reviewed
- [ ] `CommandRegistry` extended (context-awareness, availability predicates)
- [ ] `CommandPalette.razor` overhauled (recent, fuzzy search, categories, shortcut display)
- [ ] Recent commands persisted in `UiStateRepository`
- [ ] All feature commands registered (Service Bus, AKS, Redis, Storage, Releases)
- [ ] Grid keyboard navigation (↑↓ selection, Enter, Escape, Delete)
- [ ] Focus trap in all modals and panels
- [ ] Shortcut reference sheet component
- [ ] Skip-to-content link
- [ ] Tests (unit / manual)
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Feature scoped in `index.md`

## Remaining

- Author `backend.md` (CommandRegistry extension, selection context service design)
- Author `frontend.md` (command palette component design, grid keyboard nav patterns)
- Author `test-plan.md`
- Extend `CommandRegistry` with `IsAvailable` predicate and context awareness
- Implement fuzzy search in command palette
- Implement recent commands (persist + display)
- Implement category/section grouping in palette UI
- Implement shortcut display column in palette rows
- Register Service Bus commands in `ServiceBusPage.razor`
- Register AKS commands in `AksPage.razor`
- Register Redis commands in `RedisPage.razor`
- Register Storage commands in `StoragePage.razor`
- Register Releases commands in `ReleasesPage.razor`
- Implement grid keyboard navigation (↑↓, Enter, Escape, Delete) per page
- Implement focus trap in `ConfirmDialog.razor`, `Modal.razor`, command palette, side panels
- Implement shortcut reference sheet (`?` shortcut, command in palette)
- Add skip-to-content link in `MainLayout.razor`
- Full keyboard-only walkthrough of each feature area

## Blockers

None.

## Validation

Not started.
