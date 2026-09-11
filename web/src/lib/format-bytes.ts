/// Shared byte-count formatting.
///
/// Two formats exist because two are genuinely wanted, not by accident: dense list
/// and table cells use the compact form, prose-ish detail headers use the long one.
/// Both were previously copy-pasted into six components.
///
/// `api-client-format.ts` deliberately keeps its own variant — it renders the sidecar's
/// `-1` "unknown content length" sentinel and uses the `kB` convention, and both are
/// asserted by its unit tests and an e2e check.

/**
 * Compact form for dense lists and tables: `842B`, `1.5K`, `2.3M`, `1.20G`.
 * `null`/`undefined` render as `-`, since callers pass sizes that are genuinely unknown
 * (a virtual folder row has no size).
 */
export function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return "-";
  if (bytes < 1024) return `${bytes}B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}K`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)}M`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)}G`;
}

/** Long form for detail panes: `842 B`, `1.5 KB`, `2.3 MB`, `1.20 GB`. */
export function formatBytesLong(bytes: number | null | undefined): string {
  if (bytes == null) return "-";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}
