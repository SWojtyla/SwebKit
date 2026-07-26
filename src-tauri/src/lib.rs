mod sidecar;
mod native;

use sidecar::{get_sidecar_port, manage, restart_sidecar};
use native::{
    start_port_forward, stop_port_forward, list_port_forwards,
    pick_file, pick_directory, confirm_dialog, alert_dialog,
    write_clipboard, read_clipboard,
};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(tauri_plugin_dialog::init())
        .manage(manage())
        .manage(native::PortForwardState::new())
        .invoke_handler(tauri::generate_handler![
            get_sidecar_port,
            restart_sidecar,
            start_port_forward,
            stop_port_forward,
            list_port_forwards,
            pick_file,
            pick_directory,
            confirm_dialog,
            alert_dialog,
            write_clipboard,
            read_clipboard,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
