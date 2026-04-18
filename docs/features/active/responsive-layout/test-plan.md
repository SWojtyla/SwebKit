# Test Plan - responsive-layout

---

title: "Test Plan - responsive-layout"
owner: ""
status: "Not started"
created: "2026-04-18"
updated: "2026-04-18"

---

## Goal

Validate that all shell and page layouts render correctly across the desktop width range (1024 px–2560 px) with no content clipping, no fixed-width overflow, and readable information density.

## Scope

- **In scope:** Shell, Dashboard, Service Bus, AKS, Redis, Storage, Settings, Incident Timeline, Observability, Pipelines — at four canonical window widths
- **Out of scope:** Mobile/tablet resolutions, browser-based testing outside MAUI WebView, accessibility audit (separate concern)

## Canonical test widths

| Label | Width | Represents |
|-------|-------|------------|
| Narrow | 1024 px | Surface laptop, narrow side-by-side |
| Laptop | 1280 px | Standard laptop |
| Common | 1366 px | Most common laptop resolution |
| Full HD | 1920 px | Standard desktop |
| Wide | 2560 px | High-res desktop |

---

## Main scenarios (priority order)

1. **Shell nav auto-collapse (Wave 0)** — At initial launch with window at 1024 px, nav must be in icon-only mode. Expanding manually must work. Persisted state must be respected on next launch.

2. **Dashboard hero row wrapping (Wave 1)** — At 1024 px, the 4 hero stat cards must wrap to 2×2 (or 1×4) without clipping. At 1920 px they remain in a single 4-column row.

3. **Service Bus 3-pane layout (Wave 2)** — At 1024 px with nav collapsed, entity panel (260 px) + main message list must remain usable. Detail drawer, when open, must not collapse the message list to zero.

4. **AKS toolbar row (Wave 2)** — Cluster selector + namespace selector + tab bar must remain accessible (scroll or wrap) at 1024 px.

5. **Command palette (Wave 3)** — Opening the command palette at 1024 px must not overflow the window horizontally.

6. **Settings form layout (Wave 4)** — Config form field rows must stack vertically at 1024 px; no label/input overflow.

7. **Incident Timeline side panels (Wave 5)** — Panels must clamp and not squish the evidence view below 200 px.

8. **4 K wide rendering (all waves)** — At 2560 px, dashboard tiles must not stretch to unreadable widths; panels must not grow indefinitely.

---

## Manual checks

| Check | Window width | Steps | Expected |
|-------|-------------|-------|----------|
| Nav auto-collapse | 1024 px | Launch app, do not toggle nav | Nav is in icon-only mode |
| Nav toggle still works | 1024 px | Click hamburger | Nav expands to full width |
| Dashboard row | 1024 px | Open Dashboard | Hero row shows 2 rows of stats, no overflow |
| Dashboard row | 1920 px | Open Dashboard | Hero row shows 4 stats in a single row |
| SB entity panel | 1024 px | Open Service Bus, connect | Entity panel visible, main list has at least 200 px |
| SB detail drawer | 1024 px | Open Service Bus, select a message | Drawer appears, content readable, no horizontal overflow |
| AKS toolbar | 1024 px | Open AKS page | Cluster + namespace selectors accessible |
| Command palette | 1024 px | Press Ctrl+K or shortcut | Palette fits within window horizontally |
| Keyboard shortcuts | 1024 px | Open shortcuts panel | Panel fits within window horizontally |
| Settings form | 1024 px | Open Settings | Form fields stack; no label clips |
| Wide screen grid | 2560 px | Open Dashboard | Tiles are not grotesquely wide; grid looks balanced |
| Redis key list | 1024 px | Open Redis page | Key list + detail pane visible |
| Storage container list | 1024 px | Open Storage page | Container list + blob list usable |
| Incident Timeline | 1024 px | Open Incident Timeline | Toolbar wraps or scrolls; no panel collapse to zero |
| Observability tabs | 1024 px | Open Observability | Tab row does not clip |
| Pipelines tree | 1024 px | Open Pipelines | Tree panel visible; detail panel not at zero width |

---

## Automated coverage

- No new unit tests required for CSS-only changes.
- Existing `SwebKit.App.Tests` component tests should continue to pass — no Razor structure changes are expected for Waves 0–3.
- For Waves 4–5 where Razor structure may change (config form rows), add component render tests confirming the wrapper element is present.
- CI gate: build passes, all existing tests green.

---

## Regression risks & mitigations

- Risk: Auto-collapse breakpoint fires on wide monitors due to viewport miscalculation — Mitigation: test on three machines (1920 px, 2560 px) to confirm 1100 px threshold does not trigger
- Risk: `auto-fit` grid changes break demo mode tile count on Dashboard — Mitigation: run demo mode screenshots at 1024, 1280, 1920 px
- Risk: `min(preferred, Xvw)` clamp on SB panels causes panel to jump size on window resize — Mitigation: add a CSS `transition: width 0.15s ease` to the affected panels so resize is smooth

---

## Acceptance criteria

- All manual checks above pass at their target widths
- No horizontal scrollbar appears at the `html`/body level at any width ≥ 1024 px
- Nav auto-collapse fires correctly on first render below 1100 px, user toggle is preserved thereafter
- Existing tests remain green in CI

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- **Approved by:** —
- **Date:** —
- **Conditions:** All manual checks at 1024, 1280, 1920, 2560 px must pass before merge
