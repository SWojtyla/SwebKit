# Test Plan — Post-Migration UX & Feature-Parity Review

Most findings in this plan are UI/wiring gaps, not algorithmic bugs — verification is primarily
manual, against demo mode first and a real backend where available. Add automated coverage only
where a fix introduces real logic (e.g. a new sidecar endpoint, the Redis health analyzer port).

## Phase 1 — Critical (manual, do these before marking anything else)

- [ ] Auth secrets: create a Bearer-auth request, save, inspect `collections.json` — no raw secret
      present; restart the app — auth still works via the new secret store
- [ ] Monitoring: scope decision recorded in `index.md`/`status.md` (rebuild vs. drop) before any
      code change
- [ ] Redis Pub/Sub: tab either clearly disabled ("not available") or subscribe/publish actually
      round-trips a real message end to end
- [ ] Redis export: export 3+ keys of mixed types (string/hash/list), open the file, confirm each
      entry has its own correct type/TTL/value, not the last-viewed key's
- [ ] Storage: upload a real file, confirm it appears in the container; edit metadata, save,
      reload the page, confirm the edit persisted; toggle `allowMutations` off, confirm
      Upload/Copy/Delete are actually disabled, not just decorated

## Phase 2 — Quick wins (manual)

- [ ] `DevOpsSettings` tab no longer appears in Settings
- [ ] Appearance Font Size / Density either visibly change the UI or are removed
- [ ] API Client: add a capture rule, switch requests and back — the rule is still there
- [ ] Trigger a failing AKS mutation (e.g. restart a deployment that no longer exists) — a toast
      with the server's error appears

## Phase 3 — Feature-level (manual, per domain)

- [ ] API Client: body editor shows syntax highlighting and flags invalid JSON inline; a generated
      variable (GUID/timestamp) can be previewed and regenerated inline
- [ ] Redis: Keyspace Health tab shows real findings (no-TTL/oversized/heavy-prefix/hot-key), not a
      copy of Server Info; Prefix Memory shows byte sizes, not counts; a hash field can be added/
      edited/deleted from the detail view; the namespace tree expands lazily past 20 children
- [ ] Storage: drag-and-drop a file onto the blob list with a visible progress bar; compare two
      blob versions and see an actual diff, not just a list
- [ ] Dashboard: pin a resource from an AKS/SB/Redis/Storage page and see it on the Dashboard;
      Settings shows a getting-started checklist reflecting real per-area configuration state
- [ ] Shell: the footer shows per-area connection health, not one global dot; Ctrl+K fuzzy-matches
      a partial resource name and lists recently used entries
- [ ] AKS/SB: drag a panel's edge to resize it and reload — the width persisted; the Pods grid
      shows a CPU/Memory column; navigating to a pod's logs and hitting the browser back button
      returns to the previous view instead of losing state

## Automated coverage to add alongside fixes

- [ ] Sidecar: unit/integration test per newly-registered endpoint (Storage upload/copy/metadata/
      versions/undelete; Redis pubsub/channels; Redis keys/export; Redis hash/list/set/zset
      mutation routes) — assert demo-mode gating behaves like every existing endpoint
- [ ] Redis keyspace-health analyzer: unit test each finding type (no-TTL, oversized-value,
      heavy-prefix, hot-key) against a synthetic key set, same cases the old
      `RedisKeyspaceHealthAnalyzer` covered if that test file still exists in git history
      (`git show 85d24ed -- '**/RedisKeyspaceHealthAnalyzer*Tests*'`)
- [ ] Secret store: unit test save/retrieve round-trip and confirm no secret value appears in any
      serialized `collections.json` snapshot

## Quality commands

```powershell
dotnet build SwebKit.slnx
dotnet test tests/SwebKit.Core.Tests
cd web; npm run build; npm run test
```

## Regression watch

Re-run the existing `aks-migration-fixes` and `service-bus-migration-fixes` manual smoke tests
after Phase 3's AKS/SB items land — the shared `<ResizablePanel>` and mutation `onError` wiring
touch the same components those plans already modified.
