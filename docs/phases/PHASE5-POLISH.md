# Phase 5 — Polish & Advanced

**Status:** ⏳ Pending (starts after Phase 4 complete)
**Goal:** Production-quality UX — full command palette, reorderable tabs, notification center,
config portability, full keyboard audit, cross-platform testing, and performance profiling.

---

## 1. Full Fuzzy Command Palette

**Component:** `Components/Shared/CommandPalette.razor` (major enhancement from Phase 1 basic)

- [ ] Fuzzy search algorithm: score all commands against input, rank by relevance
  - Simple implementation: Levenshtein distance or substring score
  - Score boost for: exact prefix match > word-start match > any substring
- [ ] Complete command registry (all commands from the plan):

  **Project & Navigation:**
  - Switch project: [fuzzy project names]
  - Switch to Dev / Test / Acc / Prod
  - Go to Service Bus / Observability / AKS / Projects / Settings

  **Service Bus:**
  - Open queue: [fuzzy queue names from current env]
  - Open DLQ: [queue name]
  - Open topic/subscription: [name]
  - Send message to: [entity name]
  - Send template: [template name]

  **Observability:**
  - Run query: [saved query name]
  - Find logs for correlation ID... (opens input prompt)
  - Find trace for operation ID... (opens input prompt)
  - Open metrics dashboard

  **AKS:**
  - Tail logs: [deployment name from current env]
  - Describe pod: [pod name]
  - Port-forward: [service/deployment name]
  - Open shell in: [pod name]
  - Refresh workload overview

- [ ] Recently used commands bubble to top (stored in `ui-state.json`)
- [ ] Keyboard navigation: arrow up/down, Enter to execute, Escape to close
- [ ] Command icons: small colored icon per command category
- [ ] Sub-commands: some commands open a second-level picker (e.g., "Open queue" → list of queues)
- [ ] Commands dynamically sourced from current project+env (queue names fetched, deployment names fetched)

---

## 2. Reorderable / Dockable Tabs

- [ ] Tab drag-to-reorder: CSS drag API within `TabPanel.razor`
  - `draggable="true"` on tab headers
  - `ondragover` + `ondrop` to reorder `TabService` list
