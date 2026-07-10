# Backend / Platform Module — Tester Feedback UX Polish

Covers Windows platform lifecycle, notifications, Service Bus credential surfacing, and the
Kubernetes RBAC warning. UI-only items are in `frontend.md`. Item numbers match `index.md`.

---

## A1 (#5) — Minimize vs Exit semantics

**Files**

- `src/SwebKit.App/Platforms/Windows/WindowsTrayLifecycleService.cs` (~200-250) — `HideToTray()`,
  `ExitApplication()`, `OnAppWindowClosing()` (~228), `OnAppWindowChanged()` (~245), tray menu
- `src/SwebKit.App/Platforms/Windows/App.xaml.cs`

**Current**

- App uses default WinUI chrome. Close (×) is intercepted in `OnAppWindowClosing()`: if
  `ShouldInterceptClose`, it calls `HideToTray()` instead of closing. Native minimize
  (`OverlappedPresenterState.Minimized`) also hides to tray. Tray menu offers "Restore" / "Exit".
  Net effect: the top-right × behaves as "minimize to tray", so a user expecting it to exit is
  surprised, and there is no obvious real-exit affordance in the window itself.

**Change (DEC-1 confirmed: option (a) — × truly exits)**

- **× exits the app.** Remove the close-to-tray interception in `OnAppWindowClosing()` — do not
  redirect × to `HideToTray()`. Instead, run the same clean-shutdown path as the tray "Exit" menu
  (`ExitApplication()`): dispose monitors, unregister the tray icon, release the single-instance
  mutex (A2/DEC-2), and end the process. No lingering background process after ×.
- **Minimize still hides to tray.** Keep the `OnAppWindowChanged()` minimize handling
  (`OverlappedPresenterState.Minimized` → `HideToTray()`) so the background alert monitor keeps
  running on an intentional minimize.
- **`ShouldInterceptClose`** no longer forces tray-on-close. If it exists only for that purpose,
  retire/repurpose it; verify nothing else depends on it (search usages before removing).
- Restore path (tray Restore + single-instance activation, A2) must bring the minimized/hidden
  window back cleanly.

**Guard** — this code can trap the app hidden or make it unclosable. Manual Windows verification is
mandatory: × → process fully gone (no tray icon, no process); minimize → tray → restore;
tray Exit → process gone; relaunch-while-minimized focuses the existing instance (A2).

---

## A2 (#7) — Single instance enforcement

**Files**

- `src/SwebKit.App/Platforms/Windows/App.xaml.cs` — startup (no guard today)
- `src/SwebKit.App/MauiProgram.cs` — startup (no guard today)
- Tie-in: `WindowsTrayLifecycleService.cs` for restore/focus of the existing instance

**Current**

- No mutex / named-pipe guard. Running the app again (e.g. while it is hidden in the tray) starts a
  second independent instance.

**Change**

1. Acquire a named `Mutex` (stable app-scoped name) as early as possible in Windows startup.
2. If the mutex is already held, this is a second launch: signal the first instance to
   restore+focus its window (named pipe or `WM_COPYDATA`/AppInstance redirection), then exit the
   second process before the MAUI app spins up.
3. In the primary instance, handle the activation signal by calling the tray lifecycle
   restore/focus path (reuse `WindowsTrayLifecycleService` restore).
4. Release the mutex on true exit (`ExitApplication()`), including the tray-Exit path.

**Design note** — WinAppSDK `AppInstance.GetCurrent().GetActivatedEventArgs()` / `RedirectActivationToAsync`
is the idiomatic redirection mechanism for packaged apps; for the current unpackaged
(`WindowsPackageType=None`) build, a named Mutex + named pipe is the reliable path. Capture the
chosen mechanism in `decisions.md` DEC-2.

---

## B1 (#6) — Alerting / Windows toast reliability

**Files**

- `src/SwebKit.App/Platforms/Windows/WindowsToastNotificationService.cs` (~1-100) — `ShowAlert()`,
  `ShowPodAlert()`; failures currently only `_logger.LogWarning()` (~46)
- `src/SwebKit.App/Platforms/Windows/App.xaml.cs` (~16) — `RegisterAumidInRegistry` +
  `SetCurrentProcessExplicitAppUserModelID`
- `src/SwebKit.App/Services/AlertMonitorService.cs` — publishes `AlertFiredEvent` → toast
- `src/SwebKit.App/MauiProgram.cs` (~91, 170) — DI for `IWindowsNotificationService`,
  `IAlertMonitorService`

