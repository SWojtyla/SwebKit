# Test Plan — Component Decomposition

---

title: "Test Plan — Component Decomposition"
owner: ""
status: "Not started"
created: "2026-03-27"
updated: "2026-03-27"

---

## Goal

Verify that decomposing AksPage, RedisPage, and ServiceBusPage produces zero regressions in user-visible behavior while enabling isolated component testing.

## Scope

**In scope:**

- Existing 74+ bUnit tests in `SwebKit.App.Tests` must remain green
- New bUnit tests for each extracted component
- Manual regression testing of full page workflows
- Verify correct parameter binding and event callback wiring

**Out of scope:**

- E2E browser automation (no Playwright setup yet)
- Performance benchmarks (no functional changes)
- Visual pixel-diff regression (manual visual checks instead)

---

## Main scenarios (priority order)

### Phase 1 — AksPage

| #   | Scenario                               | Expected result                                                                      |
| --- | -------------------------------------- | ------------------------------------------------------------------------------------ |
| 1   | Open YAML viewer for a deployment      | YAML text displays with syntax highlighting; edit/close buttons work                 |
| 2   | Edit YAML and save                     | Textarea appears, edits apply to cluster, view returns to read-only                  |
| 3   | Search-in-YAML highlights matches      | Match count shown; highlights visible in `<pre>`                                     |
| 4   | Cancel YAML edit                       | Edit text discarded; returns to view mode                                            |
| 5   | View Helm release history              | History table loads with revisions; rollback buttons appear for superseded revisions |
| 6   | Rollback Helm release in production    | Confirmation dialog requires typing release name; rollback executes; data reloads    |
| 7   | View Helm values                       | Values YAML displayed with syntax highlighting                                       |
| 8   | Switch cluster context                 | Context dropdown triggers reconnect; namespace list refreshes; data reloads          |
| 9   | Switch namespace via searchable picker | Picker opens, filters on typing, selects namespace, data reloads                     |
| 10  | Context menu → restart deployment      | Confirmation shown; deployment restarts; grid refreshes                              |
| 11  | Context menu → kill pod (production)   | Production guard requires name typing; pod deleted; grid refreshes                   |
| 12  | Context menu → scale deployment        | Scale stepper appears; apply sends scale command                                     |
| 13  | Context menu → port-forward            | Dialog opens; session starts; sessions panel shows active forward                    |
| 14  | Context menu → open pod shell          | Shell opens in external terminal                                                     |
| 15  | Keyboard navigation (↑↓) in grids      | Selection moves between rows correctly                                               |
| 16  | Keyboard shortcuts (l, y, r, s, p, d)  | Correct action triggers for selected resource                                        |
| 17  | Tab switching between resource types   | Grid switches; selection clears; filter resets                                       |
| 18  | Events panel toggle                    | Events panel appears/disappears; warning count badge correct                         |
| 19  | Concurrent panel exclusion             | Opening YAML closes logs; opening logs closes YAML                                   |
| 20  | Component disposal                     | CTS cancelled; event subscriptions cleaned; commands unregistered                    |

### Phase 2 — RedisPage

| #   | Scenario                              | Expected result                                     |
| --- | ------------------------------------- | --------------------------------------------------- |
| 21  | Select different cache from dropdown  | Connection switches; keys reload                    |
| 22  | Scan keys with pattern                | Keys list populates; namespace tree builds          |
| 23  | Toolbar: Delete key with confirmation | Confirmation shown; key deleted; list refreshes     |
| 24  | Toolbar: Purge all (production)       | Production guard active; all keys deleted           |
| 25  | Toolbar: Export JSON                  | File save dialog; JSON file written                 |
| 26  | Multi-select mode toggle              | Selection checkboxes appear; batch delete available |
| 27  | Key detail: edit string value         | Value saved to Redis                                |
| 28  | Key detail: set/remove TTL            | TTL updated correctly                               |

### Phase 3 — ServiceBusPage

| #   | Scenario                            | Expected result                                                       |
| --- | ----------------------------------- | --------------------------------------------------------------------- |
| 29  | Add namespace via connection string | Connection string parsed; namespace appears in sidebar; entities load |
| 30  | Remove namespace                    | Namespace removed; related tabs closed; credential deleted            |
| 31  | Expand/collapse namespace sidebar   | Sidebar state persists in localStorage                                |
| 32  | Open entity tab from namespace tree | Tab opens; message list loads                                         |
| 33  | Open scheduled messages tab         | Scheduled tab opens with correct namespace context                    |

---

## Automated coverage

