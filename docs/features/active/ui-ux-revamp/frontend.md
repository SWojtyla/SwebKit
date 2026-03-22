# Frontend Plan — ui-ux-revamp

---

title: "Frontend Plan — ui-ux-revamp"
owner: ""
status: "Not started"

---

## Goal

Modernize the SwebKit visual design across six phased workstreams: theme infrastructure, dark-theme polish, light-theme introduction, dashboard redesign, per-page inline-style cleanup, and a global style token pass. A developer completing these phases in order will produce an app that is visually polished, themeable, and uses a consistent design language throughout.

---

## Impacted Areas

| Area          | Files                                                             |
| ------------- | ----------------------------------------------------------------- |
| Design tokens | `src/SwebKit.App/wwwroot/app.css`                                 |
| Layout shell  | `Components/Layout/MainLayout.razor`, `MainLayout.razor.css`      |
| Top bar       | `Components/Layout/TopBar.razor`                                  |
| Left nav      | `Components/Layout/LeftNav.razor`, `NavItem.razor`                |
| Status bar    | `Components/Layout/StatusBar.razor`                               |
| Settings page | `Components/Pages/SettingsPage.razor`                             |
| Dashboard     | `Components/Pages/DashboardPage.razor`, `DashboardPage.razor.css` |
| Releases      | `Components/Pages/ReleasesPage.razor`                             |
| Service Bus   | `Components/Pages/ServiceBusPage.razor`                           |
| AKS           | `Components/Pages/AksPage.razor`, `AksPage.razor.css`             |
| Redis         | `Components/Pages/RedisPage.razor`, `RedisPage.razor.css`         |
| Storage       | `Components/Pages/StoragePage.razor`                              |
| JS interop    | `src/SwebKit.App/wwwroot/app.js` (theme init snippet)             |

---

## A. Theme Architecture

### Design Principle

The theme system uses a **`data-theme` attribute** on the root `<div class="app-shell">` element (or on `<body>`). CSS variable blocks are declared per theme value so both themes live in `app.css` with zero JavaScript manipulation of individual properties. `FluentDesignTheme` is told which Fluent mode to activate to keep Fluent component colors in sync.

### CSS Structure

Replace the current flat `:root` block with theme-aware blocks:

```css
/* ── Defaults (dark, always applies unless overridden) ── */
:root {
  /* layout and z-index constants — not theme-specific */
  --nav-width: 240px;
  --nav-collapsed-width: 56px;
  --status-bar-height: 28px;
  --top-bar-height: 48px;
  --z-dropdown: 200;
  --z-modal: 500;
  --z-toast: 900;
  --z-overlay: 1000;

  /* spacing scale */
  --spacing-xs: 4px;
  --spacing-sm: 8px;
  --spacing-md: 12px;
  --spacing-lg: 16px;
  --spacing-xl: 24px;

  /* typography scale */
  --font-size-xs: 10px;
  --font-size-sm: 12px;
  --font-size-md: 14px; /* bumped from 13px */
  --font-size-lg: 15px;
  --font-size-xl: 18px;

  /* radius scale (new) */
  --radius-sm: 4px;
  --radius-md: 8px;
  --radius-lg: 12px;
  --radius-xl: 16px;

  /* shadow scale (new) */
  --shadow-sm: 0 1px 3px rgba(0, 0, 0, 0.35);
  --shadow-md: 0 4px 12px rgba(0, 0, 0, 0.45);
  --shadow-lg: 0 8px 32px rgba(0, 0, 0, 0.55);
}

/* ── Dark theme ── */
[data-theme='dark'] {
  --env-color: #1e1e2e;
  --env-text: #e0e0f0;
  --color-bg: #13131f;
  --color-surface: #1e1e2e;
  --color-surface-2: #252535;
  --color-surface-3: #2c2c3e; /* new: for top bar / status bar differentiation */
  --color-border: #383850;
  --color-text: #e0e0f0;
  --color-text-muted: #9090a8;
  --color-text-faint: #60607a; /* new: disabled/placeholder text */
  --color-accent: #0078d4;
  --color-accent-hover: #1a8fe3;
  --color-accent-subtle: rgba(0, 120, 212, 0.12);
  --color-error: #e53935;
  --color-warning: #fb8c00;
  --color-success: #43a047;
  --color-prod: #c8002a;
  /* per-feature accent colors */
  --color-nav-dashboard: #4f8fe0; /* blue-indigo */
  --color-nav-servicebus: #e8904a; /* orange-amber */
  --color-nav-aks: #34b4c8; /* sky-teal */
  --color-nav-redis: #e05560; /* red-coral */
  --color-nav-storage: #9e68d4; /* purple-violet */
  --color-nav-releases: #4ab87a; /* green-emerald */
  --color-nav-settings: #8a8aaa; /* gray-slate */
}

/* ── Light theme: Azure Bloom ── */
[data-theme='light-azure-bloom'] {
  --env-color: #eef4fb;
  --env-text: #1a2b40;
  --color-bg: #f5f8fc;
  --color-surface: #ffffff;
  --color-surface-2: #eef4fb;
  --color-surface-3: #e4edf7;
  --color-border: #c8d8ea;
  --color-text: #1a2b40;
  --color-text-muted: #5a7090;
  --color-text-faint: #8aaac0;
  --color-accent: #0078d4;
  --color-accent-hover: #005ea6;
  --color-accent-subtle: rgba(0, 120, 212, 0.1);
  --color-error: #c62828;
  --color-warning: #e65100;
  --color-success: #2e7d32;
  --color-prod: #b71c1c;
  --color-nav-dashboard: #1565c0;
  --color-nav-servicebus: #bf5000;
  --color-nav-aks: #006978;
  --color-nav-redis: #c62828;
  --color-nav-storage: #6a1b9a;
  --color-nav-releases: #1b5e20;
  --color-nav-settings: #455a64;
  --shadow-sm: 0 1px 3px rgba(0, 0, 0, 0.1);
  --shadow-md: 0 4px 12px rgba(0, 0, 0, 0.14);
  --shadow-lg: 0 8px 32px rgba(0, 0, 0, 0.18);
}

/* ── Light theme: Coral Studio ── */
[data-theme='light-coral-studio'] {
  --env-color: #eee0d4;
  --env-text: #2d1a10;
  --color-bg: #fbf7f4;
  --color-surface: #ffffff;
  --color-surface-2: #f5ede6;
  --color-surface-3: #eee0d4;
  --color-border: #dcc8ba;
  --color-text: #2d1a10;
  --color-text-muted: #7a5040;
  --color-text-faint: #a87860;
  --color-accent: #d45000;
  --color-accent-hover: #a83c00;
  --color-accent-subtle: rgba(212, 80, 0, 0.1);
  --color-error: #b02020;
  --color-warning: #c05000;
  --color-success: #2d6e30;
  --color-prod: #8b0000;
  --color-nav-dashboard: #1b5ea8;
  --color-nav-servicebus: #d45000;
  --color-nav-aks: #00787a;
  --color-nav-redis: #b02020;
  --color-nav-storage: #7030a0;
  --color-nav-releases: #2d6e30;
  --color-nav-settings: #606060;
  --shadow-sm: 0 1px 3px rgba(0, 0, 0, 0.1);
  --shadow-md: 0 4px 12px rgba(0, 0, 0, 0.14);
  --shadow-lg: 0 8px 32px rgba(0, 0, 0, 0.18);
}

/* ── Light theme: Forest Dev ── */
[data-theme='light-forest-dev'] {
  --env-color: #dceadc;
  --env-text: #1a2d1a;
  --color-bg: #f2f6f2;
  --color-surface: #ffffff;
  --color-surface-2: #e8f0e8;
  --color-surface-3: #dceadc;
  --color-border: #b8d0b8;
  --color-text: #1a2d1a;
  --color-text-muted: #4a7050;
  --color-text-faint: #789878;
  --color-accent: #2e7d32;
  --color-accent-hover: #1b5e20;
  --color-accent-subtle: rgba(46, 125, 50, 0.1);
  --color-error: #c62828;
  --color-warning: #e65100;
  --color-success: #2e7d32;
  --color-prod: #b71c1c;
  --color-nav-dashboard: #1565c0;
  --color-nav-servicebus: #bf5000;
  --color-nav-aks: #00838f;
  --color-nav-redis: #c62828;
  --color-nav-storage: #6a1b9a;
  --color-nav-releases: #2e7d32;
  --color-nav-settings: #546e7a;
  --shadow-sm: 0 1px 3px rgba(0, 0, 0, 0.1);
  --shadow-md: 0 4px 12px rgba(0, 0, 0, 0.14);
  --shadow-lg: 0 8px 32px rgba(0, 0, 0, 0.18);
}

/* ── Light theme: Violet Cloud ── */
[data-theme='light-violet-cloud'] {
  --env-color: #e2daef;
  --env-text: #1e1a30;
  --color-bg: #f5f3fb;
  --color-surface: #ffffff;
  --color-surface-2: #ede8f8;
  --color-surface-3: #e2daef;
  --color-border: #c5b8e0;
  --color-text: #1e1a30;
  --color-text-muted: #6050a0;
  --color-text-faint: #9080c0;
  --color-accent: #6030c8;
  --color-accent-hover: #4020a0;
  --color-accent-subtle: rgba(96, 48, 200, 0.1);
  --color-error: #b83030;
  --color-warning: #c06020;
  --color-success: #2d7030;
  --color-prod: #8b0000;
  --color-nav-dashboard: #1565c0;
  --color-nav-servicebus: #c06020;
  --color-nav-aks: #006080;
  --color-nav-redis: #b83030;
  --color-nav-storage: #6030c8;
  --color-nav-releases: #2d7030;
  --color-nav-settings: #607080;
  --shadow-sm: 0 1px 3px rgba(0, 0, 0, 0.1);
  --shadow-md: 0 4px 12px rgba(0, 0, 0, 0.14);
  --shadow-lg: 0 8px 32px rgba(0, 0, 0, 0.18);
}
```