- [ ] Tab context menu (right-click):
  - Close
  - Close others
  - Close to the right
  - Pin (pinned tabs can't be closed with Ctrl+W, shown with pin icon)
  - Rename (for user-label override)
- [ ] Overflow: when >8 tabs, show overflow dropdown `[+2 ▾]` listing hidden tabs
- [ ] Tab state persisted in `ui-state.json`: reopen same tabs on restart with last-used entity/filter

---

## 3. Notifications System

**Service:** `Services/NotificationService.cs`

- [ ] Toast notifications (Fluent UI `FluentToast`):
  - Success (green): "47 messages resubmitted"
  - Info (blue): "Port-forward started: localhost:8080 → order-api:80"
  - Warning (orange): "Pod restarted: order-api-7d9f4-abc"
  - Error (red): "Query failed: authentication error"
- [ ] Toast duration: 4s for success/info, 8s for warning/error (with X button to dismiss early)
- [ ] Notification center (bell icon in top bar):
  - Badge: unread count
  - Click → flyout panel with history (last 50 notifications)
  - Each entry: icon, message, timestamp, optional action button
  - "Clear all" button
  - Notifications persisted for current session only (`ui-state.json` cleared on restart)

---

## 4. Import / Export Project Configurations

- [ ] **Export:** Settings → Projects → `[Export config...]`
  - Generates `swebkit-[project-name].json` with Project + Environments (excluding secrets)
  - Includes: Service Bus namespace, App Insights workspace IDs, kubeconfig context names, saved queries, templates
  - Downloaded via `Microsoft.Maui.Storage.FilePicker.PickAsync` equivalent (save dialog)
- [ ] **Import:** Settings → Projects → `[Import config...]`
  - User picks a `.json` file
  - Validates schema version
  - If project name already exists: "Overwrite? [Yes - merge] [Yes - replace] [Cancel]"
  - After import: prompts user to enter secrets for each credential ref
- [ ] Use case: share project config with teammates (commit the JSON to the project repo)

---

## 5. Keyboard Shortcut Audit

Verify all 19 planned shortcuts work correctly across every page:

- [ ] `Ctrl+P` — command palette opens from any page
- [ ] `Ctrl+1` / `Ctrl+2` / `Ctrl+3` / `Ctrl+4` / `Ctrl+,` — navigation
- [ ] `Alt+1` / `Alt+2` / `Alt+3` / `Alt+4` — environment switch
- [ ] `Alt+Shift+P` — project selector focus
- [ ] `Ctrl+Tab` / `Ctrl+Shift+Tab` — tab navigation
- [ ] `Ctrl+W` — close tab (not if pinned)
- [ ] `Ctrl+\` — toggle details pane
- [ ] `F5` — refresh current view
- [ ] `Ctrl+Enter` — execute query / send message
- [ ] `Ctrl+F` — focus filter bar search input
- [ ] `Ctrl+Shift+C` — copy selected items as JSON
- [ ] `Alt+D` — open DLQ for current Service Bus entity
- [ ] `Ctrl+Shift+L` — open log view for current context
- [ ] `Escape` — close modal / clear filter / cancel operation
- [ ] Audit: no shortcut conflicts across pages; shortcuts work when focus is inside Monaco Editor

---

## 6. Full Dark / Light Theme

- [ ] Theme toggle in user settings (and top bar quick toggle)
- [ ] CSS variables approach: `--color-bg`, `--color-surface`, `--color-text`, `--color-border`, `--env-color`
- [ ] Fluent UI Blazor: set `ThemeMode.Dark` / `ThemeMode.Light` via `FluentDesignTheme`
- [ ] Monaco Editor: set theme to `vs-dark` / `vs` matching app theme
- [ ] xterm.js: set theme matching app
- [ ] Charts (ApexCharts): set chart theme matching app
- [ ] Persist choice in `user-settings.json`

---

## 7. macOS / Linux MAUI Testing

- [ ] Run app on macOS (MAUI Catalyst) — identify build and runtime blockers
- [ ] Key blockers likely:
  - `WindowsCredentialStore` → must use `ICredentialStore` abstraction with macOS Keychain implementation
  - `kubectl` path resolution (different on macOS/Linux)
  - Windows Terminal → not available; always use embedded xterm.js terminal
  - `kubectl port-forward` process management: process group handling differs on Unix
- [ ] Implement `MacOsCredentialStore.cs` using `Security.framework` via P/Invoke or a wrapper lib
- [ ] Implement `LinuxCredentialStore.cs` using `libsecret` (SecretService API)
- [ ] Document remaining platform differences in `docs/PLATFORM-NOTES.md`

---

## 8. Performance Profiling

- [ ] Profiling scenario 1: Peek 500 SB messages → render in DataTable → measure first-render time
  - Target: < 300ms to first render after data loads
- [ ] Profiling scenario 2: Stream 10,000 log lines into `PodLogView` ring buffer
  - Target: < 50ms per batch UI update, no visible stutter
- [ ] Profiling scenario 3: Log table with 500 rows — scroll performance
  - Target: 60fps scroll with `FluentDataGrid` virtualization enabled
- [ ] Profiling scenario 4: Command palette fuzzy search with 200 commands
  - Target: < 16ms per keystroke filter
- [ ] Tools: .NET Diagnostics (`dotnet-trace`, `dotnet-counters`), browser DevTools on WebView2
- [ ] Fix any identified bottlenecks before marking Phase 5 complete

---

## 9. Settings Page Completion

- [ ] Global preferences:
  - Theme (light/dark/system)
  - Default time range for log queries
  - Default max rows for peek operations
  - Auto-refresh default interval
  - Keyboard shortcut overrides (table of shortcut → key binding, editable)
- [ ] Credential management:
  - List all stored credential keys (not values) per environment
  - "Clear credential" button per key
  - "Re-enter credential" button (opens secure input dialog)
- [ ] About page: version, licenses, GitHub link

---

## Acceptance Criteria (Phase 5 Complete)

- [ ] Command palette fuzzy-searches across all command categories in < 16ms per keystroke
- [ ] Drag 3 tabs to reorder → order preserved after app restart
- [ ] Pin a tab → Ctrl+W does not close it
- [ ] Export project config → share file → import on clean install → all queues/queries restored (secrets re-entered)
- [ ] All 19 keyboard shortcuts verified working on Service Bus, Observability, and AKS pages
- [ ] Light/dark theme switch applies consistently across all components including Monaco and xterm
- [ ] App builds and runs on macOS (with known limitations documented)
- [ ] Profiling: 500 SB messages first-render < 300ms, 10k log lines no visible stutter
