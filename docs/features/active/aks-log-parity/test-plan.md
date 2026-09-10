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

`AksLogStreamEndpointTests` (6 cases, 1 added 2026-09-10) — the SSE path had no coverage at
all. Frames each line as `data: …` and terminates with `event: done`; sets
`text/event-stream`; passes `Timestamps` through to the client; filters on the message; does
**not** filter on the timestamp prefix, and still terminates when the filter excludes
everything. The added case: when the underlying client throws mid-stream, the handler emits
`event: stream-error` (with the message) ahead of `event: done` instead of throwing out of the
handler — the real-cluster fix's regression guard.

### `tests/SwebKit.Kubernetes.Tests` — `dotnet test` (new, 2026-09-10)

`KubernetesAksClientTests` gains 5 cases for `ResolveContainer`: empty/null requested resolves
to the first available container; a non-empty requested value passes through unchanged, even
when it isn't in the available list (this method decides whether to substitute a default, not
whether the name is valid — the API call itself is the validation); an empty available list
resolves to empty string rather than throwing.

## End-to-end (`npm run test:e2e`, Chromium)

`web/e2e/aks-deferred.spec.ts`

| Scenario | Expected |
| --- | --- |
| Multi-pod panel opens | Every `multi-pod-toggle-*` is already selected; `log-line-count` moves off `0 lines` (not just the absence of the "Select pods..." placeholder — that string is also absent while merely "Connecting..." forever, which is exactly the state the missing-`previousContainer` bug got stuck in); no `multi-pod-log-error`; `log-filter-input`, `log-export-btn` and `log-timestamp-select` are present; filtering to a non-existent string reports `0 lines` |
| Range selector (new, 2026-09-10) | `multi-pod-range-select` is visible, defaults to `5m`, and has 4 options (no `Previous container`); after narrowing to 2 pods (avoiding the unrelated six-connections-per-origin cap) and clearing, switching to `1h` tears down and reopens every stream and `log-line-count` moves back off `0 lines`; no `multi-pod-log-error` |

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
- `dotnet test tests/SwebKit.Sidecar.Tests --filter AksLogStream` — 6 passed.
- `dotnet test tests/SwebKit.Kubernetes.Tests --filter ResolveContainer` — 5 passed.
- `dotnet test tests/SwebKit.Kubernetes.Tests` (full) — 129 passed.
- `dotnet test tests/SwebKit.Sidecar.Tests` (full) — 255 passed.
- `dotnet test tests/SwebKit.Agents.Tests --filter PodLogs|InvestigatePod` — 5 passed (unaffected: demo mode never hit the required-parameter or container-ambiguity paths).
- `npx playwright test e2e/aks-deferred.spec.ts e2e/aks-ux.spec.ts` — 14 passed, including the
  strengthened and new multi-pod assertions above.

One flake worth recording: on the first cold run after a rebuild, `aks-deferred.spec.ts`
"multi-pod logs button opens panel" timed out waiting for the panel. It passed in isolation
and on every subsequent run of the same pair. Cold-start timing, not a behavioural failure —
but if it recurs, the first test in that file is the one to instrument.

A live diagnostic run (`page.on("request"/"response")` against the actual dev sidecar, not a
stub) is what actually found the missing-`previousContainer` bug: every `/logs/stream` request
for every pod returned 400, including the very first ones on initial panel open, both before
and after switching range. Demo mode's weak assertion in the pre-existing test had been masking
this from the start.

## Manual verification

Against a real cluster, since Playwright's log stream runs through the real demo client, not a
Kubernetes API:

1. Open multi-pod logs for two pods of one deployment, at least one of which runs a sidecar
   (istio-proxy, linkerd, the Azure Monitor agent — routine on AKS). Confirm logs actually
   arrive: this is the exact scenario that previously hung forever twice over (a 400 from the
   missing `previousContainer` parameter, then, once that's fixed, a real API rejection from an
   unqualified multi-container request) — **owner: Sebastien**.
2. Lines must interleave by the pods' own timestamps, not by arrival — check a burst where both
   pods log at once.
3. Switch the timestamp display through off / time / full; confirm it survives reopening the
   panel and appears the same way in the single-pod view.
4. Filter, pause, page back with Older, then Latest. Confirm paused lines are counted and
   arrive on resume rather than being dropped.
5. Export from the multi-pod view and confirm the file contains every selected pod, tagged.
6. Exercise the new range selector (5m/10m/1h/All) and confirm the sidecar-side stream failure
   banner appears if a pod is deleted mid-correlation, instead of a silent indefinite hang.

Point the dev run at a sandbox `SWEBKIT_APPDATA_ROOT` first — `scripts/tauri/run-dev.ps1` sets
none, so it shares the installed app's real data.
