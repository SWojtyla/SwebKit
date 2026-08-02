const TIMESPAN_REGEX = /^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{1,2}):(\d{1,2})(?:\.(\d+))?$/;

function parseTimeSpanString(value: string): number | null {
  const match = value.match(TIMESPAN_REGEX);
  if (!match) return null;

  const sign = match[1] ? -1 : 1;
  const days = parseInt(match[2] || "0", 10);
  const hours = parseInt(match[3] || "0", 10);
  const minutes = parseInt(match[4] || "0", 10);
  const seconds = parseInt(match[5] || "0", 10);

  let fractionMs = 0;
  if (match[6]) {
    const frac = parseFloat("0." + match[6].slice(0, 7));
    fractionMs = Math.round(frac * 1000);
  }

  const totalMs =
    ((days * 24 * 3600 + hours * 3600 + minutes * 60 + seconds) * 1000 + fractionMs) * sign;
  return totalMs;
}

function parseJsonTtl(value: string): number | null {
  try {
    const obj = JSON.parse(value) as unknown;
    if (obj && typeof obj === "object") {
      const candidate = obj as Record<string, unknown>;
      if (typeof candidate.ticks === "number") return candidate.ticks / 10000;
      if (typeof candidate.Ticks === "number") return candidate.Ticks / 10000;
      if (typeof candidate.totalMilliseconds === "number") return candidate.totalMilliseconds;
      if (typeof candidate.TotalMilliseconds === "number") return candidate.TotalMilliseconds;
      if (typeof candidate.totalSeconds === "number") return candidate.totalSeconds * 1000;
      if (typeof candidate.TotalSeconds === "number") return candidate.TotalSeconds * 1000;
    }
  } catch {
    // not JSON
  }
  return null;
}

/**
 * Parses a Redis TTL value returned by the sidecar. Handles:
 * - JSON TimeSpan objects ({ ticks, totalMilliseconds, ... })
 * - ISO/TimeSpan strings such as "01:00:00"
 * - Plain numbers (treated as seconds when small, ticks when huge)
 * Returns remaining milliseconds, or null when there is no TTL.
 */
export function parseTtl(value: string | null | undefined): number | null {
  if (!value) return null;
  const trimmed = value.trim();
  if (trimmed === "" || trimmed === "No expiry") return null;

  if (trimmed.startsWith("{")) {
    const fromJson = parseJsonTtl(trimmed);
    if (fromJson !== null) return fromJson;
  }

  const numeric = Number(trimmed);
  if (!Number.isNaN(numeric)) {
    if (Math.abs(numeric) > 1e9) return numeric / 10000;
    return numeric * 1000;
  }

  return parseTimeSpanString(trimmed);
}

/**
 * Formats a TTL value as a short human-readable string.
 */
export function formatTtl(value: string | null | undefined): string {
  const ms = parseTtl(value);
  if (ms === null) return "No expiry";
  if (ms <= 0) return "Expired";

  const totalSeconds = Math.floor(ms / 1000);
  const seconds = totalSeconds % 60;
  const minutes = Math.floor(totalSeconds / 60) % 60;
  const hours = Math.floor(totalSeconds / 3600);

  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m ${seconds}s`;
  return `${seconds}s`;
}

/**
 * Formats a raw .NET `TimeSpan` string (e.g. `"00:00:00.0483000"`) as a short human-readable
 * duration (`"48.3ms"`, `"1.2s"`) instead of showing the raw serialized value — used for
 * command-latency figures in the Ops/Slow Log tabs.
 */
export function formatDuration(value: string | null | undefined): string {
  if (!value) return "—";
  const ms = parseTimeSpanString(value);
  if (ms === null) return value;

  const abs = Math.abs(ms);
  if (abs < 1) return `${(ms * 1000).toFixed(0)}µs`;
  if (abs < 1000) return `${ms.toFixed(1)}ms`;
  if (abs < 60_000) return `${(ms / 1000).toFixed(2)}s`;
  const totalSeconds = Math.floor(ms / 1000);
  return `${Math.floor(totalSeconds / 60)}m ${totalSeconds % 60}s`;
}

/**
 * Returns a Tailwind background color class for a TTL progress bar based on
 * remaining milliseconds.
 */
export function getTtlColorClass(ms: number | null): string {
  if (ms === null || ms <= 0) return "bg-muted";
  if (ms < 60_000) return "bg-red-500";
  if (ms < 300_000) return "bg-yellow-500";
  return "bg-green-500";
}

/**
 * Formats a byte count as a short human-readable string (e.g. `"1.2K"`, `"3.4M"`).
 */
export function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return "-";
  if (bytes < 1024) return `${bytes}B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}K`;
  return `${(bytes / (1024 * 1024)).toFixed(1)}M`;
}
