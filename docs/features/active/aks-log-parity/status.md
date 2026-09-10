---
status: Review
---

# AKS Log Parity & Real Timestamps — Status

- **Current phase:** Review — implemented and validated, awaiting sign-off.
- **Reported by:** Sebastien: "when selecting it by default all are off when they should all
  be on and secondly it should support the same features as the single pod logs."
- **Implementation PR:** not raised yet.

## Validation

| Gate | Result |
| --- | --- |
| `npx tsc -b` | clean |
| `npm run test:unit` | 267 passed (18 files) |
| `dotnet build src-sidecar` | succeeded |
| `dotnet test tests/SwebKit.Sidecar.Tests --filter AksLogStream` | 6 passed (was 5) |
| `dotnet test tests/SwebKit.Core.Tests --filter LogLineTimestamp\|DemoAksClient` | 73 passed |
| `dotnet test tests/SwebKit.Kubernetes.Tests --filter ResolveContainer` (2026-09-10) | 5 passed |
| `npx playwright test e2e/aks{,-ux,-deferred}.spec.ts` | passed |

**2026-09-10 update:** manual real-cluster verification (below) found the multi-pod view
delivered nothing at all — two bugs, both fixed. See `index.md` and `technical-plan.md` §4.

## Definition of Done

- [x] Every pod streams from the moment the correlation view opens.
- [x] One toolbar implementation serves both log views (filter, timestamps, pause, paging,
      copy, export, clear, line count).
- [x] Lines carry the container's own timestamp; multi-pod output is ordered by it.
- [x] Timestamp display mode persisted and shared between the two views.
- [x] Multi-pod no longer renders per received line (pitfall BL-8).
- [x] Text filters match the message, not the timestamp prefix, at every layer.
- [x] Unit tests for every new pure function; first-ever tests for the SSE endpoint.
- [x] E2E for the multi-pod default and for the single-pod toolbar, which had none.
- [x] SSE pitfalls written down in `docs/pitfalls/react-frontend.md`.
- [x] Multi-pod streams actually deliver: the missing `previousContainer` query parameter (a
      400 on every request, in demo and real alike) and the real-cluster container-ambiguity
      rejection are both fixed, with regression tests and two new pitfalls entries.
- [x] A stream failure is surfaced in the panel instead of an indefinite "Connecting...".
- [x] Multi-pod gets the range selector back (reversing the original non-goal, per request).
- [ ] Manual verification against a real cluster (see `test-plan.md`) — **owner: Sebastien**.
      Specifically: confirm logs now arrive for a pod with a sidecar container, which is the
      exact scenario that was broken. Playwright's demo-mode run confirmed the fix at the
      protocol level (real 200s where every request 400ed before), but cannot check real
      chronological ordering or the actual Kubernetes API's container-resolution behavior.
- [ ] Aikido security scan per `docs/security/aikido-mcp-scan.md`.

## Behaviour changes worth knowing

1. **`DemoAksClient` no longer prepends a timestamp unless asked.** It previously did so
   unconditionally while the real client never did. Demo-mode output from `GetPodLogsTool`
   and `InvestigatePodIssueTool` is therefore now bare, matching real mode. This is a
   consistency fix, but it *is* a visible change to agent tool output in demo mode.
2. **`DemoAksClient.StreamPodLogsAsync` is now `virtual`**, so a test can substitute it —
   the same reason `TestConnectionAsync` and `GetHttpRoutesAsync` already are.
3. **The demo timestamp format changed** from `yyyy-MM-dd HH:mm:ss.fff` to RFC3339 with a
   `Z`, so demo and real agree. Both shapes still parse, so nothing breaks on old data.

## Follow-ups (not in this feature)

1. `useAksPodLogs` in `web/src/lib/hooks/useAks.ts` is dead code — nothing imports it. It
   fetches the non-streaming `/logs` endpoint. Either wire it up or delete it.
2. The log output is not virtualised. The 200-line window keeps the DOM small, so this only
   matters if the page size grows.
3. `StreamDeploymentLogsAsync` duplicates the fan-out logic that `MultiPodLogView` now does
   client-side. One of the two is redundant.
4. The demo log corpus exercises far less of `tokenizeLogLine` than the Playwright fixture
   does — no JSON payloads, no inner-exception arrows, no source locations. Clicking through
   demo mode is not a check on highlighting.
