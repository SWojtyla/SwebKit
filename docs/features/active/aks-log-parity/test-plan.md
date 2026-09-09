# AKS Log Parity & Real Timestamps — Test Plan

## Unit tests

### `web` — vitest (`npm run test:unit`)

`web/src/lib/log-window.test.ts` (new, 33 cases)

- `parseLogLine` — both wire shapes; an offset instead of `Z`; a line with no prefix; a
  prefix that is date-*shaped* but not a date; an empty message after the timestamp; an
  empty line; a timestamp appearing mid-line, which must **not** be treated as a prefix.
- `timestampMs` — null for a bare line; both shapes resolve to the same instant.
- `formatLogTimestamp` — each mode; milliseconds retained (they are what separates two
  pods); sub-100ms fractions padded; nothing rendered for a line with no timestamp.
- `filterLogEntries` — blank term; case-insensitive; cannot match a timestamp.
- `computeLogWindow` — newest page; stepping back; clamping past the end; empty log; fewer
  lines than a page; an exact multiple of the page size; no negative window.
- `windowSummary` — one-based counting; the empty case.
- `mergeByTimestamp` — ordering by log time rather than arrival; stable on ties; a stack
  frame staying attached to its header; carry-forward being **per pod**, not global;
  entries with no timestamps left in arrival order; the empty list.

### `tests/SwebKit.Core.Tests` — `dotnet test`

`LogLineTimestampTests` (new, 13 cases) — the same split/parse matrix as the frontend, plus
`MatchesFilter` matching the message, ignoring the prefix, being case-insensitive, treating
an empty filter as match-all, and still working on a line with no timestamp.

`DemoAksClientTests` (4 added) — no prefix unless `Timestamps` is set; a prefix on every line
when it is; a filter on the year returning **nothing** (the regression guard); a filter on
message text still matching.

### `tests/SwebKit.Sidecar.Tests` — `dotnet test`

`AksLogStreamEndpointTests` (new, 5 cases) — the SSE path had no coverage at all. Frames each
line as `data: …` and terminates with `event: done`; sets `text/event-stream`; passes
`Timestamps` through to the client; filters on the message; does **not** filter on the
timestamp prefix, and still terminates when the filter excludes everything.

## End-to-end (`npm run test:e2e`, Chromium)

`web/e2e/aks-deferred.spec.ts`

| Scenario | Expected |
| --- | --- |
| Multi-pod panel opens | Every `multi-pod-toggle-*` is already selected; logs appear with no further click; `log-filter-input`, `log-export-btn` and `log-timestamp-select` are present; filtering to a non-existent string reports `0 lines` |

`web/e2e/aks-ux.spec.ts`

| Scenario | Expected |
| --- | --- |
| Toolbar filter / pause / clear | Filter narrows the count and the output; clearing it restores; pause toggles its label; clear empties the buffer |
| Timestamp toggle | Survives a reload, being a view preference rather than per-stream state |

Both added because the single-pod toolbar previously had **no** coverage, which is
uncomfortable now that one implementation serves both views — a regression would reach both.

Existing specs that must keep passing unchanged: `aks-ux.spec.ts` "pod logs are syntax
highlighted" (asserts `.log-tok-*` and `.log-level-frame` inside `log-output`, against a
stubbed SSE body whose framing the extracted handler must still match), `aks.spec.ts` "pod
detail panel opens on pod click" (`pod-log-view`), and `aks-deferred.spec.ts` "multi-pod logs
button opens panel" (`multi-pod-log-view`).

## Results

- `npx tsc -b` — clean.
- `npm run test:unit` — 267 passed (18 files).
- `dotnet test tests/SwebKit.Sidecar.Tests --filter AksLogStream` — 5 passed.
- `npx playwright test e2e/aks.spec.ts e2e/aks-ux.spec.ts e2e/aks-deferred.spec.ts` — all
  passed (16 before the new tests, 13 in the ux+deferred pair after).

One flake worth recording: on the first cold run after a rebuild, `aks-deferred.spec.ts`
"multi-pod logs button opens panel" timed out waiting for the panel. It passed in isolation
and on every subsequent run of the same pair. Cold-start timing, not a behavioural failure —
but if it recurs, the first test in that file is the one to instrument.

## Manual verification

Against a real cluster, since Playwright's log stream is stubbed and never sees real ordering:

1. Open multi-pod logs for two pods of one deployment. Lines must interleave by the pods' own
   timestamps, not by arrival — check a burst where both pods log at once.
2. Switch the timestamp display through off / time / full; confirm it survives reopening the
   panel and appears the same way in the single-pod view.
3. Filter, pause, page back with Older, then Latest. Confirm paused lines are counted and
   arrive on resume rather than being dropped.
4. Export from the multi-pod view and confirm the file contains every selected pod, tagged.

Point the dev run at a sandbox `SWEBKIT_APPDATA_ROOT` first — `scripts/tauri/run-dev.ps1` sets
none, so it shares the installed app's real data.
