import { spawn, spawnSync, type ChildProcess } from "node:child_process";
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

// Overridable so a second run can coexist with an in-flight suite — a concurrent
// globalSetup on the default path would wipe the appdata out from under a running
// sidecar (and killProcessOnPort only respects the port, not the appdata dir).
const e2eAppDataRoot =
    process.env.PLAYWRIGHT_APPDATA_ROOT ??
    path.resolve(
        path.dirname(fileURLToPath(import.meta.url)),
        "..",
        ".e2e-appdata",
    );

export const sidecarPort = process.env.PLAYWRIGHT_SIDECAR_PORT ?? "5198";
export const vitePort = process.env.PLAYWRIGHT_VITE_PORT ?? "1419";

const repoRoot = path.resolve(e2eAppDataRoot, "..", "..");
const sidecarProject = path.resolve(
    repoRoot,
    "src-sidecar",
    "SwebKit.Sidecar.csproj",
);
// CI sets this to the pre-built dll so globalSetup doesn't run a full MSBuild
// restore+compile inside the test step — that build spike (Roslyn + node + vite
// coexisting) is what got the suite OOM-killed (exit 137) on the runners.
const sidecarDll = process.env.PLAYWRIGHT_SIDECAR_DLL
    ? path.resolve(repoRoot, process.env.PLAYWRIGHT_SIDECAR_DLL)
    : null;

/**
 * Best-effort kill of a process listening on a local TCP port. On Windows this
 * uses `Get-NetTCPConnection`; on Unix it uses `lsof`. Errors are ignored.
 */
/**
 * True when <paramref name="pid"/> is this process or one of its descendants.
 * The guard that keeps port cleanup from killing this run's own webServer —
 * Playwright spawns vite as OUR child (webServer plugin setup runs before
 * globalSetup), so the fresh dev server is a descendant while a zombie vite
 * from a previous run has been reparented. It also covers the original bug:
 * unfiltered `lsof -i:<port>` matched the test process's own outbound probe
 * socket and `kill -9` SIGKILLed the runner itself (silent exit 137).
 */
function isSelfOrDescendant(pid: number): boolean {
    if (pid === process.pid) return true;
    if (process.platform === "win32") {
        const r = spawnSync(
            "powershell",
            [
                "-NoProfile",
                "-Command",
                '$t=[int]$args[0]; $root=[int]$args[1]; while ($t -gt 0) { $p = Get-CimInstance Win32_Process -Filter "ProcessId=$t" -ErrorAction SilentlyContinue; if ($null -eq $p) { exit 1 }; if ($p.ParentProcessId -eq $root) { exit 0 }; $t = $p.ParentProcessId }; exit 1',
                String(pid),
                String(process.pid),
            ],
            { stdio: "ignore", timeout: 10000 },
        );
        return r.status === 0;
    }
    let p = pid;
    for (let i = 0; i < 32; i++) {
        try {
            const stat = fs.readFileSync(`/proc/${p}/stat`, "utf8");
            // Field 4 is ppid — comm can contain spaces/parens, so parse after
            // the last ')'.
            const ppid = parseInt(
                stat.slice(stat.lastIndexOf(")") + 2).split(" ")[1],
                10,
            );
            if (ppid === process.pid) return true;
            if (!Number.isFinite(ppid) || ppid <= 1) return false;
            p = ppid;
        } catch {
            return false;
        }
    }
    return false;
}

