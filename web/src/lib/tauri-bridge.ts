/// Tauri native bridge wrappers.
/// These functions call Tauri commands when running in the desktop app,
/// and fall back to web equivalents when running in the browser.

function isTauri(): boolean {
  return typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;
}

async function invoke<T>(cmd: string, args?: Record<string, unknown>): Promise<T> {
  const { invoke: tauriInvoke } = await import("@tauri-apps/api/core");
  return tauriInvoke<T>(cmd, args);
}


// ── Clipboard ────────────────────────────────────────────────────────────────

export async function writeClipboard(text: string): Promise<void> {
  if (isTauri()) {
    await invoke("write_clipboard", { text });
  } else {
    await navigator.clipboard.writeText(text);
  }
}

export async function readClipboard(): Promise<string> {
  if (isTauri()) {
    return invoke<string>("read_clipboard");
  }
  return navigator.clipboard.readText();
}

// ── File Dialogs ─────────────────────────────────────────────────────────────

export async function pickFile(title?: string): Promise<string | null> {
  if (isTauri()) {
    return invoke<string | null>("pick_file", { title: title ?? null });
  }
  // Web fallback: use a hidden input element
  return new Promise((resolve) => {
    const input = document.createElement("input");
    input.type = "file";
    input.onchange = () => {
      resolve(input.files?.[0]?.name ?? null);
    };
    input.click();
  });
}

export async function pickDirectory(title?: string): Promise<string | null> {
  if (isTauri()) {
    return invoke<string | null>("pick_directory", { title: title ?? null });
  }
  // Web fallback: no directory picker in browser
  return null;
}

// ── Dialogs ──────────────────────────────────────────────────────────────────

export async function confirmDialog(title: string, message: string): Promise<boolean> {
  if (isTauri()) {
    return invoke<boolean>("confirm_dialog", { title, message });
  }
  return window.confirm(message);
}

export async function alertDialog(title: string, message: string): Promise<void> {
  if (isTauri()) {
    await invoke("alert_dialog", { title, message });
  } else {
    window.alert(message);
  }
}

// ── Port Forward ─────────────────────────────────────────────────────────────

export interface PortForwardSessionInfo {
  localPort: number;
  namespace: string;
  pod: string;
  remotePort: number;
}

export async function startPortForward(
  namespace: string,
  pod: string,
  remotePort: number,
  localPort?: number,
  context?: string | null,
  kubeconfig?: string | null,
): Promise<number> {
  if (isTauri()) {
    return invoke<number>("start_port_forward", {
      namespace,
      pod,
      remotePort,
      localPort: localPort ?? null,
      context: context ?? null,
      kubeconfig: kubeconfig ?? null,
    });
  }
  throw new Error("Port-forward requires the Tauri desktop app");
}

export async function stopPortForward(localPort: number): Promise<void> {
  if (isTauri()) {
    await invoke("stop_port_forward", { localPort });
  } else {
    throw new Error("Port-forward requires the Tauri desktop app");
  }
}

export async function listPortForwards(): Promise<PortForwardSessionInfo[]> {
  if (isTauri()) {
    return invoke<PortForwardSessionInfo[]>("list_port_forwards");
  }
  return [];
}

// ── Git Operations ───────────────────────────────────────────────────────────

export interface GitStatus {
  branch: string;
  ahead: number;
  behind: number;
  staged: number;
  modified: number;
  untracked: number;
  conflicted: number;
}

export interface GitBranch {
  name: string;
  current: boolean;
}

export interface GitFileChange {
  path: string;
  indexState: string;
  worktreeState: string;
  staged: boolean;
  unstaged: boolean;
  untracked: boolean;
  conflicted: boolean;
  origPath: string | null;
}

export interface GitFileDiff {
  original: string | null;
  current: string;
  isBinary: boolean;
}

/**
 * Thrown when git is simply unreachable because the app is running in a browser.
 * Distinct from a command failure so the panel can tell the two apart instead of
 * showing "requires the desktop app" for every error, as it used to.
 */
export class GitUnavailableError extends Error {
  constructor() {
    super("Git actions need the SwebKit desktop app");
    this.name = "GitUnavailableError";
  }
}

/** A git command ran and failed. `message` is git's own stderr. */
export class GitCommandError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "GitCommandError";
  }
}

async function gitInvoke<T>(cmd: string, args: Record<string, unknown>): Promise<T> {
  if (!isTauri()) throw new GitUnavailableError();
  try {
    return await invoke<T>(cmd, args);
  } catch (err) {
    throw new GitCommandError(err instanceof Error ? err.message : String(err));
  }
}

export function isGitAvailable(): boolean {
  return isTauri();
}

export async function gitIsRepo(path: string): Promise<boolean> {
  return gitInvoke<boolean>("git_is_repo", { path });
}

export async function gitStatus(path: string): Promise<GitStatus> {
  return gitInvoke<GitStatus>("git_status", { path });
}

export async function gitChangedFiles(
  path: string,
  subpath?: string | null,
): Promise<GitFileChange[]> {
  return gitInvoke<GitFileChange[]>("git_changed_files", { path, subpath: subpath ?? null });
}

