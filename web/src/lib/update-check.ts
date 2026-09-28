// Notify-only update check — deliberately *not* an installer. It fetches a tiny
// `latest.json` manifest published as a release asset (falling back to the GitHub
// releases API), semver-compares against the running version, and caches the
// outcome so the badge survives offline sessions. Phase B (signed auto-update)
// is out of scope by design.
import { getVersion } from "@tauri-apps/api/app";
import packageJson from "../../package.json";

export interface UpdateCheckResult {
    currentVersion: string;
    /** Newest released version known, or null when no check has ever succeeded. */
    latestVersion: string | null;
    updateAvailable: boolean;
    /** Release page the user can open to install manually. */
    releaseUrl: string | null;
    /** Unix ms when this result was fetched (not necessarily now — cache hits keep their time). */
    checkedAt: number;
    source: "manifest" | "github" | "cache" | "unavailable";
    /** Human-readable reason the check couldn't run (offline, private repo, no releases). */
    error?: string;
}

export const UPDATE_MANIFEST_URL =
    "https://github.com/SWojtyla/SwebKit/releases/latest/download/latest.json";
export const RELEASES_API_URL =
    "https://api.github.com/repos/SWojtyla/SwebKit/releases/latest";
export const RELEASES_PAGE_URL = "https://github.com/SWojtyla/SwebKit/releases";

const CACHE_KEY = "swebkit:update-check";

function isTauri(): boolean {
    return typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;
}

/** The version the user is running: the Tauri app version inside the shell, the
 * web package version in plain-browser dev (sidecar keeps the same number). */
export async function getCurrentVersion(): Promise<string> {
    if (isTauri()) {
        try {
            return await getVersion();
        } catch {
            // Fall through to the web bundle's version.
        }
    }
    return packageJson.version;
}

/**
 * Semver-ish compare — tolerant of a leading `v` and missing segments
 * (`1.2` vs `1.2.0` are equal). Returns <0 when a<b, 0 when equal, >0 when a>b.
 * Anything non-numeric (pre-release suffixes) makes the component compare
 * lexically, which is close enough for a notify badge.
 */
export function compareVersions(a: string, b: string): number {
    const pa = a.replace(/^v/, "").split(".");
    const pb = b.replace(/^v/, "").split(".");
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
        const sa = pa[i] ?? "0";
        const sb = pb[i] ?? "0";
        const na = Number(sa);
        const nb = Number(sb);
        if (!Number.isNaN(na) && !Number.isNaN(nb)) {
            if (na !== nb) return na - nb;
        } else {
            const c = sa.localeCompare(sb);
            if (c !== 0) return c;
        }
    }
    return 0;
}

export function readCachedResult(): UpdateCheckResult | null {
    try {
        const raw = localStorage.getItem(CACHE_KEY);
        return raw ? (JSON.parse(raw) as UpdateCheckResult) : null;
    } catch {
        return null;
    }
}

function writeCachedResult(result: UpdateCheckResult): void {
    try {
        localStorage.setItem(CACHE_KEY, JSON.stringify(result));
    } catch {
        // Storage full/blocked — a missed cache write is not worth surfacing.
    }
}

interface RemoteRelease {
    version: string;
    url: string;
    source: "manifest" | "github";
}

async function fetchManifest(signal?: AbortSignal): Promise<RemoteRelease> {
    const res = await fetch(UPDATE_MANIFEST_URL, { signal });
    if (!res.ok) throw new Error(`manifest ${res.status}`);
    const data = (await res.json()) as { version?: string; tag?: string; url?: string };
    const version = data.version ?? data.tag;
    if (!version) throw new Error("manifest missing version");
    return { version: version.replace(/^v/, ""), url: data.url ?? RELEASES_PAGE_URL, source: "manifest" };
}

async function fetchGithubRelease(signal?: AbortSignal): Promise<RemoteRelease> {
    const res = await fetch(RELEASES_API_URL, {
        signal,
        headers: { Accept: "application/vnd.github+json" },
    });
    if (!res.ok) throw new Error(`github ${res.status}`);
    const data = (await res.json()) as { tag_name?: string; html_url?: string };
    if (!data.tag_name) throw new Error("release missing tag_name");
    return {
        version: data.tag_name.replace(/^v/, ""),
        url: data.html_url ?? RELEASES_PAGE_URL,
        source: "github",
    };
}

/**
 * Runs the check: manifest first, GitHub releases API as the fallback. Any failure
 * degrades to the last cached result (offline-safe) or an `unavailable` result when
 * nothing was ever fetched — the caller always gets a renderable answer back.
 */
export async function checkForUpdate(
    currentVersion: string,
    signal?: AbortSignal,
): Promise<UpdateCheckResult> {
    let remote: RemoteRelease | null = null;
    let lastError: string | undefined;
    for (const fetcher of [fetchManifest, fetchGithubRelease] as const) {
        try {
            remote = await fetcher(signal);
            break;
        } catch (error) {
            lastError = error instanceof Error ? error.message : String(error);
        }
    }

    if (remote) {
        const result: UpdateCheckResult = {
            currentVersion,
            latestVersion: remote.version,
            updateAvailable: compareVersions(currentVersion, remote.version) < 0,
            releaseUrl: remote.url,
            checkedAt: Date.now(),
            source: remote.source,
        };
        writeCachedResult(result);
        return result;
    }

    const cached = readCachedResult();
    if (cached) {
        return { ...cached, currentVersion, source: "cache", error: lastError };
    }
    return {
        currentVersion,
        latestVersion: null,
        updateAvailable: false,
        releaseUrl: null,
        checkedAt: Date.now(),
        source: "unavailable",
        error: lastError ?? "no release information found",
    };
}
