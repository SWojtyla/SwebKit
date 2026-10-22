use std::process::Child;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Condvar, Mutex};
use std::time::Duration;
use tauri::{AppHandle, Manager};

#[cfg(not(debug_assertions))]
use tauri::Emitter;
#[cfg(not(debug_assertions))]
use std::io::{BufRead, BufReader};
#[cfg(not(debug_assertions))]
use std::process::{Command, Stdio};
#[cfg(not(debug_assertions))]
use std::sync::mpsc;

#[cfg(all(not(debug_assertions), windows))]
use std::os::windows::process::CommandExt;

/// Windows process creation flag that prevents a console window from appearing
/// for the child process. Without this, spawning the .NET sidecar (a console
/// app) pops up an empty cmd window that stays visible for the app's lifetime.
#[cfg(all(not(debug_assertions), windows))]
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

pub struct SidecarState {
    pub child: Mutex<Option<Child>>,
    pub port: Mutex<u16>,
    /// Settles once the initial background boot has an outcome (listening or failed);
    /// `get_sidecar_port` parks on it so the frontend still gets the real port first try.
    boot: BootGate,
    /// Set by `kill_sidecar` on app exit. A spawn that completes after this (initial boot
    /// or a respawn still in flight) must kill its child instead of storing it, or the
    /// sidecar would outlive the app as an orphan holding its port.
    shutting_down: AtomicBool,
}

impl SidecarState {
    fn new() -> Self {
        Self {
            child: Mutex::new(None),
            port: Mutex::new(0),
            boot: BootGate::new(),
            shutting_down: AtomicBool::new(false),
        }
    }
}

/// One-shot "initial boot finished" latch. A plain `Mutex<bool>` + `Condvar` rather than a
/// tokio primitive: the only waiter is `get_sidecar_port`, which already parks on the
/// blocking pool, and this keeps the latch testable without an async runtime.
struct BootGate {
    settled: Mutex<bool>,
    cv: Condvar,
}

impl BootGate {
    fn new() -> Self {
        Self {
            settled: Mutex::new(false),
            cv: Condvar::new(),
        }
    }

    /// Marks boot as finished and wakes every waiter. Idempotent.
    fn settle(&self) {
        *self.settled.lock().unwrap_or_else(|e| e.into_inner()) = true;
        self.cv.notify_all();
    }

    /// Blocks until `settle` has been called or `timeout` elapses. Returns whether boot settled.
    fn wait(&self, timeout: Duration) -> bool {
        let guard = self.settled.lock().unwrap_or_else(|e| e.into_inner());
        let (guard, _) = self
            .cv
            .wait_timeout_while(guard, timeout, |settled| !*settled)
            .unwrap_or_else(|e| e.into_inner());
        *guard
    }
}

/// How long we wait for the sidecar to report it's actually listening before
/// giving up. Generous because a first-run .NET self-contained publish can be
/// slower to JIT/start than a warm one — and on an overloaded machine (single-file
/// extraction + AV scan + JIT all competing for CPU/IO) even a warm start can
/// blow well past a short timeout. Exceeding this is no longer fatal: `manage`
/// opens the app in its degraded "Disconnected" state and keeps retrying in the
/// background instead of letting the error kill the whole app during setup.
const READY_TIMEOUT: Duration = Duration::from_secs(60);

/// Upper bound for `get_sidecar_port`'s wait on the initial boot. The boot thread's own
/// `spawn_sidecar` gives up at READY_TIMEOUT (measured from process spawn), so this only
/// adds slack for resource resolution and process creation — it should never actually fire.
const BOOT_WAIT_TIMEOUT: Duration = Duration::from_secs(READY_TIMEOUT.as_secs() + 5);

