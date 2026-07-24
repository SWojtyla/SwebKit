const SIDECAR_BASE_URL = (() => {
  // In dev, the sidecar port is injected by Tauri via env or a runtime command.
  // In production, Tauri spawns the sidecar and provides the port.
  // Fallback to a default for browser dev mode.
  return (import.meta as any).env?.VITE_SIDECAR_URL ?? "http://localhost:5199";
})();

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
  return apiFetch<T>(path, {
    method,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
}

export { SIDECAR_BASE_URL };
