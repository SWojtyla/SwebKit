use std::process::Child;
use std::sync::Mutex;
use tauri::State;

pub struct SidecarState {
    child: Mutex<Option<Child>>,
    port: Mutex<u16>,
}

/// Spawns the .NET sidecar process and returns the port it's listening on.
/// In dev mode, the sidecar is expected to already be running (dotnet run).
/// In production, we spawn the bundled sidecar binary.
pub fn spawn_sidecar() -> Result<u16, String> {
    // In production, the sidecar binary is bundled alongside the app.
    // For now, use a fixed dev port. The sidecar will be spawned by the
    // Tauri shell plugin or a custom command in production.
    //
    // TODO: Implement production sidecar spawning:
    // 1. Resolve sidecar binary path from Tauri's resource directory
    // 2. Spawn with `--urls http://127.0.0.1:0` to get OS-assigned port
    // 3. Read the actual port from stdout (ASP.NET prints it on startup)
    // 4. Store the Child handle for lifecycle management

    let port = 5199u16;

    #[cfg(debug_assertions)]
    {
        // In dev, the sidecar is started separately (dotnet run --project src-sidecar)
        eprintln!("[swebkit] Dev mode: assuming sidecar at http://127.0.0.1:{port}");
    }

    Ok(port)
}

/// Tauri command: get the sidecar port for the frontend to use.
#[tauri::command]
pub fn get_sidecar_port(state: State<SidecarState>) -> u16 {
    *state.port.lock().unwrap()
}

/// Tauri command: restart the sidecar process.
#[tauri::command]
pub fn restart_sidecar(state: State<SidecarState>) -> Result<u16, String> {
    // Kill existing process if any
    if let Ok(mut child_guard) = state.child.lock() {
        if let Some(mut child) = child_guard.take() {
            let _ = child.kill();
        }
    }

    let port = spawn_sidecar()?;
    *state.port.lock().unwrap() = port;
    Ok(port)
}

pub fn manage() -> SidecarState {
    let port = spawn_sidecar().unwrap_or(5199);
    SidecarState {
        child: Mutex::new(None),
        port: Mutex::new(port),
    }
}
