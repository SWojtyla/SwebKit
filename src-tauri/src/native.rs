use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use tauri::State;

#[cfg(windows)]
use std::os::windows::process::CommandExt;

#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

fn hidden_command(program: &str) -> std::process::Command {
    let mut cmd = std::process::Command::new(program);
    #[cfg(windows)]
    cmd.creation_flags(CREATE_NO_WINDOW);
    cmd
}

/// Roots the frontend is allowed to read/write files under, via `read_file`/
/// `write_file`/`list_dir`. A root is only added when the user themselves picks
/// a file/folder through the native OS dialog (`pick_file`/`pick_directory`) —
/// the frontend can never grant itself access to an arbitrary path.
pub struct AllowedRoots {
    roots: Mutex<Vec<PathBuf>>,
}

impl AllowedRoots {
    pub fn new() -> Self {
        Self { roots: Mutex::new(Vec::new()) }
    }

    fn allow(&self, root: PathBuf) {
        let mut roots = self.roots.lock().unwrap();
        if !roots.iter().any(|r| r == &root) {
            roots.push(root);
        }
    }

    fn is_allowed(&self, candidate: &Path) -> bool {
        let roots = self.roots.lock().unwrap();
        roots.iter().any(|root| candidate.starts_with(root))
    }
}

/// Resolves `path` to a canonical, validated target inside an allowed root.
/// Canonicalizes the *parent* directory (the file itself may not exist yet, e.g.
/// on write) and checks that against the allowlist, then rejoins the filename —
/// this also collapses any `..`/symlink tricks in the parent portion.
fn validate_within_roots(path: &str, roots: &AllowedRoots) -> Result<PathBuf, String> {
    let requested = Path::new(path);
    let parent = requested
        .parent()
        .filter(|p| !p.as_os_str().is_empty())
        .ok_or_else(|| "Invalid path".to_string())?;
    let canonical_parent =
        std::fs::canonicalize(parent).map_err(|e| format!("Invalid path: {e}"))?;
    if !roots.is_allowed(&canonical_parent) {
        return Err("Path is outside the allowed workspace".to_string());
    }
    let file_name = requested
        .file_name()
        .ok_or_else(|| "Invalid path".to_string())?;
    Ok(canonical_parent.join(file_name))
}

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

/// Tauri command: open a file picker dialog and return the selected path.
/// The picked file's parent directory becomes an allowed root for `read_file`/
/// `write_file`/`list_dir` — the user's own pick is what grants access, not the
/// frontend's say-so.
#[tauri::command]
pub async fn pick_file(
    app: tauri::AppHandle,
    roots: State<'_, AllowedRoots>,
    title: Option<String>,
) -> Result<Option<String>, String> {
    use tauri_plugin_dialog::DialogExt;

    let mut builder = app.dialog().file();
    if let Some(t) = title {
        builder = builder.set_title(t);
    }

    let result = builder.blocking_pick_file().map(|p| p.to_string());

    if let Some(path_str) = &result {
        if let Some(parent) = Path::new(path_str).parent() {
            if let Ok(canonical) = std::fs::canonicalize(parent) {
                roots.allow(canonical);
            }
        }
    }

    Ok(result)
}

/// Tauri command: open a directory picker dialog and return the selected path.
/// The picked directory itself becomes an allowed root (see `pick_file`).
#[tauri::command]
pub async fn pick_directory(
    app: tauri::AppHandle,
    roots: State<'_, AllowedRoots>,
    title: Option<String>,
) -> Result<Option<String>, String> {
    use tauri_plugin_dialog::DialogExt;

    let mut builder = app.dialog().file();
    if let Some(t) = title {
        builder = builder.set_title(t);
    }

    let result = builder.blocking_pick_folder().map(|p| p.to_string());

    if let Some(dir_str) = &result {
        if let Ok(canonical) = std::fs::canonicalize(dir_str) {
            roots.allow(canonical);
        }
    }

    Ok(result)
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
    let output = hidden_command("git")
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
    let output = hidden_command("git")
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
    let output = hidden_command("git")
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
    let output = hidden_command("git")
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
    let output = hidden_command("git")
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
    let output = hidden_command("git")
        .args(["add", "--all"])
        .current_dir(&path)
        .output()
        .map_err(|e| format!("Failed to run git: {}", e))?;

    if !output.status.success() {
        return Err(String::from_utf8_lossy(&output.stderr).to_string());
    }
    Ok(())
}

// ── Filesystem ───────────────────────────────────────────────────────────────
//
// These commands only operate inside roots the user themselves granted via
// `pick_file`/`pick_directory` (see `AllowedRoots` above) — otherwise any script
// running in the webview (a compromised dependency, an XSS in a rendered
// YAML/log viewer) would be able to read/write any file the OS user can touch.

/// Tauri command: read a text file
#[tauri::command]
pub async fn read_file(path: String, roots: State<'_, AllowedRoots>) -> Result<String, String> {
    let target = validate_within_roots(&path, &roots)?;
    std::fs::read_to_string(&target).map_err(|e| format!("Failed to read file: {}", e))
}

/// Tauri command: write a text file
#[tauri::command]
pub async fn write_file(
    path: String,
    content: String,
    roots: State<'_, AllowedRoots>,
) -> Result<(), String> {
    let target = validate_within_roots(&path, &roots)?;
    std::fs::write(&target, &content).map_err(|e| format!("Failed to write file: {}", e))
}

/// Tauri command: list files in a directory
#[tauri::command]
pub async fn list_dir(path: String, roots: State<'_, AllowedRoots>) -> Result<Vec<String>, String> {
    let canonical = std::fs::canonicalize(&path).map_err(|e| format!("Invalid path: {e}"))?;
    if !roots.is_allowed(&canonical) {
        return Err("Path is outside the allowed workspace".to_string());
    }
    let entries = std::fs::read_dir(&canonical).map_err(|e| format!("Failed to read dir: {}", e))?;
    let mut names = Vec::new();
    for entry in entries {
        if let Ok(entry) = entry {
            if let Some(name) = entry.file_name().to_str() {
                names.push(name.to_string());
            }
        }
    }
    Ok(names)
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
