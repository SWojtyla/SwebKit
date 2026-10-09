use std::collections::{HashMap, VecDeque};
use std::io::{BufRead, BufReader};
use std::path::{Path, PathBuf};
use std::process::{Child, Stdio};
use std::sync::{mpsc, Arc, Mutex};
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
/// (stdout: "Forwarding from ...") or fail outright (stderr/early exit) before giving up.
/// Generous on purpose: a cold exec-credential plugin (kubelogin, `az` on Entra-enabled
/// clusters) can spend most of that budget just producing a token.
const FORWARD_READY_TIMEOUT: Duration = Duration::from_secs(30);

/// stderr lines retained so a startup failure can report what kubectl actually said — most
/// real failures ("Unable to connect to the server: ...") don't contain the word "error", so
/// watching for that substring alone collapsed every failure into an uninformative timeout.
const STDERR_TAIL_LINES: usize = 20;

/// Active port-forward sessions: local_port -> session (including the live kubectl child).
pub struct PortForwardState {
    sessions: Mutex<HashMap<u16, PortForwardSession>>,
}

pub struct PortForwardSession {
    pub namespace: String,
    pub pod: String,
    pub remote_port: u16,
    pub context: Option<String>,
    // No `local_port` field here — it would just duplicate the HashMap key this session is
    // stored under in `PortForwardState::sessions`; see `list_port_forwards`, which reads the
    // port from the map key, not from a struct field.
    // `Arc<Mutex>` because the waiter thread spawned in `start_port_forward` shares the child:
    // it polls `try_wait` to detect early exit, so the lock is only ever held briefly — never
    // across a blocking `wait()`, which would deadlock `stop_port_forward`'s `kill()` the same
    // way `pod_shell.rs`'s doc comment describes.
    child: Arc<Mutex<Child>>,
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
    let context_for_session = context.clone();

    let args = build_port_forward_args(
        &namespace,
        &pod,
        lp,
        remote_port,
        context.as_deref(),
        kubeconfig.as_deref(),
    );
    let mut cmd = hidden_command("kubectl");
    for arg in &args {
        cmd.arg(arg);
    }
    cmd.stdout(Stdio::piped()).stderr(Stdio::piped());

    let mut spawned = cmd
        .spawn()
        .map_err(|e| format!("Failed to start kubectl port-forward: {e}"))?;

    let stdout = spawned.stdout.take().expect("port-forward stdout was piped");
    let stderr = spawned.stderr.take().expect("port-forward stderr was piped");
    let child = Arc::new(Mutex::new(spawned));
    let (tx, rx) = mpsc::channel::<Result<u16, String>>();

    // Bounded tail of stderr so any failure path can quote kubectl's own words.
    let stderr_tail = Arc::new(Mutex::new(VecDeque::with_capacity(STDERR_TAIL_LINES + 1)));

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

    let tx_stderr = tx.clone();
    let tail_for_reader = Arc::clone(&stderr_tail);
    std::thread::spawn(move || {
        let reader = BufReader::new(stderr);
        for line in reader.lines().map_while(Result::ok) {
            eprintln!("[swebkit:port-forward:stderr] {line}");
            {
                let mut tail = tail_for_reader.lock().unwrap();
                if tail.len() == STDERR_TAIL_LINES {
                    tail.pop_front();
                }
                tail.push_back(line.clone());
            }
            if line.to_ascii_lowercase().contains("error") {
                let _ = tx_stderr.send(Err(line));
            }
        }
    });

    // Early-exit detector: `try_wait` polling, never a blocking `wait()` (that would hold the
    // mutex for the session's lifetime and deadlock `stop_port_forward`'s `kill()` — the same
    // trap `pod_shell.rs` documents). A kubectl that dies before printing "Forwarding from"
    // (dead cluster, unknown context, missing pod, failed exec-auth) is reported immediately
    // with its stderr tail instead of surfacing as a bare timeout.
    let tx_waiter = tx.clone();
    let child_for_waiter = Arc::clone(&child);
    let tail_for_waiter = Arc::clone(&stderr_tail);
    std::thread::spawn(move || loop {
        let status = child_for_waiter.lock().unwrap().try_wait();
        match status {
            Ok(Some(exit)) => {
                let _ = tx_waiter.send(Err(format!(
                    "kubectl exited before the port-forward was ready ({exit}). {}",
                    stderr_tail_text(&tail_for_waiter)
                )));
                break;
            }
            // Channel closed (start returned already) — nothing left to report to.
            Err(_) => break,
            _ => std::thread::sleep(Duration::from_millis(200)),
        }
    });

