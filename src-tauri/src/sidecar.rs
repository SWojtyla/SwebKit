use std::process::Child;
use std::sync::Mutex;
use tauri::{AppHandle, Manager, State};

#[cfg(not(debug_assertions))]
use std::io::{BufRead, BufReader};
#[cfg(not(debug_assertions))]
use std::process::{Command, Stdio};
#[cfg(not(debug_assertions))]
use std::sync::mpsc;
#[cfg(not(debug_assertions))]
use std::time::Duration;

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
}

/// How long we wait for the sidecar to report it's actually listening before
/// giving up. Generous because a first-run .NET self-contained publish can be
/// slower to JIT/start than a warm one.
#[cfg(not(debug_assertions))]
const READY_TIMEOUT: Duration = Duration::from_secs(15);

/// Spawns the .NET sidecar process and returns the port it's listening on.
/// In dev mode, the sidecar is expected to already be running (dotnet run).
/// In production, we spawn the bundled self-contained sidecar binary.
pub fn spawn_sidecar(app: &AppHandle) -> Result<(u16, Option<Child>), String> {
    // In dev mode, the sidecar is started separately (dotnet run --project src-sidecar)
    #[cfg(debug_assertions)]
    {
        let _ = app; // unused in dev mode: the sidecar is started externally
        let port = 5199u16;
        eprintln!("[swebkit] Dev mode: assuming sidecar at http://127.0.0.1:{port}");
        return Ok((port, None));
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

/// Tauri command: get the sidecar port for the frontend to use.
#[tauri::command]
pub fn get_sidecar_port(state: State<SidecarState>) -> u16 {
    *state.port.lock().unwrap()
}

/// Tauri command: restart the sidecar process.
#[tauri::command]
pub fn restart_sidecar(app: AppHandle, state: State<SidecarState>) -> Result<u16, String> {
    // Kill existing process if any
    if let Ok(mut child_guard) = state.child.lock() {
        if let Some(mut child) = child_guard.take() {
            let _ = child.kill();
            let _ = child.wait();
        }
    }

    let (port, child) = spawn_sidecar(&app)?;
    *state.child.lock().unwrap() = child;
    *state.port.lock().unwrap() = port;
    Ok(port)
}

/// Builds initial sidecar state at app startup. Propagates spawn failure to the
/// caller instead of silently falling back to a fixed port that may not
/// actually have anything listening on it.
pub fn manage(app: &AppHandle) -> Result<SidecarState, String> {
    let (port, child) = spawn_sidecar(app)?;
    Ok(SidecarState {
        child: Mutex::new(child),
        port: Mutex::new(port),
    })
}

/// Kills the sidecar child process, if any. Called on app exit so the sidecar
/// never survives as an orphan holding its port.
pub fn kill_sidecar(app: &AppHandle) {
    if let Some(state) = app.try_state::<SidecarState>() {
        if let Ok(mut guard) = state.child.lock() {
            if let Some(mut child) = guard.take() {
                let _ = child.kill();
                let _ = child.wait();
            }
        }
    }
}
