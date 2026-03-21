# Archive Summary — Redis TTL Visualisation

---

title: "Archive Summary - Redis TTL Visualisation"
owner: ""
completed_date: "2026-03-21"
pr: ""
commit: ""

---

## Goal

Replace the raw numeric TTL display in `RedisKeyDetail` with a human-readable label and a colour-coded expiry progress bar, giving developers instant cache-health situational awareness without mental arithmetic.

## Delivered

- `src/SwebKit.Core/Services/TtlFormatter.cs` — static utility with three pure methods:
  - `FormatHuman(TimeSpan?)` — "2h 22m remaining", "45s remaining", "No expiry", "Key has no TTL / already expired"
  - `GetColor(remaining, original)` — returns `--color-success/warning/error` CSS variable; percentage-based when original TTL is known, absolute-based otherwise
  - `GetBarWidthPercent(remaining, original)` — 0–100 value; capped at 1 h for absolute mode
- `RedisKeyDetail.razor` updated:
  - Human-readable TTL label replaces raw `0s` / `8547s` output
  - `ttl-critical` CSS class applied when remaining < 60 s (label turns red)
  - Thin expiry progress bar (4 px) below the label, colour-coded via CSS `background-color`
  - Live `PeriodicTimer` countdown ticking every second (client-side, zero server round-trips)
  - Every 30 ticks the timer fires `OnRefresh` to re-fetch the actual server TTL and correct drift
  - `IDisposable` implemented; timer cancelled on key change (guarded via `ReferenceEquals` per BL-3/BL-5) and on component disposal
- `RedisKeyDetail.razor.css` — `.ttl-bar-wrapper`, `.ttl-bar-fill` (CSS `transition: width 1s linear`), `.ttl-critical`
- `docs/architecture/functionalities/redis.md` — TTL visualisation capability documented; `TtlFormatter.cs` listed in code locations; new test file in validation pointers
- `tests/SwebKit.Core.Tests/TtlFormatterTests.cs` — 22 unit tests covering all branches of all three public methods

## Key decisions

- **`TtlFormatter` placed in `SwebKit.Core`** (not `SwebKit.App`) — `SwebKit.Core` ships as a reusable library; having the formatter there lets future non-UI consumers (e.g. CLI, tests) call it without taking a Blazor dependency. Original plan suggested `SwebKit.App/Helpers` but the Core location is strictly better.
- **`TimeSpan?` API instead of `long ttlSeconds`** — `RedisKeyInfo.Ttl` is already `TimeSpan?`; adapting the utility to the same type avoids a lossy `long` conversion and keeps the null-meaning-no-expiry idiom consistent.
- **Percentage bar degrades gracefully when original TTL is unknown** — Redis does not store the original TTL; the bar uses an absolute 1-hour cap as fallback rather than hiding the bar entirely, which is more useful for pre-existing volatile keys.
- **30-second server refresh baked into the countdown loop** — drift correction is automatic without requiring a separate UI control; uses the existing `OnRefresh` callback so the page re-fetches the real TTL via the normal code path.

## Validation performed

- 22 unit tests in `TtlFormatterTests.cs` — all green; cover null, zero, negative, sub-minute, sub-hour, over-hour inputs for `FormatHuman`; three threshold buckets for `GetColor` with both known and unknown original TTL; zero/half/clamped-overcap cases for `GetBarWidthPercent`.
- No Razor compilation errors (`get_errors` confirmed clean on `RedisKeyDetail.razor` and `TtlFormatter.cs`).
- `dotnet build src/SwebKit.Core` came back with 0 warnings 0 errors.
- Manual in-app verification not performed (app requires live Redis or demo mode; timer/bar behaviour is visually exercised at runtime only).

## Lessons learned

- **`ReferenceEquals` guard in `OnParametersSet` is the right tool for BL-5** — checking reference equality on the parameter object avoids re-triggering the countdown on every parent re-render while still detecting genuine data refreshes. This pattern is reusable for any component that manages a background timer tied to a parameter.
- **Keep formatter logic in `SwebKit.Core`, not in `SwebKit.App`** — pure formatting utilities with no UI dependencies belong in Core; they are easier to test and stay available to future non-Blazor consumers.
- **`OperationCanceledException` must be caught (not re-thrown) inside `PeriodicTimer` loops** — cancellation is expected and normal; re-throwing would surface as an unhandled exception in the Blazor renderer. See CS-2 in pitfalls.

## Follow-up

- TTL display in the key list view (`RedisKeyList`) — out of scope for this pass; deferred to a future list-density improvement.
- Manual in-app walkthrough — can be validated as part of next demo session.

## Archive metadata

- Active folder: `docs/features/active/redis-ttl-visualisation/`
- Archive location: `docs/features/archive/redis-ttl-visualisation/`
- Related archive: `docs/features/archive/redis/`, `docs/features/archive/redis-follow-up/`
