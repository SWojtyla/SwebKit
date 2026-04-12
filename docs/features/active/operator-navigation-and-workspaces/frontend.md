# Frontend Plan - operator-navigation-and-workspaces

---

title: "Frontend Plan - operator-navigation-and-workspaces"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Let operators move through SwebKit like a serious operations workspace: find the right resource quickly, reopen recent context, keep important resources pinned, and restore named investigation state without manually rebuilding each page.

## Impacted areas

- Shell and search surfaces:
- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/Components/Layout/TopBar.razor`
- `src/SwebKit.App/Components/Shared/CommandPalette.razor`
- `src/SwebKit.App/Services/CommandRegistry.cs`
- `src/SwebKit.App/Services/SelectionContext.cs`
- `src/SwebKit.App/Services/TabService.cs`
- Current page integration points that already participate in selection or search-like behavior:
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Pages/StoragePage.razor`
- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`
- Likely shell consumers of the shared favorite/workspace model:
- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `src/SwebKit.App/Components/Layout/LeftNav.razor`
- Tests:
- `tests/SwebKit.App.Tests/ComponentTests.cs`
- `tests/SwebKit.App.Tests/CommandRegistryTests.cs`
- `tests/SwebKit.E2E.Tests/AppUiTests.cs`

## UX notes

- Current gap to close:
- The command palette has useful command execution, but resource search is still an ad hoc `go ` branch.
- Favorites exist only as dashboard pins sourced from `AppConfig.FavoriteEntities`.
- Open tabs persist, but they are not yet a named workspace model.
- The shell has no one obvious place to see recent resource context across pages.
- Target user flow:
- Open command palette and search for either a command or a resource without needing different mental models per capability area.
- Revisit a recent Service Bus entity, AKS workload, pipeline, or Observability resource from one shell-level entry point.
- Save a named investigation workspace and later restore the route, resource, filters, and tabs that matter for that investigation.
- Accessibility expectations:
- Keep keyboard-first command palette behavior.
- Ensure search sections and results are screen-reader friendly and not color-only.
- Keep workspace save/restore affordances usable without pointing-device-only interactions.

## API / contract changes

- Replace one-off palette resource generation with structured resource results contributed through a shared shell contract.
- Expose favorite and recent resource models to shell components and dashboard from one canonical source.
- Bind saved workspace UI to a backend-managed semantic snapshot model rather than raw component object state.
- Maintain backward compatibility for existing recent command behavior while expanding shell-level recent context.

## Tasks

### Wave 1 - Palette precision and unified resource search [blazor-expert] (depends on `shell-ux-foundation`)

- [ ] Replace the current ad hoc `go ` path with a structured search-result model.
- [ ] Support one ranking pipeline for commands, resources, and later workspace picks.
- [ ] Keep current keyboard shortcuts and command execution behavior intact.
- [ ] Ensure palette sections make sense for both area-scoped and global results.

### Wave 2 - Recent and favorite resources [blazor-expert] (depends on Wave 1)

- [ ] Add shell-level surfaces for recent and favorite resources.
- [ ] Reuse the same favorite model on the dashboard pinned panel instead of maintaining separate shell-only logic.
- [ ] Hook the main operator pages into a common selection/resource-publication flow.
- [ ] Make favorite actions lightweight enough to use during active investigation.

### Wave 3 - Named workspaces [blazor-expert] (depends on Waves 1-2)

- [ ] Add a save-workspace flow that captures route, resource, and supported page state.
- [ ] Add a restore flow that navigates first and then rehydrates page contributors safely.
- [ ] Show partial-restore messaging for unsupported or stale workspace state.
- [ ] Keep workspace UI shell-level rather than embedding different save/restore patterns into each page.

### Wave 4 - Cross-page rollout and validation [blazor-expert] (depends on Waves 1-3)

- [ ] Roll the contributor model out across Service Bus, AKS, Observability, and Incident Timeline first.
- [ ] Decide whether Redis, Storage, and Pipelines should join in the same feature or a short follow-up slice.
- [ ] Add focused component and E2E coverage for resource navigation and workspace restore.
- [ ] Capture any restore-semantics tradeoffs in `decisions.md`.

## Validation

- Component tests: Not started.
- Manual UX checks:
- Verify search can locate real resources from multiple domains in one palette.
- Verify a recent resource can be reopened after route changes and app restart.
- Verify workspace restore lands on the correct route before page-level hydration starts.
- Verify partial restore is explicit and does not look like silent data loss.

## Notes

- Relevant pitfalls from `docs/pitfalls/blazor-maui.md`:
- BL-3 and BL-5 - contributor or restore logic in components must guard async parameter-driven work to avoid duplicate loads.
- BL-4 - do not rely on `@if` to preserve important workspace UI state inside transient panels.
- BL-7 - any streaming or long-lived restore helpers must cancel on component dispose.
- Relevant pitfalls from `docs/pitfalls/dotnet-csharp.md`:
- CS-2 - cancellation must propagate cleanly through restore/navigation flows.
