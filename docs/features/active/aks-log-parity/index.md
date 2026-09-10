---
status: Review
---

# AKS Log Parity & Real Timestamps

## Scope

The multi-pod log view opened with every pod switched **off** — a correlation view that
correlated nothing until each pod was clicked — and had almost none of the single-pod
view's controls: no filter, no export, no copy, no clear, no pause, no line count.

That gap is a regression that was never written down. The toolbar is a port of an archived
Blazor feature (`docs/features/archive/2026/tester-feedback-ux-polish/frontend.md`, section
C1); the React port of the *multi-pod* view dropped it. Rebuilding it separately inside
`MultiPodLogView` would have set up the same drift again, so the toolbar, buffering and
windowing now live in shared code that both views consume.

Three further defects surfaced while investigating and are fixed in the same pass:

1. **Timestamps were arrival times, not log times.** `MultiPodLogView` stamped each line
   with `new Date().toLocaleTimeString()` as the browser received it. For correlating two
   pods that is actively misleading — jitter reorders lines that were seconds apart.
   `KubernetesAksClient` never passed the `timestamps` argument, which KubernetesClient
   19.0.2 supports, so the pod's own timestamp was simply discarded.
2. **The two AKS clients disagreed about line shape.** `DemoAksClient` unconditionally
   prepended `yyyy-MM-dd HH:mm:ss.fff`; the real client emitted bare text. Any parser was
   therefore wrong against one of them, and nothing controlled it.
3. **A text filter matched the timestamp.** The filter was applied to the whole line, so
   once a prefix existed, filtering for `2026` returned every line. Present in the sidecar
   endpoint, the real client and the demo client.

A subsequent real-cluster manual check (this feature's own outstanding verification item)
found the multi-pod view still delivered nothing at all, for two independent reasons: it never
sent a required query parameter (`previousContainer`), which failed every request outright
before the handler ran; and, once that's fixed, a real multi-container pod (every pod on AKS
runs at least one sidecar) rejects an unqualified container request that the demo client never
validated. Both are fixed — see `technical-plan.md` §4 — along with surfacing a stream failure
instead of leaving the panel showing "Connecting..." forever.

## Outcomes

- Both log views render one toolbar, one buffer and one windowing model, so a control added
  to either appears in both.
- Every pod streams from the moment the correlation view opens.
- Lines carry the timestamp the container emitted them at, and the multi-pod view orders by
  it — the only thing that makes correlation trustworthy.
- Timestamp display is off / time / full, persisted as a view preference.
- The multi-pod view no longer re-renders per received line, per pod.
- Multi-pod log streams actually deliver against a real cluster: every request carries its
  required query parameters, and an ambiguous container resolves to the pod's first one
  instead of being rejected outright.
- A stream failure is surfaced (per-pod, in the panel) instead of leaving the view stuck on
  "Connecting..." indefinitely.
- Multi-pod gets the same `Last 5m / 10m / 1h / All` range selector single-pod has, sharing one
  source of truth for the range-to-`sinceSeconds` mapping.

## Non-goals

- Virtualising the log output. The window is capped at 200 rendered lines, which is what
  keeps the DOM small today.
- Multi-pod does not get the `Previous container` option — a pod's own previous instance isn't
  a concept that correlates across multiple pods, unlike the range selector above, which
  reverses this folder's original decision to omit it (the user asked for it back once the
  underlying streaming bug was found and fixed).

## Dependencies

- `web/src/lib/logLevel.ts` and `web/src/lib/logHighlight.ts` — existing highlighting,
  reused unchanged.
- `web/src/components/aks/shared/LogLineText.tsx` — memoised token renderer.
- `web/src/lib/stores/panel-preferences.ts` — view-preference persistence.
- KubernetesClient 19.0.2 `ReadNamespacedPodLogAsync(..., timestamps:)`.

## Traceability

- Technical plan: `technical-plan.md`
- Test plan: `test-plan.md`
- Status: `status.md`
- Prior art: `docs/features/archive/2026/tester-feedback-ux-polish/frontend.md` (section C1)
- Pitfalls: `docs/pitfalls/react-frontend.md` (new "Server-sent events" section),
  `docs/pitfalls/blazor-maui.md` BL-7 and BL-8
