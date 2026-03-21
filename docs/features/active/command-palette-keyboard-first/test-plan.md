# Test Plan — Command Palette & Keyboard-First Navigation

## Validation strategy

Unit tests for fuzzy search and availability predicate logic. Manual keyboard-only walkthroughs for all interaction flows.

## Unit tests

- Fuzzy search: "rb dep" matches "Restart deployment"
- Fuzzy search: exact match ranked above partial match
- `CommandRegistry.GetAvailable` filters commands where `IsAvailable()` returns false
- Recent commands: last 5 commands returned in reverse execution order
- Recent commands: persisted to and loaded from `UiStateRepository`

## Main scenarios

### Command palette — general

| Scenario | Expected |
|---|---|
| `Ctrl+P` pressed | Palette opens; input focused |
| No input | "Recent" section shows last 5 commands |
| User types | Fuzzy-filtered results shown; section headers visible |
| `↑` / `↓` | Moves selection through results |
| `Enter` | Executes selected command; palette closes |
| `Escape` | Palette closes; focus returns to trigger element |
| Shortcut column | Keyboard shortcut shown right-aligned for each command |

### Context-aware commands

| Scenario | Expected |
|---|---|
| No deployment selected in AKS | "Restart deployment" not shown in palette |
| Deployment selected | "Restart deployment" shown and executable |
| User switches to a different page | Commands from previous area not shown |

### Shortcut reference sheet

| Scenario | Expected |
|---|---|
| `?` pressed (outside input) | Shortcut reference panel opens |
| "Keyboard shortcuts" command executed | Same panel opens |
| Panel lists all registered shortcuts | Grouped by area; correct shortcut strings shown |

### Grid keyboard navigation

| Scenario | Expected |
|---|---|
| Grid focused, `↓` pressed | Next row selected |
| Row selected, `Enter` pressed | Detail panel opens for selected item |
| Detail panel open, `Escape` pressed | Detail panel closes; grid regains focus |
| Row selected, `Delete` pressed (destructive) | Confirmation dialog shown |
| Input field focused, `Delete` pressed | No destructive action (input takes priority) |

### Focus trap — modals

| Scenario | Expected |
|---|---|
| `ConfirmDialog` open, `Tab` pressed | Focus cycles within dialog only |
| `Modal` open, `Escape` pressed | Modal closes; focus returns to trigger element |
| Command palette open, click outside | Palette closes |

### Skip-to-content

| Scenario | Expected |
|---|---|
| App opens, user presses `Tab` once | "Skip to main content" link becomes visible |
| User presses `Enter` | Focus jumps to main content area, skipping left nav |

## Regression risks

- `FluentDataGrid` built-in keyboard behaviour must not conflict with custom `↑`/`↓` handlers
- Focus restore on panel close must survive Blazor re-renders that may replace the DOM element
- Shortcut guard (`event.target.tagName !== 'INPUT'`) must be applied to all grid shortcuts

## Manual keyboard-only walkthrough checklist

- [ ] Navigate to every feature area using Alt+1–5
- [ ] Open command palette, search, execute a command
- [ ] Navigate a grid with ↑/↓, open detail panel with Enter, close with Escape
- [ ] Trigger a destructive action from grid (Delete key), confirm with keyboard
- [ ] Open and close every modal/dialog using only keyboard
- [ ] Tab through all settings form fields in order
- [ ] Open shortcut reference sheet with `?`

## Acceptance criteria

- Every feature reachable without mouse
- Command palette shows recent commands and full fuzzy search
- Context-aware commands correctly filtered by current selection
- All modals and panels have working focus traps
- Keyboard-only walkthrough completes without dead-ends
