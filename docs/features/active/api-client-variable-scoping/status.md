---
status: Review
---

# API Client Variable Visibility & Environment Scoping — Status

- **Current phase:** Review — implemented and validated, awaiting sign-off.
- **Reported by:** Sebastien, from daily use of the Tauri build as the Bruno/Postman
  replacement (a `POST` returning 400 because three body variables were undefined and
  nothing said so).
- **Implementation PR:** not raised yet.

## Validation

| Gate | Result |
| --- | --- |
| `npx tsc -b` | clean |
| `npm run test:unit` | 234 passed (17 files) |
| `dotnet build SwebKit.slnx` | sidecar, Core and Blazor build |
| `dotnet test tests/SwebKit.Core.Tests` | 813 passed |
| `npx playwright test e2e/api-client{,-layout,-variables}.spec.ts` | 55 passed, 1 pre-existing failure |

The one e2e failure — `api-client.spec.ts:834`, nested-folder drag — reproduces on this
branch with the changes stashed, so it predates this work and belongs to
`api-client-drag-reorder`.

## Definition of Done

- [x] Body-editor variables coloured by resolution state, with hover.
- [x] Undefined variables announced before sending, outside the preview toggle.
- [x] cURL panel reproduces the request; secrets stay as tokens.
- [x] Global and collection-scoped environments apply together, project overriding global,
      with the same precedence on both sides of the wire.
- [x] Each picker offers only environments of its own scope.
- [x] Variable rows legible at real name lengths, one row per variable.
- [x] Unit tests for every new pure function; e2e for the body highlighting and the warning.
- [x] Feature docs written.
- [ ] Manual verification in the running Tauri app (see `test-plan.md`) — **owner: Sebastien**,
      since it needs the real `DEV (via APIM)` environment and Portima credentials.
- [ ] Aikido security scan per `docs/security/aikido-mcp-scan.md`.

## Regression found in use, and fixed

Filtering each picker by scope, combined with rendering the project picker only when a
*request tab* was open, left an estate of entirely collection-scoped environments with
**nothing selectable anywhere** — the global picker listed only global environments (of
which there were none) and the project picker never appeared.

Fixed by:

- resolving the environment layers from `activeCollection ?? selectedCollection`, so
  choosing a collection in the tree is enough and no request need be open;
- always rendering the project picker, disabled and explaining why when there is no
  collection in context, rather than hiding it;
- labelling both pickers ("Global" / the collection name) and reporting the count of the
  *merged* scope, which is the number that matters and which neither layer alone gives;
- grouping the Environment Manager list by scope with headers, showing which environment
  is active **per group** rather than only the global slot, and surfacing environments
  scoped to a deleted collection, which were previously invisible and unreachable.

Guarded by `api-client.spec.ts` "a collection-scoped environment is selectable from its own
picker".

## Follow-ups (not in this feature)

1. `GraphQlPanel` and `WebSocketPanel` bodies are plain `<textarea>`s whose content is
   substituted at send time, so they still have the blind spot this feature closed for the
   HTTP body. They need the `VariableInput` overlay treatment.
2. `ApiClientWorkflowService.BuildCurlAsync`, `GraphQlSchemaService` and
   `GraphQlSubscriptionService` still take a single environment and pass
   `[activeEnvironment]`; threading both layers through them would make backend cURL export
   and GraphQL introspection layer-aware too.
3. `EnvironmentManager` snapshots `environments` into `useState` at mount, the same
   stale-snapshot hazard fixed here in `CollectionVariableEditor`. Not fixed because it
   holds far more in-progress edit state, so a naive re-sync could discard user work.
4. Unrelated but adjacent, from the same session: `scripts/tauri/run-dev.ps1` sets no
   `SWEBKIT_APPDATA_ROOT`, so dev runs read and write the installed app's real data; and
   `CollectionsStore` has no `SchemaVersion` (unlike `EnvironmentsStore`) and only one
   generation of backup.
