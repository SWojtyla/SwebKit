use std::collections::HashMap;
use std::io::{BufRead, BufReader};
use std::path::{Path, PathBuf};
use std::process::{Child, Stdio};
use std::sync::{mpsc, Mutex};
use std::time::Duration;
use tauri::State;

#[cfg(windows)]
use std::os::windows::process::CommandExt;

#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

pub(crate) fn hidden_command(program: &str) -> std::process::Command {
    // `mut` is only exercised by the `#[cfg(windows)]` call below (`creation_flags` needs
    // `&mut self`) — on other platforms that line is compiled out entirely, so clippy correctly
    // sees an unused `mut` there.
    #[cfg_attr(not(windows), allow(unused_mut))]
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

    pub(crate) fn allow(&self, root: PathBuf) {
        let mut roots = self.roots.lock().unwrap();
        if !roots.iter().any(|r| r == &root) {
            roots.push(root);
        }
    }

    pub(crate) fn is_allowed(&self, candidate: &Path) -> bool {
        let roots = self.roots.lock().unwrap();
        roots.iter().any(|root| candidate.starts_with(root))
    }
}

/// Resolves and validates a *directory* inside an allowed root.
///
/// `validate_within_roots` canonicalizes the parent because it is written for
/// files that may not exist yet; a repository directory must be canonicalized
/// itself. Git commands use this so they are gated exactly like file access —
/// without it, any script in the webview could run `git push` in an arbitrary
/// directory.
pub(crate) fn validate_dir_within_roots(
    path: &str,
    roots: &AllowedRoots,
) -> Result<PathBuf, String> {
    let canonical = std::fs::canonicalize(path)
        .map_err(|e| format!("Invalid directory: {e}"))?;
    if !canonical.is_dir() {
        return Err("Path is not a directory".to_string());
    }
    if !roots.is_allowed(&canonical) {
        return Err("Path is outside the allowed workspace".to_string());
    }
    Ok(canonical)
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

/// How long we wait for `kubectl port-forward` to either report it's listening
/// (stdout: "Forwarding from ...") or fail outright (stderr) before giving up.
const FORWARD_READY_TIMEOUT: Duration = Duration::from_secs(10);

/// Active port-forward sessions: local_port -> session (including the live kubectl child).
pub struct PortForwardState {
    sessions: Mutex<HashMap<u16, PortForwardSession>>,
}

pub struct PortForwardSession {
    pub namespace: String,
    pub pod: String,
    pub remote_port: u16,
    // No `local_port` field here — it would just duplicate the HashMap key this session is
    // stored under in `PortForwardState::sessions`; see `list_port_forwards`, which reads the
    // port from the map key, not from a struct field.
    child: Child,
}

impl PortForwardState {
    pub fn new() -> Self {
        Self {
            sessions: Mutex::new(HashMap::new()),
        }
    }
}

/// Tauri command: start a port-forward session by spawning a real `kubectl port-forward`
/// subprocess. `context`/`kubeconfig` mirror the AKS config already used elsewhere in the app —
/// when omitted, kubectl falls back to its own default context/kubeconfig resolution.
#[tauri::command]
pub fn start_port_forward(
    state: State<PortForwardState>,
    namespace: String,
    pod: String,
    remote_port: u16,
    local_port: Option<u16>,
    context: Option<String>,
    kubeconfig: Option<String>,
) -> Result<u16, String> {
    let lp = local_port.unwrap_or(0);

    let mut cmd = hidden_command("kubectl");
    cmd.arg("port-forward")
        .arg("-n")
        .arg(&namespace)
        .arg(format!("pod/{pod}"))
        .arg(format!("{lp}:{remote_port}"))
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    if let Some(ctx) = context.filter(|c| !c.is_empty()) {
        cmd.arg("--context").arg(ctx);
    }
    if let Some(kc) = kubeconfig.filter(|k| !k.is_empty()) {
        cmd.arg("--kubeconfig").arg(kc);
    }

    let mut child = cmd
        .spawn()
        .map_err(|e| format!("Failed to start kubectl port-forward: {e}"))?;

    let stdout = child.stdout.take().expect("port-forward stdout was piped");
    let stderr = child.stderr.take().expect("port-forward stderr was piped");
    let (tx, rx) = mpsc::channel::<Result<u16, String>>();

    let tx_stdout = tx.clone();
    std::thread::spawn(move || {
        let reader = BufReader::new(stdout);
        for line in reader.lines().map_while(Result::ok) {
            eprintln!("[swebkit:port-forward] {line}");
            if let Some(port) = parse_forwarded_port(&line) {
                let _ = tx_stdout.send(Ok(port));
            }
        }
    });

    std::thread::spawn(move || {
        let reader = BufReader::new(stderr);
        for line in reader.lines().map_while(Result::ok) {
            eprintln!("[swebkit:port-forward:stderr] {line}");
            if line.to_ascii_lowercase().contains("error") {
                let _ = tx.send(Err(line));
            }
        }
    });

    let actual_port = match rx.recv_timeout(FORWARD_READY_TIMEOUT) {
        Ok(Ok(port)) => port,
        Ok(Err(e)) => {
            let _ = child.kill();
            return Err(format!("kubectl port-forward failed: {e}"));
        }
        Err(_) => {
            let _ = child.kill();
            return Err(format!(
                "kubectl port-forward did not start within {}s — check that kubectl is on PATH \
                 and the pod is running",
                FORWARD_READY_TIMEOUT.as_secs()
            ));
        }
    };

    state.sessions.lock().unwrap().insert(
        actual_port,
        PortForwardSession { namespace: namespace.clone(), pod: pod.clone(), remote_port, child },
    );

    eprintln!(
        "[swebkit] Port-forward started: {}:{} -> localhost:{}",
        namespace, pod, actual_port
    );

    Ok(actual_port)
}

/// Parses kubectl's `Forwarding from 127.0.0.1:<port> -> <remote>` startup line to recover the
/// actual bound local port (needed when the caller asked for port 0 / OS-assigned).
fn parse_forwarded_port(line: &str) -> Option<u16> {
    let marker = "127.0.0.1:";
    let idx = line.find(marker)?;
    let rest = &line[idx + marker.len()..];
    let port_str = rest.split(|c: char| !c.is_ascii_digit()).next()?;
    port_str.parse().ok()
}

/// Tauri command: stop a port-forward session, killing the underlying kubectl process.
#[tauri::command]
pub fn stop_port_forward(state: State<PortForwardState>, local_port: u16) -> Result<(), String> {
    let mut session = state
        .sessions
        .lock()
        .unwrap()
        .remove(&local_port)
        .ok_or_else(|| format!("No session on port {}", local_port))?;

    let _ = session.child.kill();
    let _ = session.child.wait();

    eprintln!("[swebkit] Port-forward stopped on port {}", local_port);
    Ok(())
}

/// Kills every active port-forward's kubectl child process. Called on app exit so forwards
/// never survive as orphans holding their local port after the app closes.
pub fn kill_all_port_forwards(state: &PortForwardState) {
    let mut sessions = state.sessions.lock().unwrap();
    for (_, mut session) in sessions.drain() {
        let _ = session.child.kill();
        let _ = session.child.wait();
    }
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
            // Recorded on disk as well so the grant survives a restart and the
            // frontend never has to be trusted as the source of authorization.
            persist_granted_root(&app, &canonical);
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

// ── Granted repository roots ─────────────────────────────────────────
//
//  is in-memory and is populated only by the native pick dialogs,
// so the frontend can never grant itself access to a path. Persisting the chosen
// repository in localStorage and handing it back after a restart would break that
// property outright — any script in the webview could write an arbitrary path
// there. Instead the *authority list* lives here, on disk, and the frontend only
// ever names a root it wants restored from it.
//
// See docs/features/active/api-client-git-completion/decisions.md DEC-G3.

fn granted_roots_file(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    use tauri::Manager;
    let dir = app
        .path()
        .app_config_dir()
        .map_err(|e| format!("Cannot resolve config directory: {e}"))?;
    std::fs::create_dir_all(&dir).map_err(|e| format!("Cannot create config directory: {e}"))?;
    Ok(dir.join("granted-roots.json"))
}

fn load_granted_roots(app: &tauri::AppHandle) -> Vec<PathBuf> {
    let Ok(file) = granted_roots_file(app) else {
        return Vec::new();
    };
    let Ok(text) = std::fs::read_to_string(file) else {
        return Vec::new();
    };
    serde_json::from_str::<Vec<String>>(&text)
        .unwrap_or_default()
        .into_iter()
        .map(PathBuf::from)
        .collect()
}

fn persist_granted_root(app: &tauri::AppHandle, root: &Path) {
    let mut roots = load_granted_roots(app);
    if roots.iter().any(|r| r == root) {
        return;
    }
    roots.push(root.to_path_buf());

    let Ok(file) = granted_roots_file(app) else {
        return;
    };
    let payload: Vec<String> = roots
        .iter()
        .map(|p| p.to_string_lossy().to_string())
        .collect();
    if let Ok(json) = serde_json::to_string_pretty(&payload) {
        // Best-effort: losing the grant means the user re-picks the folder.
        let _ = std::fs::write(file, json);
    }
}

/// Re-admits a path the user previously picked through an OS dialog.
///
/// Rejects anything not on the persisted grant list, so this is a restore
/// mechanism, not a way for the frontend to authorize a new path.
#[tauri::command]
pub async fn restore_allowed_root(
    app: tauri::AppHandle,
    path: String,
    roots: State<'_, AllowedRoots>,
) -> Result<bool, String> {
    let Ok(canonical) = std::fs::canonicalize(&path) else {
        return Ok(false);
    };
    if !load_granted_roots(&app).iter().any(|r| r == &canonical) {
        return Ok(false);
    }
    roots.allow(canonical);
    Ok(true)
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
    for entry in entries.flatten() {
        if let Some(name) = entry.file_name().to_str() {
            names.push(name.to_string());
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

#[cfg(test)]
mod port_forward_tests {
    use super::parse_forwarded_port;

    #[test]
    fn parses_ipv4_forwarding_line() {
        assert_eq!(
            parse_forwarded_port("Forwarding from 127.0.0.1:37559 -> 8080"),
            Some(37559)
        );
    }

    #[test]
    fn ignores_ipv6_forwarding_line() {
        assert_eq!(parse_forwarded_port("Forwarding from [::1]:37559 -> 8080"), None);
    }

    #[test]
    fn ignores_unrelated_lines() {
        assert_eq!(parse_forwarded_port("Handling connection for 37559"), None);
        assert_eq!(parse_forwarded_port(""), None);
    }
}