### Theme Switching in MainLayout

Add a `_currentTheme` field to `MainLayout.razor` and a JS snippet to persist and apply the theme:

```csharp
// in MainLayout.razor @code
private string _currentTheme = "dark";

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (!firstRender) return;
    // ... existing firstRender code ...
    var stored = await JS.InvokeAsync<string?>("localStorage.getItem", "swebkit-ui-theme");
    _currentTheme = stored ?? "dark";
    await JS.InvokeVoidAsync("eval",
        $"document.querySelector('.app-shell').setAttribute('data-theme', '{_currentTheme}')");
    await InvokeAsync(StateHasChanged);
}

public async Task SetThemeAsync(string theme)
{
    _currentTheme = theme;
    await JS.InvokeVoidAsync("localStorage.setItem", "swebkit-ui-theme", theme);
    await JS.InvokeVoidAsync("eval",
        $"document.querySelector('.app-shell').setAttribute('data-theme', '{theme}')");
    await InvokeAsync(StateHasChanged);
}
```

Expose `_currentTheme` as a cascading value so `SettingsPage` can read the current theme and the `FluentDesignTheme` component can receive the correct mode:

```razor
<!-- MainLayout.razor template -->
<FluentDesignTheme StorageName="swebkit-theme"
                   Mode="@(_currentTheme == "dark" ? DesignThemeModes.Dark : DesignThemeModes.Light)" />
<CascadingValue Value="this" Name="Layout">
  <CascadingValue Value="AppState">
    ...
  </CascadingValue>
</CascadingValue>
```

**Note (BL-6):** The `data-theme` attribute write and `localStorage` read must happen in `OnAfterRenderAsync(firstRender)`. Calling JS in `OnInitializedAsync` will fail in MAUI Blazor Hybrid.

### Theme Preference Storage

Use `localStorage` directly via `IJSRuntime`. Do **not** extend `UiStateRepository` or `AppConfig`/`profiles.json` — appearance is a local device preference, not a profile setting. Key: `"swebkit-ui-theme"`. Values: `"dark"` | `"light-azure-bloom"` | `"light-coral-studio"` | `"light-forest-dev"` | `"light-violet-cloud"`.

---

## B. Color Palettes — All Four Supported

