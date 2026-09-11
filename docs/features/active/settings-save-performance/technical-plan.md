# Settings Save Performance, Entra ID Auth Mode & Message Detail Panel — Technical Plan

## 1. `useUpdateProfile` (`web/src/lib/hooks/useProfile.ts`)

Adopts the pattern `useUpdateCollections` already uses:

- `scope: { id: "profile" }` — serializes saves so two are never in flight, and their
  responses cannot land out of order.
- Accepts `ProfileData | ((prev: ProfileData) => ProfileData)`. The updater is evaluated
  **inside** `mutationFn`, which the scope defers until the previous save has settled, so it
  always sees current state rather than a render snapshot.
- `onSuccess` writes the sent payload back with `setQueryData` instead of invalidating. The
  endpoint returns `200` with no body, so the payload *is* the new state; invalidating meant
  a full refetch after every save.
- `onError` invalidates, so a rejected save does not leave the cache describing something
  the server never accepted.

## 2. `DraftInput` (`web/src/components/settings/DraftInput.tsx`, new)

A text input that keeps what you type locally and commits on **blur, Enter, or unmount**.
Escape reverts. It re-syncs when the stored value changes underneath it, guarded on the last
value it committed so a save echoing back its own text does not fight the cursor.

The unmount commit is not incidental: switching settings tabs removes the field without
firing a blur, and without it a pending edit would be lost.

## 3. Call sites

Every `updateProfile.mutate({ ...profile, … })` becomes the updater form, and every text or
number input becomes a `DraftInput`, across `ServiceBusSettings`, `AksSettings`,
`RedisSettings`, `StorageSettings`, `GeneralSettings` (Key Vault rows), `WorkspaceMapSettings`
and `AgentSettings` (observability config). Discrete controls keep committing immediately.

`grep "updateProfile.mutate({"` over `web/src` returns nothing afterwards, which is the check
that no snapshot writer was missed.

## 4. Auth mode

`ServiceBusNamespace.authMode` in `web/src/lib/types.ts` becomes
`"DefaultAzureCredential" | "ConnectionString" | "ServicePrincipal"`, matching `SbAuthMode`
exactly, and the Entra ID radio in `ServiceBusSettings` reads and writes
`DefaultAzureCredential`.

Both auth radio pairs (Service Bus, Storage) gain a per-record `name` so they form a real
radio group, plus test ids.

## 5. Message detail panel (`web/src/components/service-bus/MessageDetail.tsx`)

- `ServiceBusPage` raises the panel's `maxWidth` from 600 to 1400 and its default from 380 to
  560. Both of `MessageDetail`'s action rows gain `flex-wrap`, so at any width the controls
  reflow instead of being clipped.
- `stripPreamble` removes a UTF-8 BOM and surrounding whitespace, and is now used by **both**
  `detectFormat` and `tryFormatJson`. Previously only the former trimmed, which is why a
  BOM-prefixed body was labelled JSON and then failed to parse.
- `tryFormatJson` returns `null` on failure rather than silently handing back the raw body, so
  the UI can distinguish "not JSON" from "JSON shown raw" and say which.
- Pretty/Raw and Wrap toggles, persisted through `loadViewPreference`/`saveViewPreference` in
  `web/src/lib/stores/panel-preferences.ts` — the same mechanism the API client's response
  viewer uses.
- `bodyLineCount` is derived from `displayedBody`, so the count cannot disagree with what is
  rendered.
- The properties row becomes a `grid-cols-[minmax(0,14rem)_1fr_auto]`: the key column grows to
  the longest name up to a cap, then wraps, and the value always starts clear of it.
- The System tab uses `grid-cols-[repeat(auto-fit,minmax(13rem,1fr))]` so it collapses to one
  column in a narrow panel. A viewport breakpoint would not work here — the constraint is the
  panel's width, not the window's.
