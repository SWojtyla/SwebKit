# Phase 4 — AKS Depth

**Status:** ⏳ Pending (starts after Phase 3 complete)
**Goal:** Full AKS debugging workflow — live log tailing with pod-restart resilience, port-forward
management with a tunnels panel, embedded terminal (xterm.js), and real-time pod watch.

---

## 1. Live Log Tailing

**Component:** `Components/Aks/PodLogView.razor` (major enhancement)

- [ ] "Live: ON" toggle switches from historical to `IAsyncEnumerable<string>` streaming
  - Calls `IAksClient.StreamPodLogsAsync(ns, pod, container, {Follow: true}, ct)`
  - Consumes async stream on background thread, pushes lines to a `Channel<string>`
  - UI reads from channel with a 100ms-throttled `StateHasChanged()` timer
- [ ] Ring buffer: keep last 10,000 lines in memory; older lines dropped; warning shown: "Buffer full — oldest lines removed"
- [ ] Multi-container support: container selector dropdown (defaults to first container)
- [ ] **Pod restart handling:**
  - Detect when the pod UID changes (poll pod status every 5s while tailing)
  - Banner: "Pod restarted. Showing logs from new instance. [Switch] [Keep previous pod]"
  - "Switch" → reconnects stream to new pod; previous logs still visible above separator line
  - "Keep previous pod" → shows logs from terminated pod (historical only, `Follow: false`)
- [ ] Client-side log filtering:
  - Text filter: real-time highlight of matching lines (not drop)
  - Log level filter: parse common formats (Serilog JSON `{"@l": "Error"}`, ASP.NET console `[ERR]`)
  - Correlation ID: highlight matching lines in bold amber
- [ ] Tail controls: `[Last 200 lines]` `[Last 1000 lines]` `[From beginning]` `[Live]`
- [ ] Export: `[Copy visible lines]` / `[Save to file]`

---

## 2. Multi-Pod Tailing

- [ ] "Open parallel log" button on pod view → opens second tab alongside
- [ ] Or: "Tail all pods for this deployment" → opens single combined view
- [ ] Combined view: each line prefixed with pod name (short hash), color-coded per pod
  - Up to 5 pods simultaneously (UI limit; warn beyond)
- [ ] Filter applies across all pods simultaneously
- [ ] Pod health indicator per pod in header: Running / CrashLoopBackOff / Restarting

---

## 3. Port-Forward Management

**Component:** `Components/Aks/PortForwardPanel.razor` + status bar integration

**Service:** `Services/PortForwardService.cs` — manages list of `PortForwardSession` objects

- [ ] "Port Forward" button on deployment/service row → dialog:
  ```
  Resource: order-api (Deployment)
  Local port:  [ 8080 ]
  Remote port: [ 80   ]
  [ Start Port Forward ]
  ```
- [ ] Implementation: spawn `kubectl port-forward deployment/order-api 8080:80 -n order-platform` as a child process
  - Process managed by `PortForwardService`, stored with `SessionId`
  - Stdout monitored for "Forwarding from..." confirmation message
- [ ] Status bar: shows active tunnels as chips: `[→ 8080:order-api] [X]`
  - Click chip → copies `http://localhost:8080` to clipboard, shows toast notification
- [ ] **Tunnels panel** (flyout from status bar): table of all active/past sessions:
  - Session | Local Port | Remote | Resource | Started | Status | Actions (Copy URL, Stop)
- [ ] **Session persistence:** active sessions saved to `ui-state.json`; on app restart:
  - Toast: "2 port-forwards from previous session. Reconnect? [Yes] [No]"
  - `[Yes]` → auto-starts child processes for saved sessions
- [ ] Session cleanup: sessions removed from `ui-state.json` when stopped

---

## 4. Embedded Terminal (xterm.js)

**Component:** `Components/Aks/TerminalView.razor`

- [ ] Load xterm.js from `wwwroot/js/xterm/xterm.js` + `xterm.css`
- [ ] JSInterop module: `wwwroot/js/terminalInterop.js`
  - `initTerminal(elementId)` — create `Terminal` instance, attach to DOM element
  - `writeToTerminal(elementId, text)` — write string to terminal
  - `onTerminalInput(elementId, dotNetRef, methodName)` — register input callback
- [ ] .NET side: `TerminalView.razor` calls `initTerminal` in `OnAfterRenderAsync`
- [ ] Process bridge:
  - Start `kubectl exec -it [pod] -n [ns] -- /bin/sh` as a child process
  - Read stdout/stderr asynchronously → `writeToTerminal`
  - Terminal input callback → write to process stdin
- [ ] Resize handling: `IObserver` on element size changes → call `kubectl` with correct terminal size (`COLUMNS`/`LINES`)
- [ ] "Open terminal" button on pod row in WorkloadOverview; opens as a new tab
- [ ] External fallback: "Open in Windows Terminal" button alongside embedded terminal

---

## 5. Real-Time Pod Watch

- [ ] `IAksClient` extension: `WatchPodsAsync(ns, labelSelector, CancellationToken)` returning `IAsyncEnumerable<WatchEvent<V1Pod>>`
  - Uses `KubernetesClient` watch API: `client.CoreV1.ListNamespacedPodWithHttpMessagesAsync(watch: true)`
- [ ] `WorkloadOverview.razor` subscribes when "Watch: ON" toggle is enabled
- [ ] Pod status column updates in real time without full page refresh
- [ ] Restarts counter increments live when a pod restarts
- [ ] New pod appears / old pod disappears as deployments scale
- [ ] Watch reconnects automatically on disconnect (exponential backoff, max 30s)

---

## 6. AKS Events Timeline

**Component:** `Components/Aks/EventsTimeline.razor`

- [ ] Chronological list of all events in the namespace, last 1 hour
- [ ] Columns: Time | Type (Normal/Warning) | Reason | Object | Message
- [ ] Warning events: orange/red row background
- [ ] Filter: by object name (deployment/pod), by event type, by reason
- [ ] Auto-refresh: poll every 30s (Kubernetes events are not watchable via standard watch API)
- [ ] Accessible from AKS page sidebar or "Events" tab alongside Workload Overview

---

## 7. Cross-Link: AKS → Observability

- [ ] "Find logs" button on deployment row → opens Observability log tab pre-filtered:
  - For AppInsights: filter by `cloud_RoleName = [deployment name]` or `cloud_RoleInstance contains [deployment name]`
  - For OTLP: filter by `service.name = [deployment name]`
- [ ] Navigation: `NavigationManager.NavigateTo("/observability?serviceFilter=[name]")`
- [ ] Observability page reads `serviceFilter` query param on init → sets property filter in filter bar

---

## Acceptance Criteria (Phase 4 Complete)

- [ ] Start live tail on a pod → logs stream in near real-time
- [ ] Kill the pod → "Pod restarted" banner appears → click Switch → continue tailing from new pod
- [ ] Start port-forward on order-api:80 → localhost:8080 → click chip → `http://localhost:8080` in clipboard
- [ ] Restart app → "Reconnect port-forwards?" prompt → click Yes → port-forward active again
- [ ] Open embedded terminal in a pod → `ls /` works in the terminal → can run commands
- [ ] "Watch: ON" mode → scale a deployment from 1 to 2 replicas externally → new pod appears in table
- [ ] "Find logs" on a deployment → Observability tab opens with correct service name filter
