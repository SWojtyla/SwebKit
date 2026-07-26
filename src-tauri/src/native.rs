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

// ── Git Operations ───────────────────────────────────────────────────────────

#[derive(serde::Serialize)]
pub struct GitStatus {
    pub branch: String,
    pub ahead: u32,
    pub behind: u32,
    pub staged: u32,
    pub modified: u32,
    pub untracked: u32,
}

#[derive(serde::Serialize)]
pub struct GitBranch {
    pub name: String,
    pub current: bool,
}

/// Tauri command: get git status for a repository
#[tauri::command]
pub async fn git_status(path: String) -> Result<GitStatus, String> {
    let output = std::process::Command::new("git")
        .args(["status", "--porcelain=v2", "--branch"])
        .current_dir(&path)
        .output()
        .map_err(|e| format!("Failed to run git: {}", e))?;

    if !output.status.success() {
        return Err(String::from_utf8_lossy(&output.stderr).to_string());
    }

    let text = String::from_utf8_lossy(&output.stdout);
    let mut branch = String::new();
    let mut ahead = 0u32;
    let mut behind = 0u32;
    let mut staged = 0u32;
    let mut modified = 0u32;
    let mut untracked = 0u32;

    for line in text.lines() {
        if line.starts_with("# branch.head ") {
            branch = line["# branch.head ".len()..].to_string();
        } else if line.starts_with("# branch.ab ") {
            let parts: Vec<&str> = line.split_whitespace().collect();
            if parts.len() >= 3 {
                ahead = parts[1].trim_start_matches('+').parse().unwrap_or(0);
                behind = parts[2].trim_start_matches('-').parse().unwrap_or(0);
            }
        } else if line.starts_with('1') || line.starts_with('2') {
            let fields: Vec<&str> = line.split_whitespace().collect();
            if fields.len() >= 2 {
                let staged_field = fields[1];
                if staged_field != "." { staged += 1; }
                else { modified += 1; }
            }
        } else if line.starts_with('u') {
            modified += 1;
        } else if line.starts_with("? ") {
            untracked += 1;
        }
    }

    Ok(GitStatus { branch, ahead, behind, staged, modified, untracked })
}

/// Tauri command: list git branches
#[tauri::command]
pub async fn git_branches(path: String) -> Result<Vec<GitBranch>, String> {
    let output = std::process::Command::new("git")
        .args(["branch", "--list", "--format=%(HEAD) %(refname:short)"])
        .current_dir(&path)
        .output()
        .map_err(|e| format!("Failed to run git: {}", e))?;

    if !output.status.success() {
        return Err(String::from_utf8_lossy(&output.stderr).to_string());
    }

    let text = String::from_utf8_lossy(&output.stdout);
    let branches: Vec<GitBranch> = text
        .lines()
        .filter(|l| !l.is_empty())
        .map(|l| {
            let current = l.starts_with('*');
            let name = l.trim_start_matches('*').trim().to_string();
            GitBranch { name, current }
        })
        .collect();

    Ok(branches)
}

/// Tauri command: git commit
#[tauri::command]
pub async fn git_commit(path: String, message: String) -> Result<(), String> {
    let output = std::process::Command::new("git")
        .args(["commit", "-m", &message])
        .current_dir(&path)
        .output()
        .map_err(|e| format!("Failed to run git: {}", e))?;

    if !output.status.success() {
        return Err(String::from_utf8_lossy(&output.stderr).to_string());
    }
    Ok(())
}

/// Tauri command: git push
#[tauri::command]
pub async fn git_push(path: String) -> Result<String, String> {
    let output = std::process::Command::new("git")
        .args(["push"])
        .current_dir(&path)
        .output()
        .map_err(|e| format!("Failed to run git: {}", e))?;

    if !output.status.success() {
        return Err(String::from_utf8_lossy(&output.stderr).to_string());
    }
    Ok(String::from_utf8_lossy(&output.stdout).to_string())
}

/// Tauri command: git pull
#[tauri::command]
pub async fn git_pull(path: String) -> Result<String, String> {
    let output = std::process::Command::new("git")
        .args(["pull"])
        .current_dir(&path)
        .output()
        .map_err(|e| format!("Failed to run git: {}", e))?;

    if !output.status.success() {
        return Err(String::from_utf8_lossy(&output.stderr).to_string());
    }
    Ok(String::from_utf8_lossy(&output.stdout).to_string())
}

/// Tauri command: git stage all
#[tauri::command]
pub async fn git_stage_all(path: String) -> Result<(), String> {
    let output = std::process::Command::new("git")
        .args(["add", "--all"])
        .current_dir(&path)
        .output()
        .map_err(|e| format!("Failed to run git: {}", e))?;

    if !output.status.success() {
        return Err(String::from_utf8_lossy(&output.stderr).to_string());
    }
    Ok(())
}

// ── Notifications ────────────────────────────────────────────────────────────

/// Tauri command: show an OS-level notification
#[tauri::command]
pub async fn show_notification(
    app: tauri::AppHandle,
    title: String,
    body: String,
) -> Result<(), String> {
    use tauri_plugin_dialog::DialogExt;
    // Use dialog as a simple notification fallback
    app.dialog()
        .message(body)
        .title(title)
        .blocking_show();
    Ok(())
}
