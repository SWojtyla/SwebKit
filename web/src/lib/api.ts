// Sidecar port is fixed (5199) only in dev, where the sidecar is started
// separately via `dotnet run`. In production Tauri lets the OS pick a free
// port and reports the real one via the `get_sidecar_port` command, so this
// must be re-resolved at startup (see `initSidecarBaseUrl`) before anything
// fetches — it can't be a one-shot module-load constant anymore.
let SIDECAR_BASE_URL = (() => {
  return (import.meta as any).env?.VITE_SIDECAR_URL ?? "http://localhost:5199";
})();

/// Resolves the real sidecar port from Tauri (production: OS-assigned; dev:
/// fixed 5199) and updates `SIDECAR_BASE_URL` in place. No-op outside Tauri
/// (plain browser dev mode keeps the static default above). Must be awaited
/// before the app renders anything that calls `apiFetch`/`apiSend`.
export async function initSidecarBaseUrl(): Promise<void> {
  if (typeof window === "undefined" || !("__TAURI_INTERNALS__" in window)) {
    return;
  }
  const { getSidecarPort } = await import("./tauri-bridge");
  const port = await getSidecarPort();
  if (port) {
    SIDECAR_BASE_URL = `http://127.0.0.1:${port}`;
  }
}

export async function apiFetch<T>(
  path: string,
  options?: RequestInit,
): Promise<T> {
  const res = await fetch(`${SIDECAR_BASE_URL}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...options?.headers,
    },
  });

  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new Error(`API ${res.status}: ${body || res.statusText}`);
  }

  return res.json() as Promise<T>;
}

export async function apiSend<T>(
  path: string,
  method: "POST" | "PUT" | "PATCH" | "DELETE",
  body?: unknown,
): Promise<T> {
  const res = await fetch(`${SIDECAR_BASE_URL}${path}`, {
    method,
    headers: { "Content-Type": "application/json" },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(`API ${res.status}: ${text || res.statusText}`);
  }

  const text = await res.text().catch(() => "");
  return (text ? (JSON.parse(text) as T) : undefined) as T;
}

export { SIDECAR_BASE_URL };
