import { methodMeta, toneChipStyle, toneTextStyle } from "./method-meta";

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