**Decision:** All four palettes are implemented as selectable themes. The `data-theme` values are `light-azure-bloom`, `light-coral-studio`, `light-forest-dev`, and `light-violet-cloud`. Users pick any of the five options (dark + four light) from the Appearance settings.

---

### Palette 1 — "Azure Bloom"

**Personality:** Airy, professional, Microsoft-native feel. Whites and blues, clean like Azure Portal.

| Variable                   | Value     | Notes                      |
| -------------------------- | --------- | -------------------------- |
| `--color-bg`               | `#F5F8FC` | Slightly cool white        |
| `--color-surface`          | `#FFFFFF` | Pure white cards           |
| `--color-surface-2`        | `#EEF4FB` | Very light blue-grey       |
| `--color-surface-3`        | `#E4EDF7` | Top bar / status bar       |
| `--color-border`           | `#C8D8EA` | Soft blue-grey border      |
| `--color-text`             | `#1A2B40` | Deep navy text             |
| `--color-text-muted`       | `#5A7090` | Medium blue-grey           |
| `--color-accent`           | `#0078D4` | Microsoft blue (unchanged) |
| `--color-accent-secondary` | `#005EA6` | Darker blue for hover      |
| Nav: Dashboard             | `#1565C0` |                            |
| Nav: Service Bus           | `#BF5000` |                            |
| Nav: AKS                   | `#006978` |                            |
| Nav: Redis                 | `#C62828` |                            |
| Nav: Storage               | `#6A1B9A` |                            |
| Nav: Releases              | `#1B5E20` |                            |
| Nav: Settings              | `#455A64` |                            |

**Dark variant** (same palette family): Use `#0D1824` bg, `#112233` surface, `#19334D` surface-2, `#1A4D7A` accent.

---

### Palette 2 — "Coral Studio"

**Personality:** Warm, approachable, home-office developer workspace. Warm whites with coral/salmon accents. Feels less corporate than Azure Bloom.

| Variable                   | Value     | Notes                  |
| -------------------------- | --------- | ---------------------- |
| `--color-bg`               | `#FBF7F4` | Warm off-white         |
| `--color-surface`          | `#FFFFFF` | White cards            |
| `--color-surface-2`        | `#F5EDE6` | Warm beige-grey        |
| `--color-surface-3`        | `#EEE0D4` | Top bar warm tint      |
| `--color-border`           | `#DCC8BA` | Warm grey border       |
| `--color-text`             | `#2D1A10` | Dark brown-black       |
| `--color-text-muted`       | `#7A5040` | Medium warm brown      |
| `--color-accent`           | `#D45000` | Coral-orange           |
| `--color-accent-secondary` | `#A83C00` | Deep coral             |
| Nav: Dashboard             | `#1B5EA8` | Blue offset            |
| Nav: Service Bus           | `#D45000` | Coral (matches accent) |
| Nav: AKS                   | `#00787A` | Teal                   |
| Nav: Redis                 | `#B02020` | Red                    |
| Nav: Storage               | `#7030A0` | Violet                 |
| Nav: Releases              | `#2D6E30` | Green                  |
| Nav: Settings              | `#606060` | Gray                   |

**Dark variant** (coral family): Use `#1A1008` bg, `#261810` surface, `#32221A` surface-2, `#D45000` accent.

---

### Palette 3 — "Forest Dev"

**Personality:** Earthy, calming, focused. Light sage-green backgrounds with forest-green accents. Great for long coding sessions, reduces blue-light dominance.

| Variable                   | Value     | Notes                  |
| -------------------------- | --------- | ---------------------- |
| `--color-bg`               | `#F2F6F2` | Light sage-white       |
| `--color-surface`          | `#FFFFFF` | Pure white cards       |
| `--color-surface-2`        | `#E8F0E8` | Light sage             |
| `--color-surface-3`        | `#DCEADC` | Slightly deeper sage   |
| `--color-border`           | `#B8D0B8` | Muted green border     |
| `--color-text`             | `#1A2D1A` | Deep forest text       |
| `--color-text-muted`       | `#4A7050` | Muted forest green     |
| `--color-accent`           | `#2E7D32` | Forest green           |
| `--color-accent-secondary` | `#1B5E20` | Dark green             |
| Nav: Dashboard             | `#1565C0` | Blue                   |
| Nav: Service Bus           | `#BF5000` | Orange                 |
| Nav: AKS                   | `#00838F` | Teal                   |
| Nav: Redis                 | `#C62828` | Red                    |
| Nav: Storage               | `#6A1B9A` | Purple                 |
| Nav: Releases              | `#2E7D32` | Green (matches accent) |
| Nav: Settings              | `#546E7A` | Slate                  |

**Dark variant** (forest family): Use `#0A120A` bg, `#121E12` surface, `#1A2E1A` surface-2, `#388E3C` accent.

---

### Palette 4 — "Violet Cloud"

**Personality:** Slightly playful, creative, warm lavender. Appeals to developers who like Dracula/Catppuccin aesthetics in their editors but want a light IDE.

| Variable                   | Value     | Notes                   |
| -------------------------- | --------- | ----------------------- |
| `--color-bg`               | `#F5F3FB` | Light lavender-white    |
| `--color-surface`          | `#FFFFFF` | White cards             |
| `--color-surface-2`        | `#EDE8F8` | Soft lavender           |
| `--color-surface-3`        | `#E2DAEF` | Deeper lavender tint    |
| `--color-border`           | `#C5B8E0` | Lavender border         |
| `--color-text`             | `#1E1A30` | Deep purple-black       |
| `--color-text-muted`       | `#6050A0` | Medium violet           |
| `--color-accent`           | `#6030C8` | Vivid violet            |
| `--color-accent-secondary` | `#4020A0` | Deep violet             |
| Nav: Dashboard             | `#1565C0` | Blue                    |
| Nav: Service Bus           | `#C06020` | Orange-amber            |
| Nav: AKS                   | `#006080` | Teal                    |
| Nav: Redis                 | `#B83030` | Red                     |
| Nav: Storage               | `#6030C8` | Violet (matches accent) |
| Nav: Releases              | `#2D7030` | Green                   |
| Nav: Settings              | `#607080` | Steel                   |