/// Spawns the .NET sidecar process and returns the port it's listening on.
/// In dev mode, the sidecar is expected to already be running (dotnet run).
/// In production, we spawn the bundled self-contained sidecar binary.
pub fn spawn_sidecar(app: &AppHandle) -> Result<(u16, Option<Child>), String> {
    // In dev mode, the sidecar is started separately (dotnet run --project src-sidecar)
    #[cfg(debug_assertions)]
    {
        let _ = app; // unused in dev mode: the sidecar is started externally
        // The dev launcher may relocate the sidecar off the default 5199 when that
        // port is already taken by another app — it reports the chosen port via env.
        let port = std::env::var("SWEBKIT_DEV_SIDECAR_PORT")
            .ok()
            .and_then(|v| v.parse::<u16>().ok())
            .unwrap_or(5199);
        eprintln!("[swebkit] Dev mode: assuming sidecar at http://127.0.0.1:{port}");
        Ok((port, None))
    }

    // In production, spawn the bundled sidecar binary
    #[cfg(not(debug_assertions))]
    {
        let resource_dir = app
            .path()
            .resource_dir()
            .map_err(|e| format!("Failed to resolve resource dir: {e}"))?;

        let sidecar_dir = resource_dir.join("binaries").join("sidecar");
        let sidecar_exe = sidecar_dir.join("SwebKit.Sidecar.exe");

        if !sidecar_exe.exists() {
            return Err(format!(
                "Sidecar binary not found at: {}",
                sidecar_exe.display()
            ));
        }

        // Bind to an OS-assigned free port instead of a hardcoded one — avoids
        // silently reporting "5199 is ready" when something else already holds
        // that port. We recover the real port from Kestrel's own startup log
        // line ("Now listening on: http://127.0.0.1:<port>"), which is only
        // printed once the socket is actually bound and accepting connections.
        let mut cmd = Command::new(&sidecar_exe);
        cmd.arg("--urls")
            .arg("http://127.0.0.1:0")
            .current_dir(&sidecar_dir)
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());

        #[cfg(windows)]
        cmd.creation_flags(CREATE_NO_WINDOW);

        let mut child = cmd
            .spawn()
            .map_err(|e| format!("Failed to spawn sidecar: {e}"))?;

        let stdout = child.stdout.take().expect("sidecar stdout was piped");
        let (tx, rx) = mpsc::channel::<u16>();

        std::thread::spawn(move || {
            let reader = BufReader::new(stdout);
            for line in reader.lines().flatten() {
                eprintln!("[sidecar] {line}");
                if let Some(port) = parse_listening_port(&line) {
                    let _ = tx.send(port);
                }
            }
        });

        // Surface sidecar stderr in our own logs instead of silently discarding
        // it — a crash on startup used to be completely invisible.
        if let Some(stderr) = child.stderr.take() {
            std::thread::spawn(move || {
                let reader = BufReader::new(stderr);
                for line in reader.lines().flatten() {
                    eprintln!("[sidecar:stderr] {line}");
                }
            });
        }

        let port = match rx.recv_timeout(READY_TIMEOUT) {
            Ok(port) => port,
            Err(_) => {
                let _ = child.kill();
                return Err(format!(
                    "Sidecar did not report a listening port within {}s",
                    READY_TIMEOUT.as_secs()
                ));
            }
        };

        eprintln!("[swebkit] Production mode: sidecar listening at http://127.0.0.1:{port}");
        Ok((port, Some(child)))
    }
}

/// Parses ASP.NET Core's default startup log line
/// (`Now listening on: http://127.0.0.1:52341`) to recover the actual bound
/// port when we asked Kestrel to pick one for us (port 0).
#[cfg(not(debug_assertions))]
fn parse_listening_port(line: &str) -> Option<u16> {
    let marker = "Now listening on: ";
    let idx = line.find(marker)?;
    let url = line[idx + marker.len()..].trim();
    let port_str = url.rsplit(':').next()?;
    port_str.trim_end_matches('/').parse().ok()
}

/// Starts background crash supervision for `child`, if there's a real process to supervise at
/// all — dev mode's sidecar is run externally (`dotnet run`) and has no process handle here, so
/// there's nothing to watch. Called after every successful spawn (initial startup and every
/// restart) so a sidecar that crashes on its own is detected and respawned automatically instead
/// of silently leaving the app talking to a dead port until the user notices and manually
/// restarts — see `restart_sidecar`'s doc comment for the incident this was added to fix.
fn start_supervision(app: &AppHandle, child: &Option<Child>) {
    #[cfg(not(debug_assertions))]
    if let Some(child) = child {
        watch_for_crash(app.clone(), child.id());
    }
    #[cfg(debug_assertions)]
    {
        let _ = (app, child);
    }
}

/// Polls (never blocks on) the child identified by `watched_pid` until it exits unexpectedly,
/// then attempts to respawn it a few times with backoff. Uses `try_wait()` in a sleep loop rather
/// than the blocking `Child::wait()` specifically so it never holds `SidecarState.child`'s mutex
/// for longer than a single poll — `restart_sidecar`/`kill_sidecar` need to briefly take that same
/// lock to kill the process themselves, and a competing blocking `wait()` holding the lock across
/// the process's entire remaining lifetime would deadlock them (mirrors the pattern already used
/// for the pod-shell exit watcher in `pod_shell.rs`, for the same reason).
#[cfg(not(debug_assertions))]
fn watch_for_crash(app: AppHandle, watched_pid: u32) {
    std::thread::spawn(move || {
        loop {
            std::thread::sleep(Duration::from_millis(500));
            let Some(state) = app.try_state::<SidecarState>() else {
                return; // app is shutting down
            };
            let mut guard = state.child.lock().unwrap();
            let Some(child) = guard.as_mut() else {
                return; // slot cleared elsewhere (app exit, or a concurrent manual restart) — nothing left to watch
            };
            if child.id() != watched_pid {
                return; // slot now holds a different process; whoever put it there started its own watcher
            }
            match child.try_wait() {
                Ok(Some(_status)) => {
                    *guard = None; // clear it now so restart_sidecar/kill_sidecar never touch an already-reaped handle
                    break;
                }
                _ => continue, // still running (Ok(None)), or a transient error — keep polling either way
            }
        }

        eprintln!("[swebkit] Sidecar (pid {watched_pid}) exited unexpectedly — attempting automatic recovery");
        let _ = app.emit("sidecar-crashed", ());

        respawn_with_retries(app);
    });
}