export async function gitBranches(path: string): Promise<GitBranch[]> {
  return gitInvoke<GitBranch[]>("git_branches", { path });
}

export async function gitStagePaths(path: string, paths: string[]): Promise<void> {
  await gitInvoke<void>("git_stage_paths", { path, paths });
}

export async function gitUnstagePaths(path: string, paths: string[]): Promise<void> {
  await gitInvoke<void>("git_unstage_paths", { path, paths });
}

export async function gitRevertPaths(path: string, paths: string[]): Promise<void> {
  await gitInvoke<void>("git_revert_paths", { path, paths });
}

export async function gitDiffFile(path: string, file: string): Promise<GitFileDiff> {
  return gitInvoke<GitFileDiff>("git_diff_file", { path, file });
}

export async function gitCommit(
  path: string,
  message: string,
  subpath?: string | null,
): Promise<void> {
  await gitInvoke<void>("git_commit", { path, message, subpath: subpath ?? null });
}

export async function gitPush(path: string): Promise<string> {
  return gitInvoke<string>("git_push", { path });
}

export async function gitPull(path: string): Promise<string> {
  return gitInvoke<string>("git_pull", { path });
}

export async function gitCheckoutBranch(path: string, branch: string): Promise<void> {
  await gitInvoke<void>("git_checkout_branch", { path, branch });
}

export async function gitCreateBranch(
  path: string,
  branch: string,
  checkout = true,
): Promise<void> {
  await gitInvoke<void>("git_create_branch", { path, branch, checkout });
}

export async function gitRemoteUrl(path: string): Promise<string | null> {
  return gitInvoke<string | null>("git_remote_url", { path });
}

/**
 * Re-admits a repository the user previously picked through an OS dialog.
 * Returns false when the path is not on the persisted grant list.
 */
export async function restoreAllowedRoot(path: string): Promise<boolean> {
  if (!isTauri()) return false;
  try {
    return await invoke<boolean>("restore_allowed_root", { path });
  } catch {
    return false;
  }
}

// ── Filesystem ───────────────────────────────────────────────────────────────

export async function readFile(path: string): Promise<string> {
  if (isTauri()) {
    return await invoke<string>("read_file", { path });
  }
  throw new Error("Filesystem access requires the Tauri desktop app");
}

export async function writeFile(path: string, content: string): Promise<void> {
  if (isTauri()) {
    await invoke("write_file", { path, content });
  } else {
    throw new Error("Filesystem access requires the Tauri desktop app");
  }
}

export async function listDir(path: string): Promise<string[]> {
  if (isTauri()) {
    return await invoke<string[]>("list_dir", { path });
  }
  throw new Error("Filesystem access requires the Tauri desktop app");
}

// ── Notifications ────────────────────────────────────────────────────────────

export async function showNotification(title: string, body: string): Promise<void> {
  if (isTauri()) {
    await invoke("show_notification", { title, body });
  } else if ("Notification" in window) {
    new Notification(title, { body });
  }
}

// ── Sidecar ──────────────────────────────────────────────────────────────────

export async function getSidecarPort(): Promise<number | null> {
  if (isTauri()) {
    return invoke<number>("get_sidecar_port");
  }
  return null;
}

export async function restartSidecar(): Promise<number | null> {
  if (isTauri()) {
    return invoke<number>("restart_sidecar");
  }
  return null;
}

// ── Secret Store (API Client auth) ───────────────────────────────────────────

const WEB_SECRET_VAULT_KEY = "sw-secrets-v1";

export async function saveSecret(key: string, secret: string): Promise<void> {
  if (isTauri()) {
    await invoke("save_secret", { key, secret });
    return;
  }
  const vault = JSON.parse(localStorage.getItem(WEB_SECRET_VAULT_KEY) ?? "{}");
  vault[key] = secret;
  localStorage.setItem(WEB_SECRET_VAULT_KEY, JSON.stringify(vault));
}

export async function getSecret(key: string): Promise<string | null> {
  if (isTauri()) {
    return invoke<string | null>("get_secret", { key });
  }
  const vault = JSON.parse(localStorage.getItem(WEB_SECRET_VAULT_KEY) ?? "{}");
  return vault[key] ?? null;
}

export async function deleteSecret(key: string): Promise<void> {
  if (isTauri()) {
    await invoke("delete_secret", { key });
    return;
  }
  const vault = JSON.parse(localStorage.getItem(WEB_SECRET_VAULT_KEY) ?? "{}");
  delete vault[key];
  localStorage.setItem(WEB_SECRET_VAULT_KEY, JSON.stringify(vault));
}

export async function listSecrets(prefix?: string): Promise<string[]> {
  if (isTauri()) {
    return invoke<string[]>("list_secrets", { prefix: prefix ?? null });
  }
  const vault = JSON.parse(localStorage.getItem(WEB_SECRET_VAULT_KEY) ?? "{}");
  return Object.keys(vault).filter((k) => !prefix || k.startsWith(prefix));
}
