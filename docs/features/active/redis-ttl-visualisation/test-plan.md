# Test Plan — Redis TTL Visualisation

## Validation strategy

Unit tests for the TTL formatter. Manual visual verification for the progress bar and countdown.

## Unit tests (TTL formatter)

| Input | Expected output |
|---|---|
| TTL = 7260 (2h 1m) | "2h 1m remaining" |
| TTL = 3599 (59m 59s) | "59m 59s remaining" |
| TTL = 45 | "45s remaining" |
| TTL = -1 | "No expiry" |
| TTL = -2 | "Key has no TTL / already expired" |
| TTL = 0 | "Expired" or "0s remaining" |

## Main scenarios

### Human-readable label

| Scenario | Expected |
|---|---|
| Key with long TTL (hours) | Shows "Xh Ym remaining" |
| Key with short TTL (< 1 min) | Shows "Xs remaining" in amber/red colour |
| Key with no expiry | Shows "No expiry" (no colour indicator) |

### Progress bar

| Scenario | Expected |
|---|---|
| TTL > 20% of capture-time TTL | Bar is green |
| TTL 5–20% of capture-time TTL | Bar is amber |
| TTL < 5% of capture-time TTL | Bar is red |
| TTL unknown at capture time | Bar degrades to absolute-based display (capped at 1 hour) |

### Live countdown

| Scenario | Expected |
|---|---|
| Panel open for 10 seconds | TTL label decrements by ~10s; bar updates proportionally |
| Panel open for 30 seconds | Server TTL polled to correct any drift |
| Panel closed | Timer disposed; no further ticks |

### Set TTL action

| Scenario | Expected |
|---|---|
| User clicks "Set TTL" | Popover opens with seconds/minutes/hours input |
| User sets valid TTL and confirms | Key TTL updated; panel refreshes with new value |
| User selects "No expiry" | Key TTL removed; panel shows "No expiry" |

## Regression risks

- Timer must be disposed when `RedisKeyDetail` is disposed — verify no timer leaks when switching keys rapidly
- TTL formatter must handle edge cases (0, -1, -2) without throwing

## Acceptance criteria

- TTL shown as human string immediately on panel open
- Countdown updates every second without page flicker
- Bar colour matches the threshold rules
- "Set TTL" action updates the key and refreshes the display
