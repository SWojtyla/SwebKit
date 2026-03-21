# Feature Overview — Redis TTL Visualisation

---

title: "Redis TTL Visualisation"
owner: ""
status: "Planned"
created: "2026-03-21"
updated: "2026-03-21"

---

## Goal

Replace the raw numeric TTL display in `RedisKeyDetail` with a human-readable format and a visual expiry indicator, making it immediately obvious how much time a key has remaining without mental arithmetic.

## Value

A TTL of `8547` seconds is meaningless at a glance. "Expires in 2h 22m" paired with a colour-coded bar (green → amber → red as expiry approaches) gives developers instant situational awareness when debugging cache poisoning, stale data issues, or key expiry behaviour.

## Scope

### In scope

1. **Human-readable TTL label** — Format the raw TTL value as a human string:
   - > 1 hour: "Xh Ym remaining"
   - 1–60 minutes: "Xm Ys remaining"
   - < 60 seconds: "Xs remaining" (rendered in amber/red)
   - No expiry (TTL = -1): "No expiry"
   - Expired/missing (TTL = -2): "Key has no TTL / already expired"

2. **Expiry progress bar** — A thin horizontal bar below the TTL label. The bar fills from right to left as the key approaches expiry. Colour thresholds:
   - TTL > 20% of original (or > 5 min): `--color-success` (green)
   - TTL 5–20% (or 1–5 min): `--color-warning` (amber)
   - TTL < 5% (or < 1 min): `--color-error` (red)
   - When original TTL is unknown (key was pre-existing, not just set), show a simpler linear bar based on absolute remaining seconds capped at a display maximum of 1 hour.

3. **Live countdown** — While the detail panel is open, the TTL label and bar update every second using a `System.Threading.Timer` or `PeriodicTimer`. The update is lightweight (no server round-trip — decrement client-side).

4. **TTL edit shortcut** — The existing TTL edit field (if present in `RedisKeyDetail`) remains. If not yet present, add a small "Set TTL" inline action that opens a popover with a seconds/minutes/hours input and a "No expiry" option.

### Out of scope

- TTL display in the key list view (`RedisKeyList`) — this is a list-density concern for a future pass
- Server-side TTL polling (the countdown is client-side; actual TTL is re-fetched on panel open/refresh)
- TTL history or trend tracking

## Dependencies

- `RedisKeyDetail.razor` — primary change target
- `IRedisClient.GetKeyDetailAsync` — must return TTL as part of `RedisKeyInfo` (already expected to be present)
- CSS design tokens: `--color-success`, `--color-warning`, `--color-error` (already defined in `app.css`)

## Risks

- Client-side countdown drift: the timer counts down seconds, but the actual server TTL decreases independently. On long-lived open panels, the display may drift. Acceptable for MVP; a background refresh of TTL every 30 seconds can correct it.
- Original TTL is not stored by Redis: to render a meaningful percentage bar, the original TTL must be captured at the moment the panel opens. If the key already existed, only the remaining TTL is known; the bar should degrade gracefully (absolute-based display, not percentage-based).

## Related documents

- Architecture: `docs/architecture/functionalities/redis.md` (update after implementation)
- Pitfalls: `docs/pitfalls/blazor-maui.md`

## Quick links

- Status: `status.md`
- Frontend plan: `frontend.md`
- Test plan: `test-plan.md`
