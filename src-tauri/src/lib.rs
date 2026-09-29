mod sidecar;
mod native;
mod secrets;
mod git;
mod pod_shell;

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
use pod_shell::{start_pod_shell, write_pod_shell, resize_pod_shell, close_pod_shell, PodShellState};

/// `swebkit://` URLs captured at cold start (deep-link `get_current()` runs in `setup`,
/// before the webview's JS listeners can exist). The frontend drains this once on mount
/// via `get_pending_deep_links`; warm-launch URLs never queue — they're emitted as the
/// `swebkit://deep-link` event from the single-instance callback instead.
struct PendingDeepLinks(std::sync::Mutex<Vec<String>>);

#[tauri::command]
fn get_pending_deep_links(state: tauri::State<'_, PendingDeepLinks>) -> Vec<String> {
    std::mem::take(&mut *state.0.lock().unwrap_or_else(|e| e.into_inner()))
}
use git::{
    git_is_repo, git_status, git_changed_files, git_branches,
    git_stage_paths, git_unstage_paths, git_revert_paths,
    git_diff_file, git_commit, git_push, git_pull,
    git_checkout_branch, git_create_branch, git_remote_url,
};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    // `windows_subsystem = "windows"` gives this process no console, so any panic —
    // including one inside `.setup()` or the `.expect` on `build()` below — would
    // otherwise be a completely silent crash with no trace anywhere. Leave a crumb
    // file in the app-data dir so the next "the app just vanished on launch" report
    // has something concrete to point at.
    std::panic::set_hook(Box::new(|info| {
        let message = format!("swebkit panicked: {info}\n");
        eprintln!("{message}");
        if let Ok(appdata) = std::env::var("LOCALAPPDATA") {
            let dir = std::path::Path::new(&appdata).join("com.companyname.swebkit");
            if std::fs::create_dir_all(&dir).is_ok() {
                let _ = std::fs::write(dir.join("last-panic.log"), message);
            }
        }
    }));

    tauri::Builder::default()
        // Single-instance first: on Windows a `swebkit://` activation arrives as a second
        // launch whose argv this callback sees — forward every swebkit:// arg to the webview
        // as a deep-link event and focus the existing window instead of starting a copy.
        .plugin(tauri_plugin_single_instance::init(|app, argv, _cwd| {
            use tauri::Emitter;
            if let Some(window) = app.get_webview_window("main") {
                let _ = window.unminimize();
                let _ = window.set_focus();
            }
            for url in argv.iter().filter(|a| a.starts_with("swebkit://")) {
                let _ = app.emit("swebkit://deep-link", url.clone());
            }
        }))
        .plugin(tauri_plugin_deep_link::init())
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_notification::init())
        .manage(native::PortForwardState::new())
        .manage(PodShellState::new())
        .manage(AllowedRoots::new())
        .manage(PendingDeepLinks(std::sync::Mutex::new(Vec::new())))
        .setup(|app| {
            let handle = app.handle();
            // Cold-start deep links: the URL that launched the process. Queue for the
            // frontend drain — emitting here would race the webview's listener setup.
            {
                use tauri_plugin_deep_link::DeepLinkExt;
                if let Ok(Some(urls)) = app.deep_link().get_current() {
                    if let Some(state) = app.try_state::<PendingDeepLinks>() {
                        let mut pending = state.0.lock().unwrap_or_else(|e| e.into_inner());
                        pending.extend(urls.iter().map(|u| u.as_str().to_string()));
                    }
                }
            }
            // Spawn failure is not fatal — manage() degrades to port 0 and retries
            // in the background rather than panicking during setup (a panic here is
            // a silent crash in a windows_subsystem="windows" build with no console).
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
            start_pod_shell,
            write_pod_shell,
            resize_pod_shell,
            close_pod_shell,
            get_pending_deep_links,
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
                if let Some(state) = app_handle.try_state::<PodShellState>() {
                    pod_shell::kill_all_pod_shells(&state);
                }
            }
        });
}
