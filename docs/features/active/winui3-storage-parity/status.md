# Status - winui3-storage-parity

---

title: "Status - winui3-storage-parity"
owner: ""
state: "Review"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

The native Storage workspace now covers the remaining reachable parity slice: version-history compare, download, and restore for mutation-enabled storage profiles plus loaded-blob ZIP download. Focused WinUI validation is green; the remaining work is a manual UI smoke pass over live storage content.

**Jira:** not linked

**Current focus:** run a native smoke pass over version-aware containers and ZIP download behavior, then close the feature.

## Progress checklist

- [x] MAUI versus WinUI Storage gap captured
- [x] Batch and version workflows confirmed
- [x] Large-file and binary-preview hardening scope confirmed
- [x] Shared detail/state behavior aligned in the native workspace
- [x] Focused validation approach defined
- [x] Docs aligned after implementation begins

## Completed

- Confirmed that browse, detail, and SAS-copy baselines already exist natively.
- Confirmed that large-file and binary-preview safeguards already existed in the native route, so the remaining gap was workflow parity instead of preview safety.
- Added native version history loading, compare, restore, and version download flows in the Storage detail pane, with restore respecting the existing `AllowMutations` safety gate.
- Added native multi-select loaded-blob ZIP download behavior in the blob workspace.
- Added focused `StoragePageViewModelTests` coverage for version and ZIP-download page-state behavior.
- Updated active-feature and functionality docs to match the delivered native baseline.

## Remaining

- Run a manual WinUI smoke pass against live storage content that exercises version-aware blobs, restore, and loaded-blob ZIP download.
- Track deleted-blob discovery and undelete as a separate storage recovery follow-up because the current hosted and native list surfaces do not expose soft-deleted blobs.
- Verify the ZIP-selection affordance and result messaging remain clear in the final native shell layout.

## Blockers

- None.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: Focused WinUI storage validation is green; manual live-data smoke coverage is still pending.
- Automated checks: `dotnet test .\tests\SwebKit.WinUI.Tests\SwebKit.WinUI.Tests.csproj --filter StoragePageViewModelTests`

## Notes

- ZIP export intentionally operates over blobs currently loaded in the active folder view. Operators need to page in more blobs before selecting them for the archive.
- Deleted-blob recovery is intentionally deferred until the storage workspace can discover soft-deleted blobs in a first-class way.
