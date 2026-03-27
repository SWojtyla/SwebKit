# Frontend Plan — Component Decomposition

---

title: "Frontend Plan — Component Decomposition"
owner: ""
status: "Not started"

---

## Goal

Decompose AksPage, RedisPage, and ServiceBusPage into focused orchestrators backed by extracted sub-components. No behavioral changes — every user-visible feature works identically.

## Impacted areas

- `src/SwebKit.App/Components/Pages/AksPage.razor` (2,415 lines → <300)
- `src/SwebKit.App/Components/Pages/RedisPage.razor` (1,075 lines → <400)
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor` (794 lines → <500)
- `src/SwebKit.App/Components/Aks/` — new components added here
- `src/SwebKit.App/Components/Redis/` — new components added here (create directory if needed)
- `src/SwebKit.App/Components/ServiceBus/` — new component added here
- `src/SwebKit.App/Components/_Imports.razor` — verify namespaces (BL-1)
- `tests/SwebKit.App.Tests/` — new bUnit test files

## Architecture constraints

- Orchestrator pattern: data down via `[Parameter]`, events up via `EventCallback<T>` (D-003)
- JS interop only in `OnAfterRenderAsync` (BL-6)
- `StateHasChanged` via `InvokeAsync` after awaits (BL-2)
- Rethrow `OperationCanceledException` (CS-2)
- Guard in `OnParametersSetAsync` before `await` (BL-3)
- New subdirectories require `@using` in `_Imports.razor` (BL-1)

---

## Phase 1 — AksPage Decomposition

**Priority:** Critical — 2,415 lines, 79 private methods, 10 injected services

**Target:** AksPage under 300 lines — layout, data loading, panel visibility, and delegation only.

### 1.1 — Extract `AksYamlViewer.razor`

**Location:** `src/SwebKit.App/Components/Aks/AksYamlViewer.razor`

**What moves:** The entire YAML view/edit/search overlay — currently ~250 lines of markup (lines ~300-555 of AksPage) plus ~220 lines of code-behind methods.

**Markup to extract:**

- The `@if (YamlTarget is not null)` block containing: YAML view `<pre>`, search bar, edit textarea overlay, save/cancel/apply buttons, validation error display

**Methods that move to AksYamlViewer:**

| Method              | Current line | Purpose                                                 |
| ------------------- | ------------ | ------------------------------------------------------- |
| `OpenYaml`          | ~2173        | Load YAML for a resource and optionally start edit mode |
| `CloseYaml`         | ~2207        | Reset all YAML state                                    |
| `OnYamlSearchInput` | ~2222        | Handle search-in-YAML input                             |
| `ClearYamlSearch`   | ~2235        | Clear search highlights                                 |
| `OnEditYamlToggle`  | ~2244        | Switch from view to edit mode                           |
| `OnCancelYamlEdit`  | ~2253        | Cancel edit, revert to view                             |
| `OnYamlEditInput`   | ~2260        | Handle textarea input, re-highlight                     |
| `OnSaveYaml`        | ~2268        | Validate + apply YAML via client                        |
| `OnReplaceAllYaml`  | ~2309        | Find/replace all in edit text                           |
| `HighlightAsync`    | ~2150        | JS interop for YAML syntax highlighting                 |
| `ValidateYaml`      | ~2320        | Static YAML parse validation                            |
| `CountOccurrences`  | ~2333        | Static string search helper                             |

**State that moves:**

| Field                  | Type                          |
| ---------------------- | ----------------------------- |
| `YamlTarget`           | `(string Kind, string Name)?` |
| `YamlText`             | `string?`                     |
| `YamlError`            | `string?`                     |
| `YamlLoading`          | `bool`                        |
| `YamlEditMode`         | `bool`                        |
| `YamlEditText`         | `string`                      |
| `YamlApplying`         | `bool`                        |
| `_yamlHighlighted`     | `MarkupString`                |
| `_yamlEditHighlighted` | `MarkupString`                |
| `_editTextarea`        | `ElementReference`            |
| `_editPre`             | `ElementReference`            |
| `_yamlViewPre`         | `ElementReference`            |
| `_initEditOverlay`     | `bool`                        |
| `_editKey`             | `int`                         |
| `_yamlSearch`          | `string`                      |
| `_yamlSearchCount`     | `int`                         |
| `_showYamlSearch`      | `bool`                        |
| `_yamlReplace`         | `string`                      |
| `_yamlValidationError` | `string?`                     |
| `YamlIsEditable`       | `bool` (computed)             |

**Parameter interface:**

```csharp
[Parameter] public IAksClient? Client { get; set; }
[Parameter] public string Namespace { get; set; } = "default";
[Parameter] public (string Kind, string Name)? Target { get; set; }
[Parameter] public bool IsProduction { get; set; }
[Parameter] public AksConfirmBar Confirm { get; set; } = default!;
[Parameter] public EventCallback OnClose { get; set; }
[Parameter] public EventCallback<string> OnError { get; set; }
[Parameter] public INotificationService Notifications { get; set; } = default!;
```

**JS interop dependencies:** `yamlHighlight.highlight`, `yamlHighlight.initEditOverlay`, `yamlHighlight.searchInPre`, `yamlHighlight.clearSearch`

**AksPage after extraction:** Opens YAML by setting `YamlTarget` parameter on `<AksYamlViewer>`; receives close/error events via callbacks.

### 1.2 — Extract `AksHelmPanel.razor`

**Location:** `src/SwebKit.App/Components/Aks/AksHelmPanel.razor`

**What moves:** Helm history viewer, Helm values viewer, and rollback UI — currently ~120 lines of markup (the Helm History and Helm Values `@if` blocks) plus ~100 lines of code-behind.

**Markup to extract:**

- `@if (HelmHistoryTarget is not null ...)` block — history table with rollback buttons
- `@if (HelmValuesTarget is not null ...)` block — values YAML display

**Methods that move to AksHelmPanel:**

| Method                 | Current line | Purpose                                 |
| ---------------------- | ------------ | --------------------------------------- |
| `OnCtxViewHelmHistory` | ~1891        | Fetch and display helm revision history |
| `CloseHelmHistory`     | ~1909        | Reset history state                     |
| `OnCtxViewHelmValues`  | ~1915        | Fetch and display helm values YAML      |
| `CloseHelmValues`      | ~1930        | Reset values state                      |
| `OnCtxRollbackHelm`    | ~1937        | Open history in rollback mode           |
| `OnRollbackToRevision` | ~2101        | Execute rollback to a specific revision |

**State that moves:**

| Field                    | Type                     |
| ------------------------ | ------------------------ |
| `HelmHistoryTarget`      | `string?`                |
| `HelmHistory`            | `List<HelmRevisionInfo>` |
| `HelmHistoryLoading`     | `bool`                   |
| `HelmRollbackMode`       | `bool`                   |
| `HelmValuesTarget`       | `string?`                |
| `HelmValuesText`         | `string?`                |
| `HelmValuesLoading`      | `bool`                   |
| `_helmValuesHighlighted` | `MarkupString`           |

**Parameter interface:**

```csharp
[Parameter] public IAksClient? Client { get; set; }
[Parameter] public string Namespace { get; set; } = "default";
[Parameter] public string? HistoryTarget { get; set; }
[Parameter] public string? ValuesTarget { get; set; }
[Parameter] public bool RollbackMode { get; set; }
[Parameter] public bool IsProduction { get; set; }
[Parameter] public AksConfirmBar Confirm { get; set; } = default!;
[Parameter] public EventCallback OnCloseHistory { get; set; }
[Parameter] public EventCallback OnCloseValues { get; set; }
[Parameter] public EventCallback<string> OnError { get; set; }
[Parameter] public EventCallback OnDataChanged { get; set; }  // triggers parent reload after rollback
```

**JS interop dependencies:** `yamlHighlight.highlight` (for values display; shared with AksYamlViewer — `HighlightAsync` can be a shared utility or duplicated since it's 5 lines)

### 1.3 — Extract AksResourceActions (context menu action service)

**Location:** `src/SwebKit.App/Components/Aks/AksResourceActions.razor` or `AksResourceActions.cs` (service class)

**Design choice:** This is best extracted as a **code-behind helper class** rather than a component, because the actions don't have their own UI — they mutate page state and call client APIs. The page renders the context menus; this class handles what happens when an item is clicked.

**Methods that move:**

| Method                                         | Current line | Purpose                                |
| ---------------------------------------------- | ------------ | -------------------------------------- |
| `OnCtxViewYaml`                                | ~1747        | Route view-YAML by resource type       |
| `OnCtxEditYaml`                                | ~2298        | Route edit-YAML by resource type       |
| `OnCtxViewDeploymentLogs`                      | ~1764        | Open pod logs for a deployment         |
| `OnCtxViewPodLogs`                             | ~1773        | Open pod logs for a specific pod       |
| `OnCtxRestartDeployment`                       | ~1780        | Restart a deployment with confirmation |
| `OnCtxKillPod`                                 | ~1797        | Delete a pod with confirmation         |
| `OnCtxCopyHostUrl`                             | ~1818        | Copy ingress URL to clipboard          |
| `OnCtxOpenIngressUrl`                          | ~1831        | Open ingress URL in browser            |
| `OnCtxViewYamlCronJob`                         | ~1851        | Open CronJob YAML                      |
| `OnCtxScaleDeployment`                         | ~1858        | Open scale UI for deployment           |
| `OnCtxScaleStatefulSet`                        | ~1996        | Open scale UI for statefulset          |
| `OnCtxRestartStatefulSet`                      | ~1972        | Restart a statefulset                  |
| `OnCtxAllPodsLogs`                             | ~1953        | Open multi-pod logs for deployment     |
| `OnCtxAllPodsLogsStatefulSet`                  | ~1962        | Open multi-pod logs for statefulset    |
| `OnCtxContainerDetailsPod`                     | ~2008        | Open container details for pod         |
| `OnCtxContainerDetailsDeployment`              | ~2016        | Open container details for deployment  |
| `OnCtxOpenPodShell`                            | ~2033        | Open shell in pod                      |
| `OnCtxPortForward`                             | ~2045        | Open port-forward dialog               |
| `OnScaleConfirm`                               | ~1868        | Execute scale                          |
| `OnScaleCancel`                                | ~1890        | Cancel scale                           |
| `ShowDeploymentMenu` through `ShowCronJobMenu` | ~1720-1760   | Context menu show helpers (8 methods)  |
| `CloseAllMenus`                                | ~2142        | Close all context menus                |
| `HandleLetterActionAsync`                      | ~1583        | Keyboard shortcut routing              |
| `SelectRelative`                               | ~1488        | Grid keyboard navigation               |
| `ClearSelection`                               | ~1545        | Clear all selection state              |
| `PushAksSelection`                             | ~1532        | Push selection to ISelectionContext    |
| `OpenUrlAsync`                                 | ~1846        | Open URL via MAUI launcher             |
| `BuildIngressUrl`                              | ~1838        | URL builder helper                     |
| `CopyToClipboardAsync`                         | ~2345        | Clipboard write                        |
| `TruncatePodName`                              | ~2351        | Static display helper                  |

**Pattern:** The page instantiates this class (or uses it as a nested component with `@ref`) and delegates action execution. The class uses callbacks or direct method returns to communicate state changes (e.g., "open YAML for Deployment X", "set LogPodName to Y").

**Alternative simpler approach:** Keep the context menu handlers in AksPage as thin one-line delegates that call into the child components directly. This avoids a new abstraction. **Recommend evaluating during implementation** — if after extracting AksYamlViewer and AksHelmPanel the page is already under 400 lines, the resource actions may stay in the orchestrator.

### 1.4 — Extract `AksConnectionBar.razor`

**Location:** `src/SwebKit.App/Components/Aks/AksConnectionBar.razor`

**What moves:** The cluster connection header — context dropdown, namespace searchable picker, connection status dot, events/sessions toggle buttons.

**Markup to extract:** Lines ~15-90 of AksPage — the entire `<div class="aks-toolbar-row">` first row containing:

- Connection dot
- Context `<select>` dropdown
- Namespace searchable picker (with `NsDropOpen`, `NsSearchText`, `NsFiltered`)
- Events toggle button
- Port-forward sessions toggle button
- Auto-refresh toggle
- Refresh button

**Methods that move to AksConnectionBar:**

| Method             | Current line | Purpose                  |
| ------------------ | ------------ | ------------------------ |
| `OpenNsDrop`       | ~981         | Open namespace dropdown  |
| `CloseNsDrop`      | ~988         | Close namespace dropdown |
| `OnNsSearchInput`  | ~995         | Filter namespace search  |
| `SelectNs`         | ~1001        | Select a namespace       |
| `OnContextChanged` | ~1424        | Handle context switch    |

**State that moves:**

| Field          | Type                             |
| -------------- | -------------------------------- |
| `NsDropOpen`   | `bool`                           |
| `NsSearchText` | `string`                         |
| `NsFiltered`   | `IEnumerable<string>` (computed) |

**Parameter interface:**

```csharp
[Parameter] public IAksClient? Client { get; set; }
[Parameter] public List<KubeContextInfo> Contexts { get; set; } = [];
[Parameter] public List<string> Namespaces { get; set; } = ["default"];
[Parameter] public string ActiveContext { get; set; } = "";
[Parameter] public string CurrentNamespace { get; set; } = "default";
[Parameter] public bool IsLoading { get; set; }
[Parameter] public bool HasAnyPanel { get; set; }
[Parameter] public int EventWarningCount { get; set; }
[Parameter] public int ActivePortForwardCount { get; set; }
[Parameter] public bool ShowEvents { get; set; }
[Parameter] public bool ShowPortForwardSessions { get; set; }
[Parameter] public EventCallback<string> OnContextChanged { get; set; }
[Parameter] public EventCallback<string> OnNamespaceChanged { get; set; }
[Parameter] public EventCallback OnRefresh { get; set; }
[Parameter] public EventCallback<bool> OnToggleEvents { get; set; }
[Parameter] public EventCallback<bool> OnTogglePortForwardSessions { get; set; }
```

### Post-Phase 1 AksPage structure

After all four extractions, AksPage.razor should contain:

1. **Injected services** (~12 lines)
2. **Resource type tabs** — the tab bar selecting Deployments/Pods/etc.
3. **Resource filter bar** — delegates to `ResourceFilter` (already extracted)
4. **Grid area** — renders the appropriate `*Grid` component per resource type (already extracted)
5. **Side panel area** — conditionally renders:
   - `<AksYamlViewer>`, `<AksHelmPanel>`, `<PodLogView>`, `<MultiPodLogView>`, `<ContainerDetailPanel>`, `<ConfigMapDetailPanel>`, `<SecretDetailPanel>`, `<HpaPanel>`, `<PortForwardSessionsPanel>` — all already separate components or newly extracted
6. **Context menus** — 8 `<ContextMenu>` components (keep in page — they're declarative markup with thin click handlers)
7. **Data loading** — `LoadAsync()`, `LoadContextsAsync()`, `LoadNamespacesAsync()`, `ConnectAndLoadAsync()` — core orchestration stays
8. **Panel visibility flags** — booleans controlling which panel is shown
9. **Dispose** — cleanup

**Estimated AksPage size after Phase 1:** ~250-300 lines (markup ~120, code ~150).

---

## Phase 2 — RedisPage Decomposition

**Priority:** Moderate — 1,075 lines, 34 private methods, 6 injected services

**Target:** RedisPage under 400 lines.

### 2.1 — Extract `RedisConnectionBar.razor`

**Location:** `src/SwebKit.App/Components/Redis/RedisConnectionBar.razor` (create directory)

**What moves:** The cache selector dropdown, database picker — lines ~22-40 of RedisPage markup.

**Markup:** The `<select class="cache-selector">` with options loop and the connected DB label.

**Methods that move:**

| Method                | Purpose                                     |
| --------------------- | ------------------------------------------- |
| `OnCacheChangedAsync` | Handle cache selection change and reconnect |
| `ConnectAsync`        | Establish Redis client connection           |
| `ConnectAndScanAsync` | Connect then initial scan                   |

**State that moves:** `SelectedCacheId`, `_loadedCacheId`, `Client`, connection label computation.

**Parameter interface:**

```csharp
[Parameter] public AppStateService AppState { get; set; } = default!;
[Parameter] public EventCallback<IRedisClient?> OnClientChanged { get; set; }
[Parameter] public EventCallback<string> OnError { get; set; }
```

### 2.2 — Extract `RedisToolbar.razor`

**Location:** `src/SwebKit.App/Components/Redis/RedisToolbar.razor`

**What moves:** The action button row — Scan, Refresh Key, Delete Key, Purge All, multi-select toggle, Export JSON, AutoRefreshToggle.

**Markup:** Lines ~50-80 of RedisPage — the toolbar `<div>` with all `<FluentButton>` elements.

**Methods that move:**

| Method                    | Purpose                             |
| ------------------------- | ----------------------------------- |
| `DeleteSelectedKeyAsync`  | Delete with confirmation            |
| `DeleteSelectedKeysAsync` | Batch delete selected keys          |
| `PurgeAllAsync`           | Purge all keys with confirmation    |
| `ExportKeysToJsonAsync`   | Export keys to JSON file            |
| Multi-select toggle logic | `_multiSelectMode`, `_selectedKeys` |

**Parameter interface:**

```csharp
[Parameter] public bool IsLoading { get; set; }
[Parameter] public string? SelectedKey { get; set; }
[Parameter] public int KeyCount { get; set; }
[Parameter] public bool MultiSelectMode { get; set; }
[Parameter] public int SelectedKeyCount { get; set; }
[Parameter] public EventCallback OnScan { get; set; }
[Parameter] public EventCallback OnRefreshKey { get; set; }
[Parameter] public EventCallback OnDeleteKey { get; set; }
[Parameter] public EventCallback OnDeleteSelectedKeys { get; set; }
[Parameter] public EventCallback OnPurgeAll { get; set; }
[Parameter] public EventCallback OnExportJson { get; set; }
[Parameter] public EventCallback<bool> OnMultiSelectToggle { get; set; }
```

### Post-Phase 2 RedisPage structure

RedisPage becomes: connection bar → toolbar → split panel (already uses `RedisNamespaceTree`, `RedisKeyList`, `RedisKeyDetail`, `RedisServerInfo`, `RedisPrefixMemory`) → confirm dialog. Orchestrator owns data loading (`ScanAsync`, `LoadMoreKeysAsync`), key selection, key detail fetching.

**Estimated RedisPage size after Phase 2:** ~350-400 lines.

---

## Phase 3 — ServiceBusPage Cleanup

**Priority:** Low — 794 lines, 24 private methods, already well-decomposed

**Target:** ServiceBusPage under 500 lines.

### 3.1 — Extract `ServiceBusNamespacePanel.razor`

**Location:** `src/SwebKit.App/Components/ServiceBus/ServiceBusNamespacePanel.razor`

**What moves:** The left namespace sidebar — namespace list, expand/collapse, add namespace form, remove namespace.

**Markup to extract:** Lines ~12-130 of ServiceBusPage — the entire left pane containing:

- Namespace list with expand/collapse
- Connection status per namespace
- Entity tree per namespace (delegates to existing `EntityTree` component)
- Add namespace form (connection string input, validation)
- Remove namespace button

**Methods that move:**

| Method                           | Current line | Purpose                                 |
| -------------------------------- | ------------ | --------------------------------------- |
| `ToggleAddForm`                  | ~505         | Show/hide add form                      |
| `CancelAdd`                      | ~513         | Cancel add form                         |
| `AddNamespaceAsync`              | ~536         | Parse connection string, add namespace  |
| `RemoveNamespaceAsync`           | ~615         | Remove namespace and close related tabs |
| `ExpandNamespaceAsync`           | ~529         | Expand a namespace section              |
| `ToggleNamespace`                | ~638         | Toggle expand/collapse                  |
| `SetNamespacePaneCollapsedAsync` | ~521         | Collapse/expand entire pane             |

**State that moves:** `_showAddForm`, `_addConnStr`, `_addError`, `_showConnStr`.

**Parameter interface:**

```csharp
[Parameter] public List<NsState> NamespaceStates { get; set; } = [];
[Parameter] public bool IsCollapsed { get; set; }
[Parameter] public IReadOnlyList<SbEntityLink> LinkedEntities { get; set; } = [];
[Parameter] public EventCallback<bool> OnCollapsedChanged { get; set; }
[Parameter] public EventCallback<(NsState ns, SbEntityInfo entity, bool isDlq)> OnEntityTabOpen { get; set; }
[Parameter] public EventCallback<NsState> OnScheduledTabOpen { get; set; }
[Parameter] public EventCallback<SbEntityLink> OnEntityLinkToggled { get; set; }
[Parameter] public EventCallback<NsState> OnNamespaceExpand { get; set; }
[Parameter] public EventCallback<ServiceBusNamespace> OnNamespaceAdded { get; set; }
[Parameter] public EventCallback<Guid> OnNamespaceRemoved { get; set; }
```

**Note:** `NsState` and `SbTab` inner classes should be promoted to standalone types in a shared location (e.g., `Components/ServiceBus/ServiceBusTypes.cs`) so both the page and the namespace panel can use them.

### Post-Phase 3 ServiceBusPage structure

ServiceBusPage becomes: namespace panel → tab bar → tab content area (delegates to `MessageListView`, `DlqView`, `ScheduledMessages`, `MessageComposer`, `MessageDetailPane`). Orchestrator owns tab management, client lifecycle, and event routing.

**Estimated ServiceBusPage size after Phase 3:** ~450-500 lines.

---

## Implementation sequence per extraction

For each component extraction, follow this order:

1. **Create the new .razor file** with parameter interface and empty render
2. **Move markup** from the page to the new component
3. **Move code-behind methods and state** — update field references to use parameters/callbacks
4. **Update the page** — replace inline markup with `<NewComponent ... />` tag
5. **Verify `_Imports.razor`** has the namespace (BL-1)
6. **Run existing bUnit tests** — all must pass (no regressions)
7. **Add new bUnit tests** for the extracted component
8. **Manual smoke test** — verify full workflow on the page

## Validation

- Existing bUnit tests: must remain green after each extraction
- New component tests: required for each extracted component
- Manual UX checks: see [test-plan.md](test-plan.md)

## Notes

- Context menus stay in AksPage for Phase 1 — they're thin declarative markup and moving them risks z-index/positioning regressions
- `HighlightAsync` (YAML syntax highlight) is used by both AksYamlViewer and AksHelmPanel — extract as a shared static helper or duplicate the 5-line method
- The `AksPageSnapshot` record and `PageDataCache` integration stay in AksPage — they're orchestration concerns
- Filter state (`DeploymentFilter`, `PodFilter`, etc.) and `Filtered*` computed properties stay in AksPage since they feed the already-extracted grid components
- Phase 1 task 1.3 (AksResourceActions) should be evaluated after 1.1 and 1.2 — if the page is already <400 lines, the action handlers may stay