**Dark variant** (violet family): Use `#110D1E` bg, `#1C1530` surface, `#252040` surface-2, `#7B52E8` accent.

---

## C. Per-Feature Nav Icon Colors

### Approach

Add a `data-area` attribute to the rendered `.nav-item` div in `NavItem.razor`. CSS rules in `app.css` then select `[data-area="X"] fluent-icon` to colorize the icon. The `active` state uses the same color at full opacity; the inactive state uses it at reduced opacity.

### NavItem.razor Change

```razor
<!-- Replace the outer div -->
<div class="nav-item @(CurrentArea == Area ? "active" : "")"
     data-area="@Area"
     @onclick="Navigate"
     title="@(IsExpanded ? "" : Label)">
    <FluentIcon Value="@NavIcon" Width="20px" />
    @if (IsExpanded)
    {
        <span>@Label</span>
    }
</div>
```

No new `@code` changes required — `Area` is already a parameter.

### app.css Rules

Add a block after the existing `.nav-item` rules:

```css
/* ── Per-feature nav icon colors ── */
.nav-item[data-area='dashboard'] fluent-icon {
  color: var(--color-nav-dashboard);
  opacity: 0.65;
}
.nav-item[data-area='service-bus'] fluent-icon {
  color: var(--color-nav-servicebus);
  opacity: 0.65;
}
.nav-item[data-area='aks'] fluent-icon {
  color: var(--color-nav-aks);
  opacity: 0.65;
}
.nav-item[data-area='redis'] fluent-icon {
  color: var(--color-nav-redis);
  opacity: 0.65;
}
.nav-item[data-area='storage'] fluent-icon {
  color: var(--color-nav-storage);
  opacity: 0.65;
}
.nav-item[data-area='releases'] fluent-icon {
  color: var(--color-nav-releases);
  opacity: 0.65;
}
.nav-item[data-area='settings'] fluent-icon {
  color: var(--color-nav-settings);
  opacity: 0.65;
}

/* Active state: full opacity, icon matches the active accent border color */
.nav-item.active[data-area='dashboard'] fluent-icon {
  opacity: 1;
}
.nav-item.active[data-area='service-bus'] fluent-icon {
  opacity: 1;
}
.nav-item.active[data-area='aks'] fluent-icon {
  opacity: 1;
}
.nav-item.active[data-area='redis'] fluent-icon {
  opacity: 1;
}
.nav-item.active[data-area='storage'] fluent-icon {
  opacity: 1;
}
.nav-item.active[data-area='releases'] fluent-icon {
  opacity: 1;
}
.nav-item.active[data-area='settings'] fluent-icon {
  opacity: 1;
}

/* Active state: left accent border uses feature color instead of generic --color-accent */
.nav-item.active[data-area='dashboard'] {
  border-left-color: var(--color-nav-dashboard);
  background: color-mix(in srgb, var(--color-nav-dashboard) 10%, transparent);
}
.nav-item.active[data-area='service-bus'] {
  border-left-color: var(--color-nav-servicebus);
  background: color-mix(in srgb, var(--color-nav-servicebus) 10%, transparent);
}
.nav-item.active[data-area='aks'] {
  border-left-color: var(--color-nav-aks);
  background: color-mix(in srgb, var(--color-nav-aks) 10%, transparent);
}
.nav-item.active[data-area='redis'] {
  border-left-color: var(--color-nav-redis);
  background: color-mix(in srgb, var(--color-nav-redis) 10%, transparent);
}
.nav-item.active[data-area='storage'] {
  border-left-color: var(--color-nav-storage);
  background: color-mix(in srgb, var(--color-nav-storage) 10%, transparent);
}
.nav-item.active[data-area='releases'] {
  border-left-color: var(--color-nav-releases);
  background: color-mix(in srgb, var(--color-nav-releases) 10%, transparent);
}
.nav-item.active[data-area='settings'] {
  border-left-color: var(--color-nav-settings);
  background: color-mix(in srgb, var(--color-nav-settings) 10%, transparent);
}

/* Nav item text color follows feature color when active */
.nav-item.active[data-area='dashboard'] {
  color: var(--color-nav-dashboard);
}
.nav-item.active[data-area='service-bus'] {
  color: var(--color-nav-servicebus);
}
.nav-item.active[data-area='aks'] {
  color: var(--color-nav-aks);
}
.nav-item.active[data-area='redis'] {
  color: var(--color-nav-redis);
}
.nav-item.active[data-area='storage'] {
  color: var(--color-nav-storage);
}
.nav-item.active[data-area='releases'] {
  color: var(--color-nav-releases);
}
.nav-item.active[data-area='settings'] {
  color: var(--color-nav-settings);
}
```

**Note:** `color-mix()` requires a modern browser engine — the MAUI WebView running on Edge (Chromium) supports it. Verify on the Windows target.

If `color-mix()` is unreliable, fall back to explicit `rgba()` hex values per feature using the same pattern as the current `.nav-item.active` rule (`rgba(0,120,212,0.08)`).

### Nav Label Alignment (collapsed state)

The `data-area` attribute is present regardless of whether the nav is collapsed or expanded, so the icon colors work in both states.

---

## D. Dashboard Redesign

### Layout Philosophy

Remove the `max-width: 960px` constraint. Use a fluid CSS grid that adapts to the available content column. At typical desktop widths (1280–1920px) show two or three columns.

### New Layout Structure

```
┌─────────────────────────────────────────────────────────────┐
│  .dashboard-hero-row  (full width, 4 stat chips)            │
├─────────────────────────────────────────────────────────────┤
│  .health-grid  (4 tiles, 2×2 or 4×1 depending on width)    │
├───────────────────────────────┬─────────────────────────────┤
│  .activity-panel (2/3 width)  │  .pinned-panel (1/3 width)  │
└───────────────────────────────┴─────────────────────────────┘
```

### Hero Row

Add a `.dashboard-hero-row` above the health section. This row shows 4 high-level summary chips using real data where available, otherwise a configured/unconfigured badge:

```razor
<div class="dashboard-hero-row">
    <div class="hero-stat @(_sbConfigured ? "" : "hero-stat--unconfigured")">
        <span class="hero-stat-icon">
            <FluentIcon Value="@(new Icons.Regular.Size20.ArrowSwap())" Width="20px" />
        </span>
        <span class="hero-stat-value">@(_sbData?.Summary ?? "—")</span>
        <span class="hero-stat-label">Service Bus</span>
    </div>
    <!-- Repeat for AKS, Redis, Releases -->
</div>
```

CSS for hero:

```css
.dashboard-hero-row {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: var(--spacing-lg);
  margin-bottom: var(--spacing-xl);
}

.hero-stat {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--spacing-xs);
  padding: var(--spacing-lg);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-sm);
}

.hero-stat-value {
  font-size: var(--font-size-xl);
  font-weight: 700;
  color: var(--color-text);
}

.hero-stat-label {
  font-size: var(--font-size-sm);
  color: var(--color-text-muted);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.hero-stat--unconfigured {
  opacity: 0.5;
}
```

### Health Tiles — Visual Upgrade

The existing `HealthTile` component shows a title, status, and data summary. Upgrade the tile card style without changing the component's logic:

- Add a feature-colored left border‐accent using an inline `style` or a `data-area` attribute on the tile container.
- Give each tile a `box-shadow: var(--shadow-sm)`, `border-radius: var(--radius-md)`.
- Increase min-height to `120px`.
- If the feature is configured and healthy, show a subtle green top stripe; if error, red.

In `DashboardPage.razor.css` (scoped file), target `.health-grid` and friends:

```css
.health-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: var(--spacing-lg);
  margin-bottom: var(--spacing-xl);
}
```

Add a feature color stripe via a CSS class helper per tile type:

```razor
<div class="health-tile-wrap health-tile-wrap--servicebus">
    <HealthTile ... />
</div>
```

```css
/* In DashboardPage.razor.css */
.health-tile-wrap {
  border-left: 3px solid transparent;
  border-radius: var(--radius-md);
}
.health-tile-wrap--servicebus {
  border-left-color: var(--color-nav-servicebus);
}
.health-tile-wrap--aks {
  border-left-color: var(--color-nav-aks);
}
.health-tile-wrap--redis {
  border-left-color: var(--color-nav-redis);
}
.health-tile-wrap--releases {
  border-left-color: var(--color-nav-releases);
}
```

### Activity + Pinned — 2-Column Layout

Replace the stacked sections with a side-by-side grid in the lower area.

```razor
<div class="dashboard-lower">
    <section class="dashboard-activity-panel">
        <h2 class="dashboard-section-title">Recent Activity</h2>
        <!-- existing activity list -->
    </section>
    <aside class="dashboard-pinned-panel">
        <h2 class="dashboard-section-title">Pinned</h2>
        <!-- existing pinned chips -->
    </aside>
</div>
```

```css
/* In DashboardPage.razor.css */
.dashboard-lower {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: var(--spacing-xl);
  align-items: start;
}

.dashboard-activity-panel,
.dashboard-pinned-panel {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-sm);
  padding: var(--spacing-lg);
}
```

### Remove `max-width` Constraint

In `DashboardPage.razor.css`, remove:

```css
/* REMOVE */
max-width: 960px;
margin: 0 auto;
```

Replace with:

```css
.dashboard-page {
  padding: var(--spacing-xl);
  height: 100%;
  overflow-y: auto;
  box-sizing: border-box;
}
```

---

## E. Per-Page UX Improvements

### E1. SettingsPage.razor

**Current state:** Single-column nested accordion wrapped in a padded div with inline styles. No visual hierarchy. Theme selector absent.

**Target layout:** Two-column `settings-shell` grid — left sidebar with section navigation (Appearance, Service Bus, AKS, Redis, DevOps, Storage) + right content well that shows the active section.

**Implementation approach:**

1. Replace `FluentAccordion` with a manual `_activeSection` field and a `switch` block:

```razor
<div class="settings-shell">
    <nav class="settings-nav">
        <button class="settings-nav-item @(_active == "appearance" ? "active" : "")"
                @onclick="@(() => _active = "appearance")">
            <FluentIcon Value="@(new Icons.Regular.Size20.PaintBrush())" Width="18px" />
            Appearance
        </button>
        <button class="settings-nav-item @(_active == "servicebus" ? "active" : "")"
                @onclick="@(() => _active = "servicebus")">
            <FluentIcon Value="@(new Icons.Regular.Size20.ArrowSwap())" Width="18px" />
            Service Bus
        </button>
        <!-- ... etc ... -->
    </nav>
    <div class="settings-content">
        @switch (_active)
        {
            case "appearance": <AppearanceSettings OnThemeChanged="OnThemeChanged" /> break;
            case "servicebus": <ServiceBusConfigForm ... /> break;
            <!-- ... -->
        }
    </div>
</div>
```

2. **`AppearanceSettings` component** (new, in `Components/Shared/`): a small component with a theme dropdown and a preview chip showing the active theme's accent color.

```razor
<div class="appearance-section">
    <div class="form-field">
        <label class="form-label">Theme</label>
        <FluentSelect Value="@CurrentTheme" ValueChanged="OnThemeChanged" TOption="string">
            <FluentOption Value="dark">Dark</FluentOption>
            <FluentOption Value="light-azure-bloom">Light — Azure Bloom</FluentOption>
            <FluentOption Value="light-coral-studio">Light — Coral Studio</FluentOption>
            <FluentOption Value="light-forest-dev">Light — Forest Dev</FluentOption>
            <FluentOption Value="light-violet-cloud">Light — Violet Cloud</FluentOption>
        </FluentSelect>
    </div>
    <div class="theme-preview" data-theme="@CurrentTheme">
        Preview chip
    </div>
</div>
```

3. The `OnThemeChanged` callback calls `MainLayout.SetThemeAsync()` via the cascaded `Layout` reference.

**CSS (in `SettingsPage.razor.css`):**

