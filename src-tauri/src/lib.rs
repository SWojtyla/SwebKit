mod sidecar;

use sidecar::{get_sidecar_port, manage, restart_sidecar, SidecarState};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(manage())
        .invoke_handler(tauri::generate_handler![get_sidecar_port, restart_sidecar])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
