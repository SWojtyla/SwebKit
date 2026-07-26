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
): Promise<number> {
  if (isTauri()) {
    return invoke<number>("start_port_forward", {
      namespace,
      pod,
      remotePort,
      localPort: localPort ?? null,
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
}

export interface GitBranch {
  name: string;
  current: boolean;
}

export async function gitStatus(path: string): Promise<GitStatus | null> {
  if (isTauri()) {
    return invoke<GitStatus>("git_status", { path });
  }
  return null;
}

export async function gitBranches(path: string): Promise<GitBranch[]> {
  if (isTauri()) {
    return invoke<GitBranch[]>("git_branches", { path });
  }
  return [];
}

export async function gitCommit(path: string, message: string): Promise<void> {
  if (isTauri()) {
    await invoke("git_commit", { path, message });
  } else {
    throw new Error("Git operations require the Tauri desktop app");
  }
}

export async function gitPush(path: string): Promise<string> {
  if (isTauri()) {
    return invoke<string>("git_push", { path });
  }
  throw new Error("Git operations require the Tauri desktop app");
}

export async function gitPull(path: string): Promise<string> {
  if (isTauri()) {
    return invoke<string>("git_pull", { path });
  }
  throw new Error("Git operations require the Tauri desktop app");
}

export async function gitStageAll(path: string): Promise<void> {
  if (isTauri()) {
    await invoke("git_stage_all", { path });
  } else {
    throw new Error("Git operations require the Tauri desktop app");
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
