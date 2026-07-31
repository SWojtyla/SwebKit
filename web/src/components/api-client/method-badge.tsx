/// The single source of truth for how HTTP methods and response statuses are
/// coloured and labelled across the API Client.
///
/// Colours come from the Aurora design tokens (`--info`, `--success`, …) rather
/// than raw Tailwind palette classes, so every surface follows the active theme
/// — including `fancy`, which the previous hardcoded `text-gray-500` ignored.

import type { ApiRequestMethod } from "@/lib/types";

export type Tone = "info" | "success" | "warning" | "destructive" | "accent" | "neutral";

/** CSS custom property backing each tone. */
const TONE_VAR: Record<Tone, string> = {
  info: "--info",
  success: "--success",
  warning: "--warning",
  destructive: "--destructive",
  accent: "--aurora-2",
  neutral: "--muted-foreground",
};

export interface MethodMeta {
  /** Conventional short label. Never a truncated word. */
  short: string;
  tone: Tone;
}

/**
 * Typed as a total record so adding a method to `ApiRequestMethod` without a
 * label here is a compile error rather than a silent fallback.
 */
export const METHOD_META: Record<ApiRequestMethod, MethodMeta> = {
  Get: { short: "GET", tone: "info" },
  Post: { short: "POST", tone: "success" },
  Put: { short: "PUT", tone: "warning" },
  Patch: { short: "PATCH", tone: "warning" },
  Delete: { short: "DEL", tone: "destructive" },
  Head: { short: "HEAD", tone: "neutral" },
  Options: { short: "OPT", tone: "neutral" },
  GraphQl: { short: "GQL", tone: "accent" },
  WebSocket: { short: "WS", tone: "accent" },
};

const FALLBACK: MethodMeta = { short: "?", tone: "neutral" };

/** Tolerates unknown strings, since tab state carries `method` as a plain string. */
export function methodMeta(method: string): MethodMeta {
  return METHOD_META[method as ApiRequestMethod] ?? FALLBACK;
}

/** Inline style for text tinted with a tone. */
export function toneTextStyle(tone: Tone): React.CSSProperties {
  return { color: `var(${TONE_VAR[tone]})` };
}

/**
 * Inline style for a filled chip. The low-alpha background mirrors how response
 * status pills are already rendered, so methods and statuses read as one system.
 */
export function toneChipStyle(tone: Tone): React.CSSProperties {
  const v = TONE_VAR[tone];
  return {
    color: `var(${v})`,
    backgroundColor: `color-mix(in oklch, var(${v}) 14%, transparent)`,
  };
}

/** Maps an HTTP status code onto the shared tone vocabulary. */
export function statusTone(code: number): Tone {
  if (code === 0) return "destructive";
  if (code >= 200 && code < 300) return "success";
  if (code >= 300 && code < 400) return "info";
  if (code >= 400 && code < 500) return "warning";
  return "destructive";
}

interface MethodBadgeProps {
  method: string;
  /** `chip` for tinted-background badges, `text` for dense inline use. */
  variant?: "chip" | "text";
  className?: string;
}

export function MethodBadge({ method, variant = "chip", className = "" }: MethodBadgeProps) {
  const { short, tone } = methodMeta(method);
  const base = "shrink-0 font-mono text-[10px] font-bold tracking-wide";

  if (variant === "text") {
    return (
      <span className={`${base} ${className}`} style={toneTextStyle(tone)} data-testid="method-badge">
        {short}
      </span>
    );
  }

  return (
    <span
      className={`${base} rounded px-1.5 py-0.5 ${className}`}
      style={toneChipStyle(tone)}
      data-testid="method-badge"
    >
      {short}
    </span>
  );
}

interface CountBadgeProps {
  count: number;
  className?: string;
}

/** Uniform count pill for request/response tab strips. */
export function CountBadge({ count, className = "" }: CountBadgeProps) {
  if (count <= 0) return null;
  return (
    <span
      className={`rounded-full bg-muted px-1.5 text-[10px] leading-4 text-muted-foreground ${className}`}
    >
      {count}
    </span>
  );
}
