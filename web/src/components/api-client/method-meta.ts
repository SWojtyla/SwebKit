/// The single source of truth for how HTTP methods and response statuses are
/// coloured and labelled across the API Client.
///
/// Colours come from the Aurora design tokens (`--info`, `--success`, …) rather
/// than raw Tailwind palette classes, so every surface follows the active theme
/// — including `fancy`, which the previous hardcoded `text-gray-500` ignored.

import type { ApiRequestMethod } from "@/lib/types";

export type Tone =
    | "info"
    | "success"
    | "warning"
    | "destructive"
    | "accent"
    | "neutral";

/** Ink token backing each tone — the readable text colour in the SwebOS
 *  state vocabulary (`--state-*-ink`), matching the prototype's `.mtd-*` /
 *  `.badge-*` choices: GET→working, POST→done, PUT→needs, DEL→blocked. */
const TONE_INK_VAR: Record<Tone, string> = {
    info: "--state-working-ink",
    success: "--state-done-ink",
    warning: "--state-needs-ink",
    destructive: "--state-blocked-ink",
    accent: "--accent-ink",
    neutral: "--text-subtle",
};

/** Soft-fill token backing each tone — the pill/chip wash (`--state-*-soft`). */
const TONE_SOFT_VAR: Record<Tone, string> = {
    info: "--state-working-soft",
    success: "--state-done-soft",
    warning: "--state-needs-soft",
    destructive: "--state-blocked-soft",
    accent: "--accent-soft",
    neutral: "--surface-2",
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
    return { color: `var(${TONE_INK_VAR[tone]})` };
}

/**
 * Inline style for a soft state pill: ink text on the tone's soft wash,
 * mirroring the spec's `bg-{state}-soft` / `text-{state}-ink` pill.
 */
export function toneChipStyle(tone: Tone): React.CSSProperties {
    return {
        color: `var(${TONE_INK_VAR[tone]})`,
        backgroundColor: `var(${TONE_SOFT_VAR[tone]})`,
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