**Current**

- Toasts go through `ToastNotificationManager.CreateToastNotifier("SwebKit.App")` after AUMID
  registration. Failures are swallowed (warning log only) to protect the monitoring loop. No
  capability/permission probe. On a machine where system toasts are disabled (Focus Assist / Do
  Not Disturb, notifications off for the app, missing AUMID registration, or group policy), the
  toast fails silently — explaining why one colleague saw alerts and another did not.

**Change**

1. **Startup capability probe** — after AUMID registration, verify the notifier can be created and
   record a capability state (available / unavailable + reason). Do not hard-block on a strict
   check (avoid false negatives) — prefer "attempt, observe, record".
2. **Never-silent fallback** — when a toast fails or capability is unavailable, always raise the
   existing in-app notification (Notifications panel / history) so the alert is not lost. The
   in-app path becomes the reliable baseline; toast is the enhancement.
3. **One-time diagnostic** — surface a single, dismissible hint when toasts are unavailable
   ("System notifications appear disabled for SwebKit — alerts will show in-app. Enable in Windows
   Settings → Notifications."), gated by a "don't show again" flag.
4. Keep the loop-protective try/catch, but route the catch to the fallback + diagnostic, not to a
   bare warning log.

---

## C4 (#11) — Gateways excluded from permission warning

**Files**

- `src/SwebKit.App/Components/Pages/AksPage.razor` (~44-47 display, ~1313 `BuildPermissionWarning()`)
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` (~32-37 `GatewayApiGroup` /
  `GatewayApiVersions`; ~1197-1350 `GetGatewaysAsync` / `GetGatewayClassesAsync`)

**Current**

- RBAC denials collect as `(ResourceKind, Namespace)` in `accessScope.Denials`.
  `BuildPermissionWarning()` groups by kind and renders "No permission to list some resources…".
  Gateways/GatewayClasses (Gateway API, `gateway.networking.k8s.io`) are optional advanced
  networking, but a 403 on them lands in the same denial list and triggers the same warning,
  implying missing core access.

**Change**

- Introduce an exclusion set (e.g. `gateways`, `gatewayclasses`) filtered out in
  `BuildPermissionWarning()` before grouping/rendering. Prefer defining the excluded kinds as
  constants near the gateway API constants in `KubernetesAksClient.cs` and consuming them in the
  warning builder, so the source of truth stays with the gateway feature.
- Gateways remaining inaccessible should simply hide/disable the gateway view, not raise a
  core-permission warning. Confirm the gateway tab/section already degrades gracefully when denied.

---

## E1 (#1) — Service Bus: which credential was used (backend/extraction)

**Files**

- `src/SwebKit.App/Components/ServiceBus/ServiceBusNamespacePanel.razor` (~217-290) — parses via
  `ServiceBusConnectionStringProperties.Parse()`, stores `NsState.ConnectionError`
- `src/SwebKit.Azure/ServiceBus/ServiceBusClientFactory.cs` (~1-30) — builds client from conn str
- `src/SwebKit.Azure/AzureKeyVaultSecretResolver.cs`,
  `src/SwebKit.Azure/MultiVaultKeyVaultSecretResolver.cs` — secret-reference resolution

**Current**

- On connect failure only a generic exception message is shown. The parsed endpoint / SAS key name
  and the credential _source_ (which Key Vault secret / config reference resolved the connection
  string) are not surfaced, so a misconfigured or wrong credential is hard to diagnose.

**Change**

1. In the connect path, capture non-secret diagnostics: resolved `Endpoint` host, SAS
   `SharedAccessKeyName`, auth method (SAS key vs token/`DefaultAzureCredential`), and the
   credential-source label (the secret-reference name / config key that was resolved — available
   from the resolver layer, not the secret value).
2. On failure, attach these to the error state (`NsState`) for the UI (frontend E1) to render.
3. **Security (DEC-3)** — a hard rule: expose only identifiers. Never place the SAS key value,
   full connection string, or token into the diagnostic payload, logs, or UI. Add a focused test
   asserting the diagnostic contains the key _name_ and endpoint but not the key _value_.
4. Catch `UnauthorizedAccessException` / auth-specific failures distinctly so the message reads as
   a credential problem (with the source label) rather than a generic transport error.