/// Retries `spawn_sidecar` with backoff; on success stores the new child/port in
/// `SidecarState`, emits `sidecar-restarted`, and hands the process off to
/// `watch_for_crash` for supervision. Shared by the post-crash recovery in
/// `watch_for_crash` and the startup retry path in `manage`, which uses it when
/// the very first spawn fails (e.g. an overloaded machine where .NET needs
/// longer than READY_TIMEOUT just to boot).
#[cfg(not(debug_assertions))]
fn respawn_with_retries(app: AppHandle) {
    for attempt in 1..=3u32 {
        std::thread::sleep(Duration::from_secs(attempt as u64));
        match spawn_sidecar(&app) {
            Ok((port, Some(new_child))) => {
                let new_pid = new_child.id();
                let Some(state) = app.try_state::<SidecarState>() else {
                    return; // app shut down mid-recovery
                };
                if !store_spawned(&state, port, Some(new_child)) {
                    return; // app began exiting mid-spawn — store_spawned already killed the child
                }
                eprintln!("[swebkit] Sidecar (re)spawned on attempt {attempt}, now listening on {port}");
                let _ = app.emit("sidecar-restarted", port);
                watch_for_crash(app, new_pid); // hand off supervision to a fresh watch cycle
                return;
            }
            Ok((_, None)) => return, // dev mode — nothing to supervise
            Err(e) => eprintln!("[swebkit] Sidecar (re)spawn attempt {attempt} failed: {e}"),
        }
    }

    eprintln!("[swebkit] Sidecar auto-recovery gave up after 3 attempts — manual restart required");
    let _ = app.emit("sidecar-recovery-failed", ());
}

/// Stores a freshly spawned child + port, unless `kill_sidecar` already ran — then the child is
/// killed here instead and this returns `false`. The `shutting_down` check happens under the
/// child lock, which `kill_sidecar` also holds while setting the flag, so a spawn finishing
/// concurrently with app exit can never slip its child into the slot after the exit-time kill.
fn store_spawned(state: &SidecarState, port: u16, child: Option<Child>) -> bool {
    let mut guard = state.child.lock().unwrap_or_else(|e| e.into_inner());
    if state.shutting_down.load(Ordering::SeqCst) {
        if let Some(mut child) = child {
            let _ = child.kill();
            let _ = child.wait();
        }
        return false;
    }
    *guard = child;
    *state.port.lock().unwrap_or_else(|e| e.into_inner()) = port;
    true
}

/// Tauri command: get the sidecar port for the frontend to use. Waits (on the blocking pool,
/// never the event loop) for the initial background boot to settle, so the frontend's
/// `initSidecarBaseUrl()` still resolves the real port before first render. A failed boot
/// returns 0 exactly as before: the frontend keeps its default URL and shows "Disconnected"
/// while `respawn_with_retries` keeps trying and reports back via `sidecar-restarted`.
#[tauri::command]
pub async fn get_sidecar_port(app: AppHandle) -> u16 {
    tauri::async_runtime::spawn_blocking(move || {
        let state = app.state::<SidecarState>();
        if !state.boot.wait(BOOT_WAIT_TIMEOUT) {
            eprintln!(
                "[swebkit] Sidecar boot still unsettled after {}s — reporting current port",
                BOOT_WAIT_TIMEOUT.as_secs()
            );
        }
        let port = *state.port.lock().unwrap_or_else(|e| e.into_inner());
        port
    })
    .await
    .unwrap_or(0)
}