    let actual_port = match rx.recv_timeout(FORWARD_READY_TIMEOUT) {
        Ok(Ok(port)) => port,
        Ok(Err(e)) => {
            let mut c = child.lock().unwrap();
            let _ = c.kill();
            let _ = c.wait();
            return Err(format!("kubectl port-forward failed: {e}"));
        }
        Err(_) => {
            {
                let mut c = child.lock().unwrap();
                let _ = c.kill();
                let _ = c.wait();
            }
            let detail = stderr_tail_text(&stderr_tail);
            return Err(format!(
                "kubectl port-forward did not start within {}s{}",
                FORWARD_READY_TIMEOUT.as_secs(),
                if detail.is_empty() {
                    " — check that kubectl can reach the cluster and the pod is running".to_string()
                } else {
                    format!(" — kubectl said: {detail}")
                },
            ));
        }
    };

    state.sessions.lock().unwrap().insert(
        actual_port,
        PortForwardSession {
            namespace: namespace.clone(),
            pod: pod.clone(),
            remote_port,
            context: context_for_session,
            child,
        },
    );

    eprintln!(
        "[swebkit] Port-forward started: {}:{} -> localhost:{}",
        namespace, pod, actual_port
    );

    Ok(actual_port)
}

/// Builds the `kubectl port-forward` argument list as a plain `Vec<String>` (rather than
/// mutating a `Command` directly) so argument construction is unit-testable without spawning
/// a real process — same convention as `pod_shell.rs`'s `build_exec_args`.
fn build_port_forward_args(
    namespace: &str,
    pod: &str,
    local_port: u16,
    remote_port: u16,
    context: Option<&str>,
    kubeconfig: Option<&str>,
) -> Vec<String> {
    let mut args = vec![
        "port-forward".to_string(),
        "-n".to_string(),
        namespace.to_string(),
        format!("pod/{pod}"),
        // `:8080`, not `0:8080` — the empty local side is kubectl's documented "pick a random
        // local port" form (`kubectl port-forward pod/x :8080`).
        if local_port == 0 {
            format!(":{remote_port}")
        } else {
            format!("{local_port}:{remote_port}")
        },
    ];
    if let Some(ctx) = context.filter(|c| !c.trim().is_empty()) {
        args.push("--context".to_string());
        args.push(ctx.to_string());
    }
    if let Some(kc) = kubeconfig.filter(|k| !k.trim().is_empty()) {
        args.push("--kubeconfig".to_string());
        args.push(kc.to_string());
    }
    args
}

/// Joins the bounded stderr tail into one sentence for error messages. Empty string when
/// kubectl never wrote anything (e.g. it hung in an auth plugin before printing).
fn stderr_tail_text(tail: &Arc<Mutex<VecDeque<String>>>) -> String {
    tail.lock().unwrap().iter().cloned().collect::<Vec<_>>().join("; ")
}

/// Parses kubectl's `Forwarding from 127.0.0.1:<port> -> <remote>` startup line to recover the
/// actual bound local port (needed when the caller asked for an OS-assigned port). Accepts the
/// IPv6 `[::1]:` form too — kubectl normally prints both, but on a host where only the v6
/// listener binds, the v4-only match would still end in a timeout with a working forward.
fn parse_forwarded_port(line: &str) -> Option<u16> {
    let rest = ["127.0.0.1:", "[::1]:", "[::]:"]
        .iter()
        .find_map(|marker| line.find(marker).map(|idx| &line[idx + marker.len()..]))?;
    let port_str = rest.split(|c: char| !c.is_ascii_digit()).next()?;
    port_str.parse().ok()
}

/// Tauri command: stop a port-forward session, killing the underlying kubectl process.
#[tauri::command]
pub fn stop_port_forward(state: State<PortForwardState>, local_port: u16) -> Result<(), String> {
    let session = state
        .sessions
        .lock()
        .unwrap()
        .remove(&local_port)
        .ok_or_else(|| format!("No session on port {}", local_port))?;

    let mut child = session.child.lock().unwrap();
    let _ = child.kill();
    let _ = child.wait();

    eprintln!("[swebkit] Port-forward stopped on port {}", local_port);
    Ok(())
}

/// Kills every active port-forward's kubectl child process. Called on app exit so forwards
/// never survive as orphans holding their local port after the app closes.
pub fn kill_all_port_forwards(state: &PortForwardState) {
    let mut sessions = state.sessions.lock().unwrap();
    for (_, session) in sessions.drain() {
        let mut child = session.child.lock().unwrap();
        let _ = child.kill();
        let _ = child.wait();
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
            context: s.context.clone(),
        })
        .collect()
}

