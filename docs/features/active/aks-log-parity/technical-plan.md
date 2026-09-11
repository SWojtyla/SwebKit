# AKS Log Parity & Real Timestamps — Technical Plan

## 1. Timestamps end to end

New `src/SwebKit.Core/Services/LogLineTimestamp.cs` splits a line into its optional
timestamp prefix and its message. It accepts both wire shapes — Kubernetes RFC3339Nano
(`2026-09-09T10:22:30.118456789Z msg`) and the demo client's space-separated form — so one
parser serves both clients. `Split` verifies the prefix actually parses as a date: matching
the shape is not the same as being one, and stripping `2026-13-45T99:99:99Z` would silently
lose part of the message.

`MatchesFilter` is the reason it exists as a helper rather than inline: **every** filter site
must match the message, never the prefix.

- `LogStreamOptions.Timestamps` (`AksModels.cs`), defaulting to **false** so
  `GetPodLogsTool` and `InvestigatePodIssueTool` keep the bare lines they parse.
- `KubernetesAksClient.LogsExec.cs` passes `timestamps: opts.Timestamps` and filters through
  `LogLineTimestamp.MatchesFilter`.
- `DemoAksClient` prepends only when asked, via a new `FormatDemoLogLine`, and emits RFC3339
  with a `Z` to match Kubernetes rather than its old local form. `StreamDeploymentLogsAsync`
  gets the same treatment; its structured `AggregatedLogLine.Timestamp` is now taken from the
  value used to format the line rather than re-parsed out of it, so it stays populated even
  when no in-line prefix was requested.
- `DemoAksClient.LogTimestampPrefixRegex` and `TryExtractLogTimestamp` are deleted — the
  shared helper replaces them.
- `AksEndpoints` gains a `timestamps` query parameter.

### Extracted SSE handler

The stream handler moves out of its `MapGet` lambda into
`internal static AksEndpoints.StreamPodLogsAsync`, following the existing extraction pattern
in `ApiClientEndpoints.PreviewKeyVaultSecretAsync`. This gives the SSE path its first test —
the framing (`data:` per line, `event: done` terminator) is a contract that both log views
and a Playwright stub depend on, and nothing verified the server produced it.

`DemoAksClient.StreamPodLogsAsync` becomes `virtual` so a test can override it, matching
`TestConnectionAsync` and `GetHttpRoutesAsync`, which are already virtual for the same reason.

## 2. Shared frontend pieces

**`web/src/lib/log-window.ts`** — pure, node-testable:

| Export | Purpose |
| --- | --- |
| `parseLogLine` | Mirrors `LogLineTimestamp.Split`; tolerates both wire shapes and a bare line |
| `timestampMs` | Parsed epoch millis, normalising the space separator to `T` |
| `formatLogTimestamp` | `off` / `time` / `full`; keeps milliseconds, which `Intl` will not format |
| `filterLogEntries` | Case-insensitive match over the message only |
| `computeLogWindow` | The paging arithmetic previously inline in `PodLogView` |
| `windowSummary` | The "Showing 301-500 of 500" caption |
| `mergeByTimestamp` | Chronological merge for the multi-pod view |

`mergeByTimestamp` carries the last seen timestamp forward **per pod**, so a stack frame with
no timestamp of its own stays attached to its header instead of drifting to one end of the
merged output. Ties fall back to arrival order, keeping the sort stable.

**`shared/useLogBuffer.ts`** — buffer ref, 10 fps flush, pending count, clear. Source-agnostic
(`push(rawLine, pod?)`), so one stream or twelve feed the same buffer. Lines are parsed on the
way in, so the prefix is split once rather than on every render. This is what fixes the
per-line render in the multi-pod view.

**`shared/useLogWindow.ts`** — filter, pause and paging, including the *anchor*: once the user
pauses or pages back, the total is pinned so newly arriving lines do not slide the window out
from under what they stopped to read. Lifted from `PodLogView` rather than rewritten.

**`shared/LogToolbar.tsx`** — presentational, all state owned by the caller. `leading` and
`trailing` slots carry each view's own controls (the live toggle, container and range selects
for single-pod; nothing for multi-pod). `goLive` is optional because the multi-pod view always
follows.

