---
status: Review
---

# Settings Save Performance, Entra ID Auth Mode & Message Detail Panel

## Scope

Reported against the Service Bus settings page: "the performance is horrible… sluggish slow
and horrible, and clicking on entra id btn does nothing." Both turned out to be general,
not Service Bus specific.

1. **Every settings field saved on every keystroke.** `useUpdateProfile` wrote the whole
   profile and then `invalidateQueries(["profile"])`. Since the profile is one document,
   each character cost a full `PUT`, an atomic rewrite of `profiles.json` on disk, a refetch
   and a re-render of the settings page — and because the inputs were controlled off server
   state, each character had to complete that loop before appearing.
2. **Saves were not serialized.** With no `scope`, mutations ran in parallel and their
   refetches landed out of order, so a refetch from an earlier keystroke could overwrite a
   later change. This is the pitfall `useUpdateCollections` was already fixed for; the
   profile hook never was. An existing e2e test had a comment describing the race and
   working around it by awaiting each `PUT`.
3. **The Entra ID radio sent a value the backend rejects.** `types.ts` declared
   `authMode: "ConnectionString" | "Entra"`, but the C# enum is
   `SbAuthMode { DefaultAzureCredential, ConnectionString, ServicePrincipal }`. `"Entra"` is
   not a member, so the entire profile save failed and the radio reverted. Entra ID auth is
   `DefaultAzureCredential`. **This was the actual cause of the reported button; fixing the
   hook alone did not fix it**, which the regression test caught.

4. **The message details panel could not show its own controls.** The panel is resizable
   but was capped at 600px, and neither of its action rows wrapped, so the trailing buttons
   (Replay, Schedule) were clipped off the right edge at every width the user could drag to.
5. **A JSON body was not prettified and offered no way to be.** `detectFormat` trimmed the
   body before deciding it was JSON, but `tryFormatJson` parsed the *untrimmed* string — so a
   payload carrying a UTF-8 BOM, routine for messages published by .NET, was labelled JSON and
   then silently failed to parse, rendering as one unreadable line with no explanation and no
   control to change it.
6. **Properties overflowed onto their values.** The key column was a hard `w-48 shrink-0`
   with no wrapping, so a longer key ran straight over the value beside it.

## Outcomes

- Typing in any settings field is instant; a save happens once per edit, on blur, Enter or
  when the field unmounts.
- Concurrent settings edits queue instead of racing, so none is silently lost.
- Entra ID can be selected for a Service Bus namespace and survives a reload.
- Both auth radio groups (Service Bus, Storage) now share a `name`, so they behave as real
  radio groups.
- The message details panel widens to 1400px and both action rows wrap, so no control is
  ever unreachable.
- A JSON body is prettified by default, with Pretty/Raw and Wrap toggles that persist, a
  line count reflecting what is actually on screen, and an explicit "Not valid JSON — showing
  raw" notice instead of silence.
- Property keys wrap within their own column; the System tab fits its columns to the panel
  rather than always forcing two.

## Non-goals

- No change to what the profile stores or to `ProfileRepository`.
- `DiagnosticsSettings` and `AppearanceSettings` write user settings, not the profile, and
  are untouched.

## Traceability

- Technical plan: `technical-plan.md`
- Test plan: `test-plan.md`
- Status: `status.md`
- Pitfalls: `docs/pitfalls/react-frontend.md`, new "Sidecar contract" section