```css
.settings-shell {
  display: grid;
  grid-template-columns: 200px 1fr;
  height: 100%;
  overflow: hidden;
}

.settings-nav {
  border-right: 1px solid var(--color-border);
  padding: var(--spacing-md) 0;
  overflow-y: auto;
}

.settings-nav-item {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  width: 100%;
  padding: var(--spacing-sm) var(--spacing-lg);
  background: none;
  border: none;
  text-align: left;
  color: var(--color-text-muted);
  font-size: var(--font-size-md);
  cursor: pointer;
  border-left: 3px solid transparent;
}

.settings-nav-item:hover {
  background: var(--color-surface-2);
  color: var(--color-text);
}
.settings-nav-item.active {
  color: var(--color-accent);
  border-left-color: var(--color-accent);
  background: var(--color-accent-subtle);
}

.settings-content {
  padding: var(--spacing-xl);
  overflow-y: auto;
  max-width: 640px;
}
```

**NOTE (BL-1):** If `AppearanceSettings` is placed in `Components/Shared/`, add `@using SwebKit.App.Components.Shared` to `_Imports.razor`.

**Save feedback:** Replace the current inline `rgba(67,160,71,0.2)` save banner with a `FluentToast` or a styled `.settings-saved-banner` using `var(--color-success)` and `var(--radius-sm)`.

---

### E2. ReleasesPage.razor

**Current state:** Entirely inline-styled. Tab navigation uses raw `<button>` with inline styles. Release selector uses a raw `<select>` with inline styles.

**Target:** All structural layout in CSS classes. Pill-style tab bar. Styled release selector.

**Tab bar replacement:**

```razor
<!-- Replace the inline-styled tab div with: -->
<div class="pill-tab-bar">
    <button class="pill-tab @(ActiveTab == "board" ? "active" : "")"
            @onclick="@(() => SetTab("board"))">
        Release Board
    </button>
    <button class="pill-tab @(ActiveTab == "approvals" ? "active" : "")"
            @onclick="@(() => SetTab("approvals"))">
        Approval Center
        @if (PendingApprovalCount > 0)
        {
            <span class="pill-badge">@PendingApprovalCount</span>
        }
    </button>
    <button class="pill-tab @(ActiveTab == "pipelines" ? "active" : "")">Deployments</button>
    <button class="pill-tab @(ActiveTab == "tags" ? "active" : "")">Tag Manager</button>
</div>
```

```css
/* in app.css — shared, reusable */
.pill-tab-bar {
  display: flex;
  gap: var(--spacing-xs);
  padding: var(--spacing-xs);
  background: var(--color-surface-2);
  border-radius: var(--radius-lg);
  margin-bottom: var(--spacing-lg);
  width: fit-content;
}

.pill-tab {
  padding: var(--spacing-xs) var(--spacing-lg);
  border-radius: var(--radius-md);
  border: none;
  background: transparent;
  color: var(--color-text-muted);
  font-size: var(--font-size-md);
  cursor: pointer;
  position: relative;
  transition:
    background 0.15s ease,
    color 0.15s ease;
}

.pill-tab:hover {
  background: var(--color-surface);
  color: var(--color-text);
}
.pill-tab.active {
  background: var(--color-surface);
  color: var(--color-accent);
  box-shadow: var(--shadow-sm);
}

.pill-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  margin-left: var(--spacing-xs);
  padding: 1px 6px;
  border-radius: 8px;
  background: var(--color-accent);
  color: white;
  font-size: var(--font-size-xs);
  font-weight: 700;
}
```

**Page wrapper:** Replace `style="padding:20px; height:100%; overflow-y:auto;"` with class `page-content` (add to `app.css`):

```css
.page-content {
  padding: var(--spacing-xl);
  height: 100%;
  overflow-y: auto;
  box-sizing: border-box;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: var(--spacing-lg);
}

.page-title {
  margin: 0;
  font-size: var(--font-size-xl);
  font-weight: 600;
}
```

**Release selector:** Replace the raw `<select>` with a styled wrapper:

```razor
<div class="release-selector-wrap">
    <select class="release-selector" value="@SelectedReleaseId" @onchange="OnReleaseSelected">
        @foreach (var r in EffectiveReleases.OrderByDescending(r => r.CreatedAt))
        {
            <option value="@r.Id">@r.Name @StatusLabel(r.Status)</option>
        }
    </select>
</div>
```

```css
.release-selector {
  padding: var(--spacing-xs) var(--spacing-md);
  font-size: var(--font-size-md);
  background: var(--color-surface);
  color: var(--color-text);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  cursor: pointer;
}

.release-selector:focus {
  outline: 2px solid var(--color-accent);
  outline-offset: 1px;
}
```

---

### E3. ServiceBusPage.razor

**Current state:** Heavily inline-styled. Left entity pane and right detail pane split using inline flex. Namespace/queue/topic nodes have hardcoded padding and font-sizes.

**Target:** Move layout to CSS classes. Improve entity tree visual hierarchy.

**Layout shell:** Add `.sb-page-shell` CSS class for the left/right split:

```css
/* app.css */
.sb-page-shell {
  display: grid;
  grid-template-columns: 280px 1fr;
  height: 100%;
  overflow: hidden;
}

.sb-entity-pane {
  border-right: 1px solid var(--color-border);
  overflow-y: auto;
  background: var(--color-surface);
}

.sb-detail-pane {
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
```

**Entity tree improvements:**

- Namespace header row: `font-size: var(--font-size-sm); text-transform: uppercase; letter-spacing: 0.5px; color: var(--color-text-faint);` — makes namespace label visually subordinate to queue names.
- Queue/topic rows: increase hit area to `padding: 7px 12px`.
- DLQ rows: add `color: var(--color-error);` and a small `⚠` prefix icon.
- Collapse animation on entity group: CSS only — add `transition: max-height 0.2s ease; overflow: hidden;` (caution: use BL-4 guidance if toggling with `@if`; prefer `display` or `height` toggle instead).

---

### E4. AksPage.razor

**Current state:** Inline-styled page shell and pod list rows.

**Target:** CSS classes, colored pod status badges.

**Pod status badges:**

```css
/* app.css */
.pod-status-badge {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: var(--radius-xl);
  font-size: var(--font-size-xs);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.4px;
}

.pod-status-badge--running {
  background: rgba(67, 160, 71, 0.15);
  color: var(--color-success);
}
.pod-status-badge--pending {
  background: rgba(251, 140, 0, 0.15);
  color: var(--color-warning);
}
.pod-status-badge--failed {
  background: rgba(229, 57, 53, 0.15);
  color: var(--color-error);
}
.pod-status-badge--unknown {
  background: var(--color-surface-2);
  color: var(--color-text-muted);
}
```

