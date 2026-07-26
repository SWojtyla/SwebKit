use std::collections::HashMap;
use std::sync::Mutex;
use tauri::State;

/// Active port-forward sessions: local_port -> (namespace, pod, remote_port)
pub struct PortForwardState {
    sessions: Mutex<HashMap<u16, PortForwardSession>>,
}

pub struct PortForwardSession {
    pub namespace: String,
    pub pod: String,
    pub remote_port: u16,
    pub local_port: u16,
}

impl PortForwardState {
    pub fn new() -> Self {
        Self {
            sessions: Mutex::new(HashMap::new()),
        }
    }
}

/// Tauri command: start a port-forward session via kubectl
#[tauri::command]
pub fn start_port_forward(
    state: State<PortForwardState>,
    namespace: String,
    pod: String,
    remote_port: u16,
    local_port: Option<u16>,
) -> Result<u16, String> {
    let lp = local_port.unwrap_or(0);

    // In production, this would spawn `kubectl port-forward -n {namespace} {pod} {lp}:{remote_port}`
    // For now, we register the session and return the local port
    let actual_port = if lp == 0 { 18000 + (state.sessions.lock().unwrap().len() as u16) } else { lp };

    let session = PortForwardSession {
        namespace: namespace.clone(),
        pod: pod.clone(),
        remote_port,
        local_port: actual_port,
    };

    state
        .sessions
        .lock()
        .unwrap()
        .insert(actual_port, session);

    eprintln!(
        "[swebkit] Port-forward registered: {}:{} -> localhost:{}",
        namespace, pod, actual_port
    );

    Ok(actual_port)
}

/// Tauri command: stop a port-forward session
#[tauri::command]
pub fn stop_port_forward(state: State<PortForwardState>, local_port: u16) -> Result<(), String> {
    state
        .sessions
        .lock()
        .unwrap()
        .remove(&local_port)
        .ok_or_else(|| format!("No session on port {}", local_port))?;

    eprintln!("[swebkit] Port-forward stopped on port {}", local_port);
    Ok(())
}

/// Tauri command: list active port-forward sessions
#[tauri::command]
pub fn list_port_forwards(state: State<PortForwardState>) -> Vec<PortForwardSessionInfo> {
    state
        .sessions
        .lock()
        .unwrap()
        .iter()
        .map(|(port, s)| PortForwardSessionInfo {
            local_port: *port,
            namespace: s.namespace.clone(),
            pod: s.pod.clone(),
            remote_port: s.remote_port,
        })
        .collect()
}

#[derive(serde::Serialize)]
pub struct PortForwardSessionInfo {
    pub local_port: u16,
    pub namespace: String,
    pub pod: String,
    pub remote_port: u16,
}

/// Tauri command: open a file picker dialog and return the selected path
#[tauri::command]
pub async fn pick_file(app: tauri::AppHandle, title: Option<String>) -> Result<Option<String>, String> {
    use tauri_plugin_dialog::DialogExt;

    let mut builder = app.dialog().file();
    if let Some(t) = title {
        builder = builder.set_title(t);
    }

    let result = builder.blocking_pick_file();
    Ok(result.map(|p| p.to_string()))
}

/// Tauri command: open a directory picker dialog and return the selected path
#[tauri::command]
pub async fn pick_directory(app: tauri::AppHandle, title: Option<String>) -> Result<Option<String>, String> {
    use tauri_plugin_dialog::DialogExt;

    let mut builder = app.dialog().file();
    if let Some(t) = title {
        builder = builder.set_title(t);
    }

    let result = builder.blocking_pick_folder();
    Ok(result.map(|p| p.to_string()))
}

/// Tauri command: show a confirmation dialog (replaces window.confirm)
#[tauri::command]
pub async fn confirm_dialog(
    app: tauri::AppHandle,
    title: String,
    message: String,
) -> Result<bool, String> {
    use tauri_plugin_dialog::{DialogExt, MessageDialogButtons, MessageDialogKind};

    let result = app
        .dialog()
        .message(message)
        .title(title)
        .kind(MessageDialogKind::Warning)
        .buttons(MessageDialogButtons::OkCancel)
        .blocking_show();

    Ok(result)
}

/// Tauri command: show an alert dialog (replaces window.alert)
#[tauri::command]
pub async fn alert_dialog(
    app: tauri::AppHandle,
    title: String,
    message: String,
) -> Result<(), String> {
    use tauri_plugin_dialog::{DialogExt, MessageDialogButtons, MessageDialogKind};

    app.dialog()
        .message(message)
        .title(title)
        .kind(MessageDialogKind::Info)
        .buttons(MessageDialogButtons::Ok)
        .blocking_show();

    Ok(())
}

/// Tauri command: write text to clipboard
#[tauri::command]
pub async fn write_clipboard(app: tauri::AppHandle, text: String) -> Result<(), String> {
    use tauri_plugin_clipboard_manager::ClipboardExt;
    app.clipboard()
        .write_text(&text)
        .map_err(|e| e.to_string())
}

/// Tauri command: read text from clipboard
#[tauri::command]
pub async fn read_clipboard(app: tauri::AppHandle) -> Result<String, String> {
    use tauri_plugin_clipboard_manager::ClipboardExt;
    app.clipboard()
        .read_text()
        .map_err(|e| e.to_string())
}
