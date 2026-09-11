# Settings Save Performance, Entra ID Auth Mode & Message Detail Panel — Test Plan

## End-to-end (`npm run test:e2e`, Chromium)

`web/e2e/settings.spec.ts` — two new tests, since the Service Bus settings page had none.

| Scenario | Expected |
| --- | --- |
| Entra ID survives a reload | Adding a namespace and selecting Entra ID leaves the radio checked, and it is still checked after a reload. Uses `click()` plus an awaited assertion rather than `check()`, because the state change round-trips through a save and `check()` asserts synchronously |
| Text fields commit on blur | Typing a full namespace host fires **zero** `PUT /api/config/profiles`; blurring fires exactly one; the value survives a reload |

Both scope to the row the test adds (`.last()`): the e2e sidecar's appdata is shared by every
test in a file, so namespaces left by earlier tests are still present.

`web/e2e/api-client.spec.ts` — "environment variable source picker switches fields and lists
configured key vaults" updated. It filled Key Vault name and URL and awaited a `PUT` after
each; those fields now commit on blur, so it fills and then blurs. Its comment described the
save race as a live hazard to work around — that race is now actually fixed, so the comment
was corrected rather than left describing a bug that no longer exists.

## Regression check

`grep -rn "updateProfile.mutate({" web/src` must return nothing: every profile writer uses the
updater form.

## Results

- `npx tsc -b` — clean.
- `npm run test:unit` — 267 passed.
- `npx playwright test e2e/settings.spec.ts` — 17 passed.
- `npx playwright test e2e/storage.spec.ts e2e/storage-deferred.spec.ts` — 20 passed.
- `npx playwright test e2e/api-client.spec.ts e2e/settings.spec.ts` — see `status.md`.

## Manual verification

1. Service Bus settings: type a namespace host. It should be instant, with no per-character
   lag, and the value should persist after clicking away and reloading.
2. Select Entra ID. It should stay selected, and still be selected after a reload.
3. Switch settings tabs mid-edit without clicking away first — the edit must still be saved
   (this is the unmount commit).
4. The same typing behaviour applies to AKS, Redis, Storage, General (Key Vaults) and Map.

## Message detail panel

`web/e2e/service-bus.spec.ts` — "a JSON body is prettified by default and the choice is
remembered": the body reports `Format: JSON`, Pretty is pressed by default, the line count is
greater than one, switching to Raw drops it to one, and the choice survives a reload.

The line count is compared **numerically**, not by substring — `"Lines: 12"` contains
`"Lines: 1"`, which is exactly the false pass a `toContainText` assertion gave on the first
attempt.

Results: `npx playwright test e2e/service-bus.spec.ts e2e/service-bus-url-state.spec.ts` — 23
passed. One flake seen once on `scheduled messages panel opens and shows empty state`, which
passed in isolation and on re-run of the full file, and is unrelated to this panel.

Not covered by a test: the panel width and the wrapping of the two action rows, and the
properties/System column layout. These are pure layout with no behavioural assertion that
would not just restate the CSS — they need looking at.