Usage in `AksPage.razor`:

```razor
<span class="pod-status-badge pod-status-badge--@pod.Status.ToLower()">@pod.Status</span>
```

---

### E5. RedisPage.razor

**Current state:** Two-pane layout with key browser on the left and value viewer on the right. Some inline styles.

**Target cleanup:**

- Replace inline-styled pane split with `.redis-page-shell` class (same pattern as `sb-page-shell` above, left ~300px).
- Add `.redis-key-row` class for key list items; current font-size and padding are too compressed.
- Value viewer: add `background: var(--color-surface); border-radius: var(--radius-md); padding: var(--spacing-lg)` card wrapper.
- Key type badges (String, Hash, List, Set, ZSet): use `.pod-status-badge` pattern with distinct colors per type:

```css
.key-type-badge--string {
  background: rgba(0, 120, 212, 0.12);
  color: var(--color-accent);
}
.key-type-badge--hash {
  background: rgba(155, 89, 182, 0.12);
  color: #9b59b6;
}
.key-type-badge--list {
  background: rgba(231, 76, 60, 0.12);
  color: #e74c3c;
}
.key-type-badge--set {
  background: rgba(46, 204, 113, 0.12);
  color: #27ae60;
}
.key-type-badge--zset {
  background: rgba(243, 156, 18, 0.12);
  color: #f39c12;
}
```

---

### E6. StoragePage.razor

**Current state:** Single-pane blob browser with minimal layout.

**Target cleanup:**

- Wrap in `.page-content` (shared class from § E2).
- Container selector: use the `.pill-tab-bar` pattern (shared) for container-level breadcrumb navigation.
- Blob table: add `border-radius: var(--radius-md); box-shadow: var(--shadow-sm)` to the outer table wrapper.
- Blob rows: alternate row tinting using `var(--color-surface-2)` on odd rows.
- File type icons: small `FluentIcon` per blob extension.

---

## F. Global Style Improvements

### F1. Typography

Change base font-size from `13px` to `14px` in the `html, body` block:

```css
html,
body {
  font-size: 14px; /* was 13px */
}
```

Update `--font-size-md` from `13px` to `14px` (done in § A).

Update the `--font-size-sm` token from `11px` to `12px` so the relative scale stays proportional.

Review any hardcoded `font-size: 10px` / `11px` / `12px` inline values in components — replace with `var(--font-size-xs)` / `var(--font-size-sm)` / `var(--font-size-md)` accordingly.

### F2. Shadow System

Add to `app.css` (already defined in the `:root` block in § A). Apply to:

| Element                     | Variable      |
| --------------------------- | ------------- |
| `.health-tile-wrap`         | `--shadow-sm` |
| `.dashboard-activity-panel` | `--shadow-sm` |
| `.pill-tab.active`          | `--shadow-sm` |
| `.command-palette-box`      | `--shadow-lg` |
| Modal/overlay containers    | `--shadow-lg` |
| Dropdown/popover elements   | `--shadow-md` |

### F3. Border-Radius Standardization

Audit current hardcoded values and replace:

| Current hardcoded          | Replace with             |
| -------------------------- | ------------------------ |
| `border-radius: 3px`       | `var(--radius-sm)` (4px) |
| `border-radius: 4px–5px`   | `var(--radius-sm)`       |
| `border-radius: 6px–8px`   | `var(--radius-md)`       |
| `border-radius: 10px–12px` | `var(--radius-lg)`       |
| `border-radius: 16px+`     | `var(--radius-xl)`       |

Start with `app.css` global class definitions first, then tackle scoped `.razor.css` files.

### F4. Micro-Animations

Add transitions for the following interactions (all in `app.css`):

```css
/* Nav item hover */
.nav-item {
  transition:
    background 0.15s ease,
    color 0.12s ease;
}

/* Nav item active border expand */
.nav-item {
  border-left: 3px solid transparent;
  transition:
    border-left-color 0.15s ease,
    background 0.15s ease,
    color 0.12s ease;
}

/* Tab active state */
.pill-tab {
  transition:
    background 0.15s ease,
    color 0.12s ease,
    box-shadow 0.15s ease;
}

/* Button press micro-feedback */
.fluent-button,
button {
  transition: transform 0.08s ease;
}
.fluent-button:active,
button:active {
  transform: scale(0.97);
}

/* Card expand/collapse for SettingsPage sections */
.settings-content {
  transition: opacity 0.15s ease;
}

/* Command palette overlay fade-in */
.command-palette-overlay {
  animation: fadeIn 0.1s ease;
}
@keyframes fadeIn {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}
```

**Caution:** Do not add transitions to `max-height` with unknown values — use `opacity` or `transform` instead.

### F5. Top Bar Improvements

**Remove hardcoded color:** `TopBar.razor` currently has `style="background: #1E1E2E;"` on the outer `div`. Remove this and let `var(--color-surface-3)` from the theme apply (this distinguishes the top bar from the nav panel which uses `var(--color-surface)`).

```razor
<!-- Remove inline style -->
<div class="top-bar">
```

The `top-bar` CSS class already uses `background: var(--env-color)` for environment-aware coloring. Ensure `--env-color` is defined per theme (it is, in § A).

**App logo/name polish:** In `TopBar.razor`, replace the plain `<span>` logo with a styled element:

```razor
<span class="app-logo-text">SwebKit</span>
```

```css
/* In app.css or TopBar.razor.css */
.app-logo-text {
  font-weight: 700;
  font-size: var(--font-size-lg);
  letter-spacing: -0.3px;
  color: var(--env-text);
}
```

**Environment indicator:** If a profile name or environment tag is available from `AppState.Config`, show it as a small pill badge in the top bar:

```razor
@if (!string.IsNullOrEmpty(AppState.Config.ProfileName))
{
    <span class="env-badge">@AppState.Config.ProfileName</span>
}
```

