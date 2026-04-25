# Status - winui3-cutover-audit-hardening

---

title: "Status - winui3-cutover-audit-hardening"
owner: ""
state: "In Progress"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-24"
last_updated: "2026-04-25"

---

## Quick summary

The cutover umbrella now owns coordination and validation, not the whole implementation backlog. The remaining migration work has been split into separate active features, with layout redesign first, settings completeness second, and domain parity slices after that.

**Jira:** not linked

**Current focus:** hold the dependency order steady while the new feature plans become the source of truth for execution.

## Progress checklist

### Coordination and planning

- [x] Baseline migration archived as `winui3-migration`
- [x] Current route coverage and hardening baseline reviewed
- [x] MAUI versus WinUI parity comparison completed by domain
- [x] Feature-specific active plans created for remaining migration work

### Shared cutover dependencies

- [ ] Layout redesign implemented
- [ ] Settings completeness implemented
- [ ] Domain parity features implemented and validated

### Final cutover gate

- [ ] Manual smoke suite executed against the native host
- [ ] Cross-feature validation results recorded
- [ ] Architecture and functionality docs updated where behavior changes
- [ ] Explicit cutover recommendation recorded

## Completed

- Verified that the WinUI app launches and remains alive; the current blocker is not a launch-time crash.
- Confirmed that the current migration already has native routed pages for Dashboard, Settings, Service Bus, AKS, Redis, Storage, Pipelines, and Observability.
- Landed the current hardening baseline: shared deferred first-load scheduling, disposal guards, Pipelines and Observability readiness states, app-level unhandled-exception logging, and focused WinUI readiness tests.
- Archived the completed `winui3-migration` baseline as a historical checkpoint.
- Replaced the monolithic remaining-work plan with dedicated active features for layout redesign, settings completeness, Service Bus, AKS, Redis, Storage, Pipelines/Releases, and Observability.

## Remaining

- Execute the dependency features in the planned order.
- Decide which domain gaps remain truly cutover-critical once the split features land.
- Run the first full native-host smoke pass and record the cutover gate.
- Produce the final recommendation on whether `SwebKit.App` can move to legacy-only status.

## Blockers

- Live validation of Pipelines and Observability remains environment-sensitive because the current machine state does not provide a successful Azure DevOps or Azure credential path.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: In progress
- Last known implementation baseline: `build-winui` succeeded on 2026-04-25, `dotnet test tests/SwebKit.WinUI.Tests/SwebKit.WinUI.Tests.csproj` succeeded with 8 passing tests, and `dotnet test tests/SwebKit.DevOps.Tests/SwebKit.DevOps.Tests.csproj` succeeded with 34 passing tests.
- This planning update changes docs only; execution validation remains owned by the split features and the final cutover gate.

## Notes

- The split feature folders are now the execution source of truth; this umbrella should only retain cross-feature cutover work.