### Existing tests (must remain green)

All 74+ tests in `tests/SwebKit.App.Tests/`:

- `ServiceBusPageTests.cs`
- `MessageListViewTests.cs`
- `MessageComposerTests.cs`
- `EntityTreeTests.cs`
- `ScheduledMessagesComponentTests.cs`
- `NotificationServiceTests.cs`
- `CommandRegistryTests.cs`
- `ComponentTests.cs`
- `PageDataCacheTests.cs`

### New tests per extraction

**Phase 1:**

| Component          | Test file                  | Key test cases                                                                                                                               |
| ------------------ | -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `AksYamlViewer`    | `AksYamlViewerTests.cs`    | Renders YAML content; edit mode toggle; search highlights; save calls client; cancel reverts; validation error shown; close callback fires   |
| `AksHelmPanel`     | `AksHelmPanelTests.cs`     | Renders history table; rollback button visible for superseded; rollback fires confirmation; values YAML displayed; close callbacks fire      |
| `AksConnectionBar` | `AksConnectionBarTests.cs` | Renders context dropdown; renders namespace picker; context change fires callback; namespace search filters; namespace select fires callback |

**Phase 2:**

| Component            | Test file                    | Key test cases                                                              |
| -------------------- | ---------------------------- | --------------------------------------------------------------------------- |
| `RedisConnectionBar` | `RedisConnectionBarTests.cs` | Renders cache selector; cache change fires callback with new client         |
| `RedisToolbar`       | `RedisToolbarTests.cs`       | Renders all buttons; disabled state propagates; click events fire callbacks |

**Phase 3:**

| Component                  | Test file                          | Key test cases                                                                      |
| -------------------------- | ---------------------------------- | ----------------------------------------------------------------------------------- |
| `ServiceBusNamespacePanel` | `ServiceBusNamespacePanelTests.cs` | Renders namespace list; add form validation; expand/collapse; remove fires callback |

### Test data and setup

- Use existing `DemoServiceBusClient` / `DemoAksClient` patterns for mock data
- For AksYamlViewer: mock `IAksClient.GetResourceYamlAsync` to return sample YAML
- For AksHelmPanel: mock `IAksClient.GetHelmReleaseHistoryAsync` to return sample revisions
- JS interop methods (`yamlHighlight.*`) should be mocked via bUnit's `JSInterop` — existing test patterns already do this

---

## Manual checks

| Check                    | Steps                                                                                                                                                                           |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AKS full workflow        | Connect to cluster → switch namespace → view deployments → open YAML → edit → cancel → open Helm values → view history → rollback → restart deployment → scale → switch context |
| Redis full workflow      | Select cache → scan → select key → view detail → edit value → set TTL → delete key → purge all → export JSON                                                                    |
| ServiceBus full workflow | Add namespace → expand → open entity → peek messages → open DLQ → remove namespace                                                                                              |
| Context menu positioning | Right-click on various rows across all grids — menus appear at cursor position, not clipped                                                                                     |
| Tab persistence          | Open multiple tabs → switch between them → verify state preserved                                                                                                               |
| Production safety        | With `IsProduction` flag: verify all destructive actions require name typing                                                                                                    |

---

## Regression risks & mitigations

| Risk                                                                                  | Mitigation                                                                                              |
| ------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| YAML edit overlay JS interop breaks after DOM restructuring                           | Test `OnAfterRenderAsync` fires correctly in AksYamlViewer; verify `_initEditOverlay` flag works        |
| Context menu z-index breaks when menus stay in page but target content moves to child | Keep context menus at page root level; test right-click positioning manually                            |
| Event subscription leak in new components                                             | Each new component implementing IDisposable must have a test verifying cleanup                          |
| BL-5 re-render cascade when parent passes parameters                                  | Add render-count assertions where practical (bUnit `RenderCount` property)                              |
| State loss when YAML viewer mounts/unmounts (`@if` block) via BL-4                    | Keep `AksYamlViewer` always rendered; use `display:none` or pass `Target=null` to indicate hidden state |

---

## Acceptance criteria

- All existing bUnit tests pass (0 failures, excluding pre-existing flaky `ScheduledMessageRepositoryTests` IO file-lock)
- New bUnit tests added for each extracted component (minimum 3 tests per component)
- Each phase ships as an independent PR with clean build
- Manual regression checklist completed per phase
- AksPage < 300 lines after Phase 1
- RedisPage < 400 lines after Phase 2
- ServiceBusPage < 500 lines after Phase 3

## Validation status

- Automated: Not started
- Manual: Not started
