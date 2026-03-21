# Test Plan — Fluent UI Icons in Navigation

## Validation strategy

Visual verification only. No automated tests required for an icon swap.

## Main scenarios

| Scenario | Expected |
|---|---|
| Nav expanded | Each item shows icon + label; icons are consistent size and weight |
| Nav collapsed | Each item shows icon only; icons are centred and correctly sized |
| Active nav item | Icon and label styled with active/accent colour |
| Dashboard cards | Each card shows correct Fluent icon at appropriate size |
| 100% DPI | Icons render crisp (SVG, no blur) |
| 125% / 150% DPI | Icons still render crisp; no pixel rounding artifacts |

## Regression risks

- CSS changes to `.nav-icon` sizing must not break the collapsed nav layout
- Icon size must not cause nav item height to change (affecting overall layout)

## Acceptance criteria

- All emoji icons replaced with Fluent UI `<FluentIcon>` components
- No emoji visible anywhere in the left nav or dashboard cards
- Icons render correctly at 100% and 150% DPI
- Collapsed nav still shows icon-only layout correctly
