import { execSync, spawn, type ChildProcess } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

/**
 * Shared e2e configuration and sidecar lifecycle helpers.
 *
 * Playwright's `webServer` can start `dotnet run`, but it only kills the parent
 * `dotnet` process. The child `SwebKit.Sidecar` executable keeps running on
 * Windows, which locks `.e2e-appdata` and causes EPERM on the next run. These
 * helpers start the sidecar in `globalSetup` and tear it down (process tree and
 * all) in the returned teardown, keeping all process management in one place.
 */

export const e2eAppDataRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
  ".e2e-appdata",
);

export const sidecarPort = process.env.PLAYWRIGHT_SIDECAR_PORT ?? "5198";
export const vitePort = process.env.PLAYWRIGHT_VITE_PORT ?? "1419";

const sidecarProject = path.resolve(
  e2eAppDataRoot,
  "..",
  "..",
  "src-sidecar",
  "SwebKit.Sidecar.csproj",
);

/**
 * Best-effort kill of a process listening on a local TCP port. On Windows this
 * uses `Get-NetTCPConnection`; on Unix it uses `lsof`. Errors are ignored.
 */
export function killProcessOnPort(port: string) {
  try {
    if (process.platform === "win32") {
      execSync(
        `powershell -Command "Get-NetTCPConnection -LocalPort ${port} -LocalAddress 127.0.0.1 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }"`,
        { stdio: "ignore", timeout: 10000 },
      );
    } else {
      execSync(`lsof -ti:${port} | xargs -r kill -9`, {
        stdio: "ignore",
        timeout: 10000,
      });
    }
  } catch {
    // Best effort: port may be free or we may lack permission.
  }
}

/**
 * Removes and recreates the throwaway appdata directory. Any sidecar holding it
 * is killed first, and the deletion is retried briefly so the OS has time to
 * release file handles.
 */
export async function resetE2EAppData() {
  killProcessOnPort(sidecarPort);

  for (let i = 0; i < 30; i++) {
    try {
      fs.rmSync(e2eAppDataRoot, { recursive: true, force: true });
      break;
    } catch {
      if (i === 29) {
        throw new Error(`Could not remove ${e2eAppDataRoot} after 3 seconds`);
      }
      await new Promise((r) => setTimeout(r, 100));
    }
  }

  fs.mkdirSync(e2eAppDataRoot, { recursive: true });
}

async function waitForSidecarHealth(port: string, timeoutMs: number) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try {
      const res = await fetch(`http://127.0.0.1:${port}/health`);
      if (res.ok) return;
    } catch {
      // not ready yet
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`Sidecar did not become healthy on port ${port} within ${timeoutMs}ms`);
}

/**
 * Starts the .NET sidecar as a child process and waits for its /health endpoint.
 * The throwaway appdata path is set via `SWEBKIT_APPDATA_ROOT`.
 */
export async function startSidecar(): Promise<ChildProcess> {
  const proc = spawn(
    "dotnet",
    ["run", "--project", sidecarProject, "--urls", `http://127.0.0.1:${sidecarPort}`],
    {
      cwd: path.resolve(e2eAppDataRoot, ".."),
      env: { ...process.env, SWEBKIT_APPDATA_ROOT: e2eAppDataRoot },
      stdio: "ignore",
      windowsHide: true,
    },
  );

  await waitForSidecarHealth(sidecarPort, 120_000);
  return proc;
}

/**
 * Stops the sidecar process. On Windows this kills the whole process tree so the
 * child `SwebKit.Sidecar` executable does not outlive the test run.
 */
export function stopSidecar(proc: ChildProcess | undefined) {
  if (!proc || proc.killed) return;

  if (process.platform === "win32") {
    try {
      execSync(`taskkill /T /F /PID ${proc.pid}`, { stdio: "ignore", timeout: 10000 });
    } catch {
      proc.kill("SIGTERM");
    }
  } else {
    proc.kill("SIGTERM");
  }
}