// `rename_all = "camelCase"` matters here: serialized return values are not camelCased on the
// way out the way command *arguments* are (see docs/pitfalls/react-frontend.md, "Tauri does not
// camelCase struct fields on the way out") — without it, `local_port`/`remote_port` would arrive
// in `tauri-bridge.ts` as snake_case while the TS interface expects `localPort`/`remotePort`,
// silently reading `undefined` for both in `PortForwardPanel.tsx`.
#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PortForwardSessionInfo {
    pub local_port: u16,
    pub namespace: String,
    pub pod: String,
    pub remote_port: u16,
    pub context: Option<String>,
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

// ── File manager ─────────────────────────────────────────────────────────────

/// Tauri command: reveal a file or folder in the OS file manager.
///
/// Deliberately *not* gated by `AllowedRoots` — this only asks the OS file
/// manager to open a window (read-only navigation), and call sites pass paths
/// the sidecar itself reported (the collections.json location, linked-root
/// paths) that legitimately live outside any user-picked root.
#[tauri::command]
pub fn reveal_in_explorer(path: String) -> Result<(), String> {
    if path.trim().is_empty() {
        return Err("Path is empty".to_string());
    }
    let metadata =
        std::fs::metadata(&path).map_err(|e| format!("Path does not exist: {e}"))?;
    let (program, args) = reveal_args(Path::new(&path), metadata.is_dir())?;
    let mut cmd = hidden_command(&program);
    #[cfg(windows)]
    for arg in &args {
        // explorer.exe parses its own command line (it never calls
        // CommandLineToArgvW), so `arg()`'s auto-quoting would mangle
        // `/select,"<dir with space>"` — the fragments arrive pre-quoted from
        // `reveal_args` and go on the command line verbatim.
        cmd.raw_arg(arg);
    }
    #[cfg(not(windows))]
    cmd.args(&args);
    let mut child = cmd
        .spawn()
        .map_err(|e| format!("Failed to launch file manager: {e}"))?;
    // explorer.exe exits 1 even on success, and `open`/`xdg-open` return
    // immediately — the launcher's exit status is meaningless, so it's never
    // asserted on. The child is still reaped on a thread so repeated reveals
    // can't pile up zombies (Unix) or leaked process handles (Windows).
    std::thread::spawn(move || {
        let _ = child.wait();
    });
    Ok(())
}

/// Builds `(program, args)` for the OS file-manager call. Returns data rather
/// than mutating a `Command` so argument construction is unit-testable —
/// same convention as `build_port_forward_args`.
///
/// On Windows the returned args are raw command-line fragments (pre-quoted) for
/// `raw_arg`, not `Command::arg` inputs — see `reveal_in_explorer`.
#[cfg(windows)]
fn reveal_args(target: &Path, is_dir: bool) -> Result<(String, Vec<String>), String> {
    let p = target.to_string_lossy();
    // `explorer /select,"<file>"` selects a file in its parent folder;
    // `explorer "<dir>"` opens the folder itself.
    let arg = if is_dir {
        format!("\"{p}\"")
    } else {
        format!("/select,\"{p}\"")
    };
    Ok(("explorer".to_string(), vec![arg]))
}

/// macOS: `open -R <file>` reveals a file in Finder; `open <dir>` opens it.
#[cfg(target_os = "macos")]
fn reveal_args(target: &Path, is_dir: bool) -> Result<(String, Vec<String>), String> {
    let mut args = Vec::new();
    if !is_dir {
        args.push("-R".to_string());
    }
    args.push(target.to_string_lossy().to_string());
    Ok(("open".to_string(), args))
}

/// Linux/BSD: `xdg-open <dir>` — there is no cross-desktop "select this file"
/// flag, so a file resolves to its parent directory.
#[cfg(all(unix, not(target_os = "macos")))]
fn reveal_args(target: &Path, is_dir: bool) -> Result<(String, Vec<String>), String> {
    let dir = if is_dir {
        target.to_path_buf()
    } else {
        // `absolute` (not `canonicalize`) resolves `file.txt` -> `<cwd>/file.txt`
        // without touching the filesystem again, so `.parent()` has something to
        // return even for a bare filename.
        let abs = std::path::absolute(target).map_err(|e| format!("Invalid path: {e}"))?;
        abs.parent()
            .ok_or_else(|| "Cannot determine parent directory".to_string())?
            .to_path_buf()
    };
    Ok(("xdg-open".to_string(), vec![dir.to_string_lossy().to_string()]))
}

// ── Notifications ────────────────────────────────────────────────────────────

