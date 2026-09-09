---
status: Review
---

# Settings Save Performance, Entra ID Auth Mode & Message Detail Panel — Status

- **Current phase:** Review — implemented and validated.
- **Reported by:** Sebastien: "the performance is horrible on that page. It sluggish slow and
  horrible. and clicking on entra id btn does nothing."
- **Implementation PR:** not raised yet.

## Definition of Done

- [x] Settings text fields commit once per edit rather than per keystroke.
- [x] Profile saves serialized, so concurrent edits cannot silently overwrite each other.
- [x] A rejected save resyncs the cache instead of leaving it describing unsaved state.
- [x] Entra ID selectable for a Service Bus namespace and persisted.
- [x] Both auth radio pairs form real radio groups.
- [x] All seven profile-writing settings pages migrated; no snapshot writers remain.
- [x] Regression tests for both reported symptoms.
- [x] Pitfalls recorded.
- [x] Message detail panel widens and its action rows wrap.
- [x] JSON bodies prettify by default, with persisted Pretty/Raw and Wrap toggles and an
      explicit notice when the payload does not parse.
- [x] Property keys and System fields no longer overflow their columns.
- [ ] Manual verification in the running Tauri app — **owner: Sebastien**.
- [ ] Aikido security scan.

## Note on the diagnosis

The two reported symptoms had *different* causes, and only one was the hook. Fixing the save
storm and the ordering race did **not** make the Entra ID radio work — the value `"Entra"`
was simply not a member of the C# `SbAuthMode` enum, so the whole profile save was rejected.
That only surfaced because the regression test still failed after the hook fix; without it
the page would have felt faster while the button stayed broken.

## Follow-ups

1. `useUpdateUserSettings` (`DiagnosticsSettings`, `AppearanceSettings`, agent profiles) has
   not been audited for the same per-keystroke pattern.
2. The other enum-backed fields crossing this boundary (`transportType`, storage `useAad`,
   generator kinds) were not systematically checked against their C# definitions. The
   `authMode` mismatch suggests a sweep would be worthwhile.
