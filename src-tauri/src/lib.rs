mod sidecar;
mod native;
mod secrets;
mod git;

use tauri::Manager;
use sidecar::{get_sidecar_port, restart_sidecar};
use secrets::{save_secret, get_secret, delete_secret, list_secrets};
use native::{
    start_port_forward, stop_port_forward, list_port_forwards,
    pick_file, pick_directory, confirm_dialog, alert_dialog,
    write_clipboard, read_clipboard,
    show_notification,
    read_file, write_file, list_dir,
    restore_allowed_root,
    AllowedRoots,
};
use git::{
    git_is_repo, git_status, git_changed_files, git_branches,
    git_stage_paths, git_unstage_paths, git_revert_paths,
    git_diff_file, git_commit, git_push, git_pull,
    git_checkout_branch, git_create_branch, git_remote_url,
};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(tauri_plugin_dialog::init())
        .manage(native::PortForwardState::new())
        .manage(AllowedRoots::new())
        .setup(|app| {
            let handle = app.handle();
            // Propagates a spawn failure as a fatal setup error instead of
            // silently pretending port 5199 is ready when nothing is listening.
            let state = sidecar::manage(handle)?;
            app.manage(state);
            Ok(())
        })
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
            git_is_repo,
            git_status,
            git_changed_files,
            git_branches,
            git_stage_paths,
            git_unstage_paths,
            git_revert_paths,
            git_diff_file,
            git_commit,
            git_push,
            git_pull,
            git_checkout_branch,
            git_create_branch,
            git_remote_url,
            show_notification,
            read_file,
            write_file,
            list_dir,
            restore_allowed_root,
            save_secret,
            get_secret,
            delete_secret,
            list_secrets,
        ])
        .build(tauri::generate_context!())
        .expect("error while building tauri application")
        .run(|app_handle, event| {
            // Kill the sidecar child process on app exit so it never survives
            // as an orphan holding its port.
            if let tauri::RunEvent::Exit = event {
                sidecar::kill_sidecar(app_handle);
                if let Some(state) = app_handle.try_state::<native::PortForwardState>() {
                    native::kill_all_port_forwards(&state);
                }
            }
        });
}
