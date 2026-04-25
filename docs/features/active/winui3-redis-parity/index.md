# Feature Overview - winui3-redis-parity

---

title: "Feature Overview - winui3-redis-parity"
owner: ""
status: "Done"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Close the remaining Redis workspace parity gap in WinUI so operators can move from key browsing to the deeper analysis and bulk workflows already available in MAUI.

## Value

The native Redis route now carries the deeper operator workflows that previously kept Redis parity anchored to MAUI: keyspace health, prefix analysis, slow-log and hot-key correlation, Pub/Sub inspection, and safer bulk actions. The feature is accepted as done for now, with any remaining demo-mode or representative live-profile verification treated as optional cutover follow-up rather than a blocker.

## Scope

- In scope: native WinUI health and prefix tooling, deeper analysis surfaces such as slow-log or hot-key workflows and Pub/Sub inspection, and broader bulk operations that already exist in MAUI.
- In scope: compact, content-first adoption of shared cards and detail-pane primitives so analytics fit beside browse and edit flows.
- Out of scope: new Redis product features beyond existing MAUI behavior.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/RedisPage.razor`, `src/SwebKit.App/Components/Pages/RedisConfigForm.razor`
- WinUI target: `src/SwebKit.WinUI/Views/Redis/`, `src/SwebKit.WinUI/ViewModels/Redis/`

## Dependencies

- Shared baselines already absorbed from: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/redis.md`
- Pitfall files that apply: `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`

## Risks & mitigations

- Risk: analytics surfaces become harder to scan than the MAUI baseline.  
  Mitigation: keep the browser column narrow, move deeper insight into stacked right-pane cards, and reuse compact shared primitives.
- Risk: broader bulk actions land without equivalent safety cues.  
  Mitigation: keep selection explicit, scope namespace actions to loaded descendants only, and require typed confirmation for production bulk delete.

## Related documents

- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`
- Redis functionality: `docs/architecture/functionalities/redis.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: none; implementation is concentrated in `src/SwebKit.WinUI/Views/Redis/` and `src/SwebKit.WinUI/ViewModels/Redis/`
