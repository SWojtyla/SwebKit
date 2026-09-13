/// Pure validation/clamping helper for numeric settings fields committed via `DraftInput`.
///
/// `parseInt(v) || fallback` (the pattern this replaces in AksSettings/RedisSettings) only
/// catches falsy results — `NaN` and `0` — so a negative number (or anything above a sane
/// upper bound) passed straight through unchanged and only surfaced later as an opaque
/// connection failure. This clamps to `[min, max]` and reports whether it had to.

export interface ClampIntOptions {
  min: number;
  max?: number;
  /** Used when the input can't be parsed as an integer at all (empty, non-numeric). */
  fallback: number;
}

export interface ClampIntResult {
  value: number;
  /** True if the parsed value was out of range and got pulled back to `min`/`max`. */
  clamped: boolean;
  /** True if the input couldn't be parsed as an integer at all (`fallback` was used). */
  invalid: boolean;
}

export function clampInt(raw: string, { min, max, fallback }: ClampIntOptions): ClampIntResult {
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed)) {
    return { value: fallback, clamped: false, invalid: true };
  }

  if (parsed < min) {
    return { value: min, clamped: true, invalid: false };
  }
  if (max != null && parsed > max) {
    return { value: max, clamped: true, invalid: false };
  }
  return { value: parsed, clamped: false, invalid: false };
}
