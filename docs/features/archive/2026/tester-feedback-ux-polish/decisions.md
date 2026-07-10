# Decisions — Tester Feedback UX Polish

Design choices to settle before implementation. Each needs a maintainer call where noted.

---

## DEC-1 — Exit button semantics (item #5)

**Question:** Should the window close (×) truly exit, or keep the current close-to-tray behavior?

**Decision (maintainer-confirmed):** **(a)** — the window close (×) **truly exits the app**.
Minimize continues to go to the tray (so the background alert monitor keeps running when the user
intentionally minimizes), but × performs a full, clean shutdown.

**Consequence:**

- `OnAppWindowClosing()` must **not** intercept × to hide-to-tray. It must allow the real close and
  run the same clean-shutdown path as the tray "Exit" menu (`ExitApplication()`): dispose monitors,
  unregister the tray icon, release the single-instance mutex (DEC-2), and end the process.
- `OnAppWindowChanged()` minimize handling stays (minimize → hide to tray).
- `ShouldInterceptClose` semantics change: it no longer redirects × to tray. If the flag exists only
  to force tray-on-close, retire/repurpose it and verify nothing else depends on it.
- Single-instance (A2/DEC-2): because minimize still hides to tray, relaunch must focus the existing
  hidden instance rather than starting fresh.

**Status:** Confirmed — implement option (a).

---

## DEC-2 — Single-instance mechanism (item #7)

**Question:** How to enforce one instance for the unpackaged Windows build?

**Decision:** Named `Mutex` (acquired earliest in Windows startup) + a lightweight named-pipe /
`WM_COPYDATA` signal to restore+focus the primary instance; second process exits before MAUI
initializes. `AppInstance` redirection is the packaged-app idiom but the current build is
`WindowsPackageType=None`, so mutex+pipe is the reliable path.

**Consequence:** Mutex must be released on the true-exit path (`ExitApplication()`), and the
restore signal must reuse the tray lifecycle restore so behavior matches A1.

**Status:** Recommended; confirm mechanism during implementation.

---

## DEC-3 — Credential diagnostic: names only, never secrets (item #1)

**Decision (hard rule):** The Service Bus connection diagnostic exposes only non-secret
identifiers — endpoint host, SAS key **name**, auth method, and the credential-source label
(secret-reference name / config key). It must never contain the SAS key value, full connection
string, or a token.

**Enforcement:** A focused unit test asserts the diagnostic payload contains the key name +
endpoint and does **not** contain the secret value. Applies to UI, error state, and any logging.

**Status:** Fixed — non-negotiable per the repo's secret-by-reference model.

---

## DEC-4 — Toast reliability: fallback over gating (item #6)

**Decision:** Do not hard-gate alerting on a strict capability check (risk of false negatives
disabling working toasts). Instead: attempt the toast, and on failure/unavailability always raise
the in-app notification and record a one-time diagnostic hint. In-app notification is the reliable
baseline; the OS toast is a best-effort enhancement.

**Status:** Fixed.

---

## DEC-5 — Splitter lag fix approach (item #4)

**Decision:** Root cause is a CSS `transition` on pane width animating each drag step. Fix by
disabling transitions during drag (a `.resizing`/`.active` class → `transition: none`), optionally
re-enabling a short settle transition only on mouseup. Prefer this over rewriting the drag to
`transform: scaleX()` (which complicates content layout). Add `requestAnimationFrame` coalescing
only if repaint queuing persists after the transition fix.

**Status:** Fixed; validate no transition remains during drag across all workspaces.

---

## DEC-6 — Redis "select all": single scoped control (item #3)

**Decision:** Collapse the two overlapping affordances (toolbar "Select All Loaded" button + list
"all visible" checkbox) into one header checkbox co-located with the key list, labeled by its true
scope, with a none/some/all tri-state. Remove the duplicate. Placement/labeling change only — no
change to the selection data model.

**Status:** Recommended; confirm final label/scope wording with maintainer.
