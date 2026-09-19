# API Client Fixes

## Status

`Review` — see `status.md`.

## Goal

Make the API Client's variable system actually work end-to-end for secrets, and
give generators useful bounds instead of fully-random output.

Two user-reported problems drove this, plus a focused bug scan of the feature:

1. **"Secret Store" variables never resolve.** The mode offers a bare
   `credentialKey` input — a lookup key into the OS credential store — but
   nothing in the app can put a secret there, and the store it resolves from
   (`SidecarCredentialStore`, KeySharp per-key entries) is a *different* store
   than the Tauri keyring vault auth secrets use. Net effect: the variable
   resolves to null and `{{var}}` is sent literally.
2. **Faker dates are fully random.** `date.past`/`date.future`/`date.recent`
   map to bare Bogus calls with no bounds — the user wants "before/after a
   date" control.

## Scope

| Item | Where |
| ---- | ----- |
| 1. Secret Store mode works end-to-end | sidecar credential endpoints (`save`/`delete`/`preview` over `ICredentialStore`) + value field in `VariableList` (debounced save, masked, exists-check preview) + hint text |
| 2. Bounded faker dates | `fakerDateAfter`/`fakerDateBefore` on `VariableGeneratorDefinition`, pickers for `date.*` categories, new `date.between` category |
| 3. Scan bugs | `Integer` `maxInt = int.MaxValue` overflow; `authorization` missing from frontend `isLikelySecret`; unresolved `{{var}}` now produces a send-time warning in the response panel |

## Non-goals

- Collection-level secrets (`CollectionVariable` has no `secretSource`) —
  needs a model change; documented as a gap only.
- Unifying the Tauri keyring vault and `SidecarCredentialStore` — the
  credential endpoints make the env-var path self-consistent; auth keeps its
  existing transient-secret flow.
- List-generator comma escaping (minor; documented).

## Docs

- `status.md` — checklist and validation
- `decisions.md` — D-log for the credential-store and date-bound choices
