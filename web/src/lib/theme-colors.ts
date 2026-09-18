/**
 * Resolves SwebKit theme CSS variables to literal hex colors for canvas renderers.
 *
 * Theme variables hold full `oklch(L C H)` values (see globals.css). Cytoscape
 * parses colors with its own CSS2-style parser — hex/rgb/hsl/named only — so both
 * `var(--x)` and `hsl(var(--x))` silently fall back to default colors. Read the
 * computed value and convert it here instead.
 */

/** Converts an `oklch(L C H [/ A])` string to `#rrggbb`. Non-oklch input is
 * returned unchanged (already-literal colors like `#hex`/`rgb()` pass through). */
export function oklchToHex(input: string): string {
    const match = input
        .trim()
        .match(/^oklch\(\s*([\d.]+%?)\s+([\d.]+)\s+([\d.]+)(?:\s*\/\s*[\d.%]+\s*)?\)$/i);
    if (!match) return input;

    const l = match[1].endsWith("%") ? parseFloat(match[1]) / 100 : parseFloat(match[1]);
    const c = parseFloat(match[2]);
    const h = (parseFloat(match[3]) * Math.PI) / 180;

    // OKLCH → OKLab
    const a = c * Math.cos(h);
    const b = c * Math.sin(h);

    // OKLab → linear sRGB (Björn Ottosson's matrices)
    const l_ = l + 0.3963377774 * a + 0.2158037573 * b;
    const m_ = l - 0.1055613458 * a - 0.0638541728 * b;
    const s_ = l - 0.0894841775 * a - 1.291485548 * b;
    const l3 = l_ * l_ * l_;
    const m3 = m_ * m_ * m_;
    const s3 = s_ * s_ * s_;

    const toSrgb = (v: number) =>
        Math.round(
            255 *
                Math.min(
                    1,
                    Math.max(
                        0,
                        v <= 0.0031308 ? 12.92 * v : 1.055 * Math.pow(v, 1 / 2.4) - 0.055,
                    ),
                ),
        );

    const r = toSrgb(+4.0767416621 * l3 - 3.3077115913 * m3 + 0.2309699292 * s3);
    const g = toSrgb(-1.2684380046 * l3 + 2.6097574011 * m3 - 0.3413193965 * s3);
    const bl = toSrgb(-0.0041960863 * l3 - 0.7034186147 * m3 + 1.707614701 * s3);

    return `#${((r << 16) | (g << 8) | bl).toString(16).padStart(6, "0")}`;
}

/** Reads a CSS custom property off `:root` and returns it as a literal color —
 * oklch values are converted, everything else passes through. Returns
 * `fallback` when the variable is unset or no document exists (SSR/tests). */
export function themeColor(varName: string, fallback: string): string {
    if (typeof getComputedStyle !== "function" || typeof document === "undefined")
        return fallback;
    const raw = getComputedStyle(document.documentElement)
        .getPropertyValue(varName)
        .trim();
    return raw ? oklchToHex(raw) : fallback;
}