```css
.env-badge {
  padding: 2px 8px;
  border-radius: var(--radius-xl);
  background: var(--color-accent-subtle);
  color: var(--color-accent);
  font-size: var(--font-size-xs);
  font-weight: 600;
  letter-spacing: 0.3px;
}
```

### F6. Status Bar Improvements

The status bar currently shows only muted text items. Improve it by:

1. Showing the active area name (e.g., "Service Bus") as the first status chip with the feature-area color.
2. Showing a connection indicator: a colored dot (green/red/grey) before connection status text.
3. Keeping the font at `var(--font-size-xs)` but using `var(--color-surface-3)` as background (distinct from nav `var(--color-surface)`).

```css
.status-bar {
  background: var(--color-surface-3); /* was var(--color-surface) */
}

.status-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 0 var(--spacing-xs);
}

.status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
}
.status-dot--ok {
  background: var(--color-success);
}
.status-dot--error {
  background: var(--color-error);
}
.status-dot--unknown {
  background: var(--color-text-faint);
}
```

---

## G. Implementation Phasing

### Phase 1 — Theme Infrastructure (Size: M)

**Goal:** All subsequent phases depend on this. The CSS variable restructure must be done first.

**Key files:**

- `src/SwebKit.App/wwwroot/app.css` — restructure `:root` into `[data-theme]` blocks, add shadow/radius tokens, bump typography
- `Components/Layout/MainLayout.razor` — add `_currentTheme` field, `SetThemeAsync`, theme init in `OnAfterRenderAsync`
- `Components/Pages/SettingsPage.razor` — add Appearance section with theme dropdown (minimal version: just a dropdown, full two-column layout in Phase 5)

**Acceptance:** Dark theme looks identical to current state. Theme toggle works and persists on restart.

---

### Phase 2 — Dark Theme Polish + Nav Icon Colors (Size: S)

**Goal:** Fastest visible improvement. Can ship independently as a dark-theme-only improvement.

**Key files:**

- `Components/Layout/TopBar.razor` — remove hardcoded `background: #1E1E2E`
- `Components/Layout/NavItem.razor` — add `data-area="@Area"` attribute
- `src/SwebKit.App/wwwroot/app.css` — add per-feature icon color rules (§ C)
- `Components/Layout/StatusBar.razor` — use `var(--color-surface-3)`, add connection dot

**Acceptance:** Nav icons are distinctly colored per feature. Top bar, nav, and status bar each have visibly different surface levels. No regressions in component tests.

---

### Phase 3 — Light Themes (Size: M)

**Prerequisite:** Phase 1 complete.

**Key files:**

- `src/SwebKit.App/wwwroot/app.css` — add all four `[data-theme="light-*"]` blocks (§ A and § B): `light-azure-bloom`, `light-coral-studio`, `light-forest-dev`, `light-violet-cloud`
- `Components/Pages/SettingsPage.razor` (or `AppearanceSettings` component) — all 5 theme options in dropdown

**Acceptance:** All four light themes functional on all pages. No hardcoded dark colors visible. `FluentDesignTheme` switches to light mode for any non-dark selection. Switching between light palettes live-previews correctly.

---

### Phase 4 — Dashboard Redesign (Size: M)

**Prerequisite:** Phase 1 complete (tokens must exist for shadows/radii).

**Key files:**

- `Components/Pages/DashboardPage.razor` — add hero row, 2-column lower layout, health tile wrappers
- `Components/Pages/DashboardPage.razor.css` — full rewrite, remove max-width

**Acceptance:** Dashboard uses full width. Hero row visible. Activity feed and pinned items appear side by side. Feature-colored tile accents visible.

---

### Phase 5 — Page-by-Page Inline Style Cleanup (Size: L)

**Prerequisite:** Phase 1 complete. Can be done concurrently across pages.

**Order (suggested):**

1. `SettingsPage` — two-column layout (highest value, most used)
2. `ReleasesPage` — pill tabs (common user path)
3. `ServiceBusPage` — most complex (save for focused session)
4. `AksPage`
5. `RedisPage`
6. `StoragePage`

**Key files:** Each page `.razor` + its `.razor.css` file.

**Acceptance per page:** No inline `style=""` attributes on layout/structural elements (only dynamic values like colors passed from C# logic are acceptable as inline). Each page verified visually in both themes.

---

### Phase 6 — Global Style Polish (Size: S)

**Prerequisite:** Phase 5 complete.

**Key files:**

- `src/SwebKit.App/wwwroot/app.css` — shadow assignments, radius audit, micro-animation transitions, top bar logo, env badge
- `Components/Layout/TopBar.razor` — env badge, logo

**Acceptance:** All cards have consistent shadows. All rounded elements use the token scale. Key interactions have smooth transitions. Status bar shows connection indicator.

---

## Validation

- **Component tests:** `SwebKit.App.Tests` should pass after each phase. Structural HTML changes (adding CSS classes, removing inline styles) may require updating test selector strings in bUnit tests.
- **Manual QA:** After each phase, spot-check all 6 pages in both dark and light themes.
- **Theme persistence:** Verify that closing and reopening the app retains the selected theme.
- **Hardcoded color audit:** After Phase 3, grep for `#1E1E`, `#13131`, `#252525` literals in `.razor` files to catch any leaked hardcoded values.

```powershell
# Run from repo root to find remaining hardcoded dark-palette colors in Razor files
Select-String -Path "src/**/*.razor" -Pattern "#[0-9A-Fa-f]{6}" -Recurse
```

## Notes

- `color-mix()` (used in nav active background) requires Chromium 111+. The MAUI WebView on Windows runs on Edge WebView2, which is kept updated with Windows. Verify on the actual test machine before using in production rather than falling back to explicit rgba values.
- All new component subdirectories require a `@using` line in `_Imports.razor` (BL-1).
- All async state mutations after `await` calls must use `InvokeAsync(StateHasChanged)` (BL-2).
- The `AppearanceSettings` component, if created as `Components/Shared/AppearanceSettings.razor`, needs `@using SwebKit.App.Components.Shared` in `_Imports.razor`.
- `decisions.md` should be created in this feature folder once the palette is selected and other non-obvious tradeoffs are recorded.