/// Tauri command: show an OS-level notification
#[tauri::command]
pub async fn show_notification(
    app: tauri::AppHandle,
    title: String,
    body: String,
) -> Result<(), String> {
    use tauri_plugin_notification::NotificationExt;
    // Real OS toast (Windows action center / macOS / linux notify) — the previous
    // implementation was a blocking MessageBox, which froze the webview and
    // looked nothing like a notification.
    app.notification()
        .builder()
        .title(title)
        .body(body)
        .show()
        .map_err(|e| e.to_string())
}

#[cfg(test)]
mod port_forward_tests {
    use super::{build_port_forward_args, parse_forwarded_port};

    #[test]
    fn parses_ipv4_forwarding_line() {
        assert_eq!(
            parse_forwarded_port("Forwarding from 127.0.0.1:37559 -> 8080"),
            Some(37559)
        );
    }

    #[test]
    fn parses_ipv6_forwarding_line() {
        assert_eq!(parse_forwarded_port("Forwarding from [::1]:37559 -> 8080"), Some(37559));
        assert_eq!(parse_forwarded_port("Forwarding from [::]:37559 -> 8080"), Some(37559));
    }

    #[test]
    fn ignores_unrelated_lines() {
        assert_eq!(parse_forwarded_port("Handling connection for 37559"), None);
        assert_eq!(parse_forwarded_port(""), None);
    }

    #[test]
    fn auto_assign_uses_documented_empty_local_form() {
        let args = build_port_forward_args("default", "my-pod", 0, 8080, None, None);

        assert!(args.contains(&":8080".to_string()));
        assert!(!args.contains(&"0:8080".to_string()));
    }

    #[test]
    fn explicit_local_port_binds_that_port() {
        let args = build_port_forward_args("default", "my-pod", 5000, 8080, None, None);

        assert!(args.contains(&"5000:8080".to_string()));
    }

    #[test]
    fn includes_context_and_kubeconfig_flags() {
        let args = build_port_forward_args(
            "prod",
            "api-abc",
            0,
            443,
            Some("prod-cluster"),
            Some(r"C:\kube\config"),
        );

        assert!(args.windows(2).any(|w| w == ["--context", "prod-cluster"]));
        assert!(args.windows(2).any(|w| w == ["--kubeconfig", r"C:\kube\config"]));
    }

    #[test]
    fn blank_optional_values_are_treated_as_absent() {
        let args = build_port_forward_args("prod", "api-abc", 0, 443, Some(""), Some("  "));

        assert!(!args.contains(&"--context".to_string()));
        assert!(!args.contains(&"--kubeconfig".to_string()));
    }
}

#[cfg(test)]
mod reveal_tests {
    use super::{reveal_args, reveal_in_explorer};
    use std::path::Path;

    #[test]
    fn empty_path_is_rejected() {
        assert!(reveal_in_explorer("   ".to_string()).is_err());
    }

    #[test]
    fn missing_path_is_rejected() {
        let err = reveal_in_explorer("swebkit-no-such-path-9f3b2d".to_string()).unwrap_err();
        assert!(err.contains("does not exist"), "unexpected error: {err}");
    }

    #[cfg(windows)]
    #[test]
    fn windows_file_uses_quoted_select_fragment() {
        let (program, args) = reveal_args(Path::new(r"C:\work dir\file.txt"), false).unwrap();
        assert_eq!(program, "explorer");
        assert_eq!(args, vec![r#"/select,"C:\work dir\file.txt""#.to_string()]);
    }

    #[cfg(windows)]
    #[test]
    fn windows_dir_is_quoted_without_select() {
        let (program, args) = reveal_args(Path::new(r"C:\work dir"), true).unwrap();
        assert_eq!(program, "explorer");
        assert_eq!(args, vec![r#""C:\work dir""#.to_string()]);
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn macos_file_uses_dash_r_dir_omits_it() {
        let (program, args) = reveal_args(Path::new("/Users/me/f.txt"), false).unwrap();
        assert_eq!(program, "open");
        assert_eq!(args, vec!["-R".to_string(), "/Users/me/f.txt".to_string()]);
        let (_, dir_args) = reveal_args(Path::new("/Users/me/dir"), true).unwrap();
        assert_eq!(dir_args, vec!["/Users/me/dir".to_string()]);
    }

    #[cfg(all(unix, not(target_os = "macos")))]
    #[test]
    fn linux_file_opens_parent_dir_opens_itself() {
        let (program, args) = reveal_args(Path::new("/tmp/f.txt"), false).unwrap();
        assert_eq!(program, "xdg-open");
        assert_eq!(args, vec!["/tmp".to_string()]);
        let (_, dir_args) = reveal_args(Path::new("/tmp/dir"), true).unwrap();
        assert_eq!(dir_args, vec!["/tmp/dir".to_string()]);
    }
}
