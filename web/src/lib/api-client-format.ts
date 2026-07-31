/// Display formatting for API Client response metadata.
///
/// The sidecar reports an unknown content length as `-1` (see
/// `HttpRequestResult.ContentLength`), which must read as "unknown" rather than
/// a negative byte count.

const UNKNOWN = "—";

/** Byte-count units. Divisor is 1024 — `kB` is used as the conventional label. */
const BYTE_UNITS = ["B", "kB", "MB", "GB", "TB"] as const;

/**
 * Formats a byte count for display next to a response status.
 *
 * Values below 1 kB render as whole bytes; larger values get one decimal so the
 * magnitude stays readable at a glance. A negative length is the sidecar's
 * "unknown" sentinel.
 */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return UNKNOWN;
  if (bytes === 0) return "0 B";

  let value = bytes;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < BYTE_UNITS.length - 1) {
    value /= 1024;
    unitIndex++;
  }

  // Whole bytes never need a fractional part.
  if (unitIndex === 0) return `${Math.round(value)} B`;
  return `${value.toFixed(1)} ${BYTE_UNITS[unitIndex]}`;
}

/**
 * Formats an elapsed duration in milliseconds.
 *
 * Sub-second timings stay in whole milliseconds because that is the resolution
 * developers compare against; longer ones switch to seconds, then minutes.
 */
export function formatElapsed(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) return UNKNOWN;
  if (ms < 1000) return `${Math.round(ms)} ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)} s`;

  const minutes = Math.floor(ms / 60_000);
  const seconds = Math.round((ms % 60_000) / 1000);
  // 90_000 ms must not render as "1m 60s".
  if (seconds === 60) return `${minutes + 1}m 0s`;
  return `${minutes}m ${seconds}s`;
}