export function killProcessOnPort(port: string) {
    // Numeric-only guard plus argv-style spawns (no shell): the port is a
    // process.env value, so it must never be interpolated into a command line.
    if (!/^\d+$/.test(port)) return;
    try {
        if (process.platform === "win32") {
            // No -LocalAddress filter: a stale vite can hold only [::1]:<port>, which
            // a 127.0.0.1-scoped query misses — Playwright's localhost probe then sees
            // the zombie and browsers get served by it instead of this run's server.
            const out = spawnSync(
                "powershell",
                [
                    "-NoProfile",
                    "-Command",
                    "Get-NetTCPConnection -LocalPort $args[0] -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess",
                    port,
                ],
                { stdio: ["ignore", "pipe", "ignore"], timeout: 10000 },
            );
            for (const pid of out.stdout?.toString().split(/\r?\n/) ?? []) {
                const n = parseInt(pid.trim(), 10);
                if (Number.isFinite(n) && !isSelfOrDescendant(n)) {
                    spawnSync(
                        "powershell",
                        [
                            "-NoProfile",
                            "-Command",
                            "Stop-Process -Id $args[0] -Force -ErrorAction SilentlyContinue",
                            String(n),
                        ],
                        { stdio: "ignore", timeout: 10000 },
                    );
                }
            }
        } else {
            // -sTCP:LISTEN is load-bearing too: plain `-i:<port>` also matches
            // outbound and TIME_WAIT sockets — Playwright's own webServer
            // readiness probe connects to the vite port, so the unfiltered
            // query returned the test process's own pid and `kill -9`
            // SIGKILLed it mid-run (silent exit 137).
            const pids = spawnSync(
                "lsof",
                ["-t", `-iTCP:${port}`, "-sTCP:LISTEN"],
                {
                    stdio: ["ignore", "pipe", "ignore"],
                    timeout: 10000,
                },
            )
                .stdout?.toString()
                .split("\n")
                .map((p) => p.trim())
                .filter((p) => /^\d+$/.test(p));
            for (const pid of pids ?? []) {
                if (!isSelfOrDescendant(parseInt(pid, 10))) {
                    spawnSync("kill", ["-9", pid], {
                        stdio: "ignore",
                        timeout: 10000,
                    });
                }
            }
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
    // Playwright's webServer teardown can orphan vite on [::1]:<vitePort> —
    // clear it here too so the next run's port check doesn't see the zombie.
    // Safe for this run's own vite (spawned as our descendant before
    // globalSetup): isSelfOrDescendant skips it.
    killProcessOnPort(vitePort);

    for (let i = 0; i < 30; i++) {
        try {
            fs.rmSync(e2eAppDataRoot, { recursive: true, force: true });
            break;
        } catch {
            if (i === 29) {
                throw new Error(
                    `Could not remove ${e2eAppDataRoot} after 3 seconds`,
                );
            }
            await new Promise((r) => setTimeout(r, 100));
        }
    }

    fs.mkdirSync(e2eAppDataRoot, { recursive: true });
}

async function waitForSidecarHealth(port: string, timeoutMs: number) {
    if (!/^\d+$/.test(port)) throw new Error(`Invalid sidecar port: ${port}`);
    // Build the URL structurally rather than interpolating the env-sourced port
    // into a request string.
    const health = new URL("http://127.0.0.1/health");
    health.port = port;
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
        try {
            const res = await fetch(health);
            if (res.ok) return;
        } catch {
            // not ready yet
        }
        await new Promise((r) => setTimeout(r, 500));
    }
    throw new Error(
        `Sidecar did not become healthy on port ${port} within ${timeoutMs}ms`,
    );
}

/**
 * Starts the .NET sidecar as a child process and waits for its /health endpoint.
 * The throwaway appdata path is set via `SWEBKIT_APPDATA_ROOT`.
 */
export async function startSidecar(): Promise<ChildProcess> {
    const proc = spawn(
        "dotnet",
        sidecarDll
            ? [sidecarDll, "--urls", `http://127.0.0.1:${sidecarPort}`]
            : [
                  "run",
                  "--project",
                  sidecarProject,
                  "--urls",
                  `http://127.0.0.1:${sidecarPort}`,
              ],
        {
            cwd: path.resolve(e2eAppDataRoot, ".."),
            env: { ...process.env, SWEBKIT_APPDATA_ROOT: e2eAppDataRoot },
            stdio: "ignore",
            windowsHide: true,
        },
    );

    try {
        await waitForSidecarHealth(sidecarPort, 120_000);
    } catch (err) {
        // A timed-out startup must not leak the spawned tree: the child SwebKit.Sidecar.exe
        // can outlive the dotnet run parent and either hold .e2e-appdata open (EPERM next run)
        // or answer health checks on this port while a later run's own child fails to bind.
        stopSidecar(proc);
        killProcessOnPort(sidecarPort);
        throw err;
    }
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
            const pid = proc.pid;
            if (pid !== undefined)
                spawnSync("taskkill", ["/T", "/F", "/PID", String(pid)], {
                    stdio: "ignore",
                    timeout: 10000,
                });
        } catch {
            proc.kill("SIGTERM");
        }
    } else {
        proc.kill("SIGTERM");
    }
}