**`shared/LogOutput.tsx`** — the scroll container. `testId` is a prop because the two ids are a
Playwright contract. Highlighting is unchanged: `getLogLineClass` for the whole line,
memoised `LogLineText` for tokens.

## 3. The two views

`PodLogView` keeps its stream lifecycle, range/container/live state and export, and renders
the shared toolbar and output. Its buffer and paging are now the shared hooks.

`MultiPodLogView` keeps its per-pod `EventSource` map and the full teardown on unmount, and
gains the toolbar, buffer, window and chronological merge. Its export walks every selected
pod and tags each line with its pod name. All pods start selected, re-syncing only when the
pod set itself changes — `pods` is memoised off the URL param upstream.

Both request `timestamps=true`. Both persist the display mode under one shared key, so the
setting follows the user between views.

## 4. Real-cluster fixes and the range selector (2026-09-10)

The manual real-cluster verification this feature's `status.md` had flagged as outstanding
found the multi-pod view never delivered anything. Two independent causes, both fixed:

1. **Missing required query parameter.** `MultiPodLogView.tsx` never sent `previousContainer`
   in its `EventSource` URL. The sidecar bound it as a non-nullable `bool` with no default, so
   ASP.NET's minimal-API model binding 400ed the request *before the handler ran* — invisible to
   every existing test, since the sidecar unit tests call the extracted handler directly
   (bypassing binding) and the e2e assertion only checked an empty-state string, true regardless
   of whether any line ever arrived. Fixed by always sending it (`MultiPodLogView.tsx`,
   `streamParams`) and, for defense in depth, giving every primitive parameter on the route a
   default (`AksEndpoints.cs`) so a future caller that omits one degrades instead of 400ing
   silently. See `docs/pitfalls/react-frontend.md`.
2. **Ambiguous container against a real multi-container pod.** Every real AKS pod runs at least
   one sidecar; `KubernetesAksClient.StreamPodLogsAsync` passed an empty `container` straight to
   `ReadNamespacedPodLogAsync`, which the real Kubernetes API rejects outright (it does not pick
   one) — unlike `DemoAksClient`, which never validated this, so it was invisible in demo mode
   too. Fixed by a new `KubernetesAksClient.ResolveContainer` helper: when `container` is empty,
   fetch the pod and default to its first container, mirroring the precedent
   `StreamDeploymentLogsAsync` already set. This also fixes `GetPodLogsTool` and
   `InvestigatePodIssueTool`, which had the identical bug.

Both bugs together meant the 400 was masking the container-ambiguity bug entirely — fixing (1)
alone would have just traded an invisible 400 for a (still invisible, still swallowed) real-API
exception, which is why the SSE handler now also frames a failure as `event: stream-error`
ahead of `event: done` instead of leaving it unhandled, and both log views surface it instead
of showing "Connecting..." forever.

**Range selector, reversing the original non-goal.** Multi-pod now gets the same `Last 5m / 10m
/ 1h / All` control as single-pod (not `Previous container` — a pod's own previous instance
isn't a correlatable concept across multiple pods). `web/src/components/aks/shared/logRange.ts`
is the shared `LogRange` type and `rangeOptions`/`multiPodLogRangeOptions` single source of
truth for the `since` mapping; both views now import from it instead of `PodLogView` owning a
private copy.

## Files

| Area | Files |
| --- | --- |
| New (backend) | `src/SwebKit.Core/Services/LogLineTimestamp.cs` |
| New (frontend) | `web/src/lib/log-window.ts`, `shared/useLogBuffer.ts`, `shared/useLogWindow.ts`, `shared/LogToolbar.tsx`, `shared/LogOutput.tsx`, `shared/logRange.ts` |
| Backend | `AksModels.cs`, `DemoAksClient.cs`, `KubernetesAksClient.LogsExec.cs`, `src-sidecar/Endpoints/AksEndpoints.cs` |
| Frontend | `PodLogView.tsx`, `MultiPodLogView.tsx` |
| Docs | `docs/pitfalls/react-frontend.md` (SSE section: required-parameter footgun, `event: error` collision), `docs/pitfalls/index.md` |