/// Tauri command: restart the sidecar process. Also used by the frontend's manual "Reconnect"
/// button in the status bar — not just internally by `watch_for_crash`'s automatic recovery — so
/// this (re)starts crash supervision for the freshly spawned process too, the same as `manage()`
/// does at app startup. Without that, a manual restart would leave the newly-spawned process
/// completely unsupervised until the next full app relaunch, defeating the point.
///
/// `async` + `spawn_blocking`: a sync command runs on the main (event-loop) thread, and
/// `spawn_sidecar` can block for up to READY_TIMEOUT — the whole UI froze while reconnecting.
#[tauri::command]
pub async fn restart_sidecar(app: AppHandle) -> Result<u16, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let state = app.state::<SidecarState>();
        // Kill existing process if any
        if let Ok(mut child_guard) = state.child.lock() {
            if let Some(mut child) = child_guard.take() {
                let _ = child.kill();
                let _ = child.wait();
            }
        }

        let (port, child) = spawn_sidecar(&app)?;
        // Supervision first is safe: the watcher's first poll is 500ms out and it bails if the
        // slot doesn't hold this pid (e.g. store_spawned refused it because the app is exiting).
        start_supervision(&app, &child);
        if !store_spawned(&state, port, child) {
            return Err("Sidecar restart aborted: the app is shutting down".to_string());
        }
        Ok(port)
    })
    .await
    .map_err(|e| format!("Sidecar restart task failed: {e}"))?
}

/// Registers sidecar state at app startup and boots the sidecar on a background thread.
/// `setup()` runs on the event-loop thread, so blocking it on `spawn_sidecar` (up to
/// READY_TIMEOUT waiting for Kestrel) kept the webview from loading in parallel with .NET's
/// cold start. State is managed immediately with `port = 0` / no child; `get_sidecar_port`
/// waits on `boot` until the thread stores the real port or gives up.
///
/// A spawn failure is still NOT fatal: on an overloaded machine the sidecar can exceed
/// READY_TIMEOUT merely because .NET is slow to boot. The port stays 0 (the frontend falls
/// back to its default, fails health checks, and shows its existing "Disconnected" state with
/// a Reconnect button) while the same thread keeps retrying the spawn and emits the usual
/// sidecar-* lifecycle events the UI already handles.
pub fn manage(app: &AppHandle) {
    app.manage(SidecarState::new());
    let app = app.clone();
    std::thread::spawn(move || {
        let state = app.state::<SidecarState>();
        match spawn_sidecar(&app) {
            Ok((port, child)) => {
                start_supervision(&app, &child);
                store_spawned(&state, port, child);
                state.boot.settle();
            }
            Err(e) => {
                eprintln!(
                    "[swebkit] Sidecar failed to start ({e}) — continuing without a backend; \
                     spawn retries continue in the background and Reconnect remains available"
                );
                state.boot.settle();
                #[cfg(not(debug_assertions))]
                {
                    let _ = app.emit("sidecar-crashed", ());
                    respawn_with_retries(app.clone());
                }
            }
        }
    });
}

/// Kills the sidecar child process, if any. Called on app exit so the sidecar
/// never survives as an orphan holding its port.
pub fn kill_sidecar(app: &AppHandle) {
    if let Some(state) = app.try_state::<SidecarState>() {
        let mut guard = state.child.lock().unwrap_or_else(|e| e.into_inner());
        // Under the child lock — see `store_spawned` for why that ordering matters.
        state.shutting_down.store(true, Ordering::SeqCst);
        if let Some(mut child) = guard.take() {
            let _ = child.kill();
            let _ = child.wait();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{BootGate, SidecarState, store_spawned};
    use std::sync::atomic::Ordering;
    use std::sync::Arc;
    use std::time::{Duration, Instant};

    #[test]
    fn boot_gate_times_out_when_never_settled() {
        let gate = BootGate::new();
        assert!(!gate.wait(Duration::from_millis(20)));
    }

    #[test]
    fn boot_gate_returns_immediately_once_settled() {
        let gate = BootGate::new();
        gate.settle();
        let started = Instant::now();
        assert!(gate.wait(Duration::from_secs(5)));
        assert!(started.elapsed() < Duration::from_secs(1));
    }

    #[test]
    fn boot_gate_wakes_a_waiter_settled_from_another_thread() {
        let gate = Arc::new(BootGate::new());
        let settler = Arc::clone(&gate);
        let handle = std::thread::spawn(move || {
            std::thread::sleep(Duration::from_millis(30));
            settler.settle();
        });
        assert!(gate.wait(Duration::from_secs(5)));
        handle.join().unwrap();
    }

    #[test]
    fn boot_gate_settle_is_idempotent() {
        let gate = BootGate::new();
        gate.settle();
        gate.settle();
        assert!(gate.wait(Duration::from_millis(1)));
    }

    #[test]
    fn store_spawned_records_port_while_running() {
        let state = SidecarState::new();
        assert!(store_spawned(&state, 5199, None));
        assert_eq!(*state.port.lock().unwrap(), 5199);
    }

    #[test]
    fn store_spawned_refuses_after_shutdown_began() {
        let state = SidecarState::new();
        state.shutting_down.store(true, Ordering::SeqCst);
        assert!(!store_spawned(&state, 5199, None));
        assert_eq!(*state.port.lock().unwrap(), 0, "port must stay at the degraded value");
        assert!(state.child.lock().unwrap().is_none());
    }
}
