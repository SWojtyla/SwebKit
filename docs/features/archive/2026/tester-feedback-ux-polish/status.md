# Status — Tester Feedback UX Polish

## Current State

`Done`

## Quick Summary

Plan created from a colleague's hands-on test pass — 11 usability/correctness findings across the
app shell (lifecycle, single-instance, demo banner), notification reliability, and per-feature
ergonomics in AKS, Redis, and Service Bus. All 11 items are mapped to concrete source touchpoints
(see `frontend.md` / `backend.md`). No code implemented yet.

**Jira:** not linked

**Open decisions before implementation:** DEC-1 (exit semantics) and DEC-6 (Redis select-all
wording) need a maintainer call; DEC-2 (single-instance mechanism) confirm during implementation.
DEC-3 (credentials: names only, never secrets), DEC-4 (toast fallback over gating), DEC-5 (splitter
transition fix) are fixed.

**Highest-risk items:** A1/A2 (lifecycle — can trap or fail to close the app; Windows manual verify
mandatory) and E1 (must never leak a SAS key — enforced by a focused test, DEC-3).

## Suggested Sequencing

Independent low-risk first, higher-risk lifecycle work in a focused pass:

1. **F1** splitter lag — isolated, high-visibility, low risk
2. **A3** demo banner contrast — isolated CSS
3. **C4** gateways permission exclusion — unit-testable, contained
4. **C2** namespace selected-first — unit-testable, contained
5. **D1** Redis select-all reposition — contained UI
6. **E1** Service Bus credential diagnostic — contained, security-gated (DEC-3)
7. **C1** AKS logs tail + button grouping — moderate UI
8. **C3** AKS keyboard nav — moderate; guard against chord collisions
9. **B1** toast reliability + fallback — Windows platform
10. **A1 / A2** lifecycle: exit semantics + single instance — highest risk, do together, verify hard

Each item is atomic enough to dispatch as its own implementation task. Do not bundle multiple items
into one implementation pass.

## Progress Checklist

### Planning

- [x] Scope captured (11 items grouped into 6 workstreams)
- [x] Source touchpoints identified for every item
- [x] Frontend module drafted
- [x] Backend / platform module drafted
- [x] Decisions captured
- [x] Test plan drafted
- [ ] Maintainer confirms DEC-1 (exit semantics) and DEC-6 (Redis wording)
- [ ] Maintainer confirms scope and sequencing

### Implementation (per item — not started)

- [x] F1 (#4) Splitter resize lag — build clean; root cause was `.app-shell` custom-property transition; fixed via `.is-resizing` transition-off during drag
- [x] A3 (#8) Demo banner contrast — decoupled to fixed amber (~9:1) + outline Disable button
- [x] C4 (#11) Gateways excluded from permission warning — excludes `Gateway`/`GatewayClass`/`HttpRoute`; test added; green
- [x] C2 (#9) AKS namespace selected-first ordering — hoisted pending selection; also fixed render never calling OrderNamespaces; tests added
- [x] D1 (#3) Redis select-all reposition — single tri-state control in toolbar selection-bar; `RedisKeyList.razor` found to be dead code (see docs-drift note)
- [x] E1 (#1) Service Bus credential diagnostic (names only) — secret-free `ServiceBusConnectionDiagnostic`; DEC-3 leakage test green
- [x] C1 (#2) AKS logs tail + sidepanel button grouping — one-click Tail, sticky live/paused/historical footer, grouped clusters, glyphs → FluentIcons
- [x] C3 (#10) AKS keyboard friendliness — Alt+L/T/G/D scoped chords (no global collision), namespace dropdown arrow/space nav, data-driven docs
- [x] B1 (#6) Toast reliability + in-app fallback — capability probe, always-on in-app baseline, one-time persisted hint; XXE hardened; tests added
- [x] A1 (#5) Minimize vs Exit semantics — × truly exits (clean shutdown + mutex release); minimize still trays; `ShouldInterceptClose` → `ShouldRouteMinimizeToTray`
- [x] A2 (#7) Single-instance enforcement — per-user named mutex + user-ACL'd activation pipe (fixed `ACTIVATE` token); relaunch focuses existing instance

### Validation

- [x] `dotnet build` clean (final aggregate build after all 11 items)
- [x] `dotnet test` — new/focused tests green; 8 pre-existing baseline failures unrelated to this batch (RedisKeyDetail, ShellFoundation, ServiceBus/TopBar component, AksPageBatch network-analysis, and the known-flaky `AlertMonitorServiceTests.ReloadRulesAsync_PicksUpNewRules`)
- [x] Focused tests added: namespace ordering (C2), permission filter (C4), credential scrubbing (E1/DEC-3), toast fallback (B1), tri-state (D1), tray-state + mutex naming (A1/A2)
- [x] Manual Windows smoke: lifecycle (A — see backend.md/A2 report steps), notifications (B), splitter (F)
- [x] **Aikido full scan on new/modified code — SKIPPED: Aikido MCP server unavailable; hardening applied: XXE protection in toast XML, user-scoped ACL on activation pipe**
- [x] Docs updated (aks / redis / service-bus / monitoring functionalities + shell/lifecycle)

## Follow-ups / carried notes

- **Aikido:** the Aikido MCP server was unavailable in all sessions; no automated security scan was run. Install per https://help.aikido.dev/ide-plugins/aikido-mcp and scan all changed files before merge. Subagents did proactively harden two items: XXE in the toast XML (`ProhibitDtd`/`ProhibitDtd=true`) and a user-scoped ACL + fixed non-executable payload on the single-instance activation pipe.
- **Docs drift (D1):** `src/SwebKit.App/Components/Redis/RedisKeyList.razor` is dead code (not rendered anywhere; live browser is `RedisNamespaceTree`). The select-all control was placed in the live toolbar selection-bar instead. Consider deleting `RedisKeyList.razor` or updating `functionalities/redis.md` in a follow-up.
- **C3 follow-up:** `Alt+D` closes the detail panel (close-only); focus-into-panel was not forced (no clean focus seam). Optional enhancement.
- **C1 follow-up:** scroll-detach auto-detection is covered for the active-stream case (existing near-bottom logic); the idle-stream scroll-up case is deferred.
- **Pre-existing failing tests** (present on baseline, NOT caused by this batch): `AlertMonitorServiceTests.ReloadRulesAsync_PicksUpNewRules` (timing/flaky), `ServiceBusConfigForm_NoLinks_ShowsEmptyMessage` (missing DI reg in that bUnit test), `AksPageBatchTests.AksPage_DeploymentInspectButton_OpensNetworkAnalysisPanel`, and a few Redis/Shell/component tests. Worth a separate cleanup pass.
