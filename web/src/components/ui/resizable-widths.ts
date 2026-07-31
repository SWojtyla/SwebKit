/// Pure width resolution for `ResizablePanels`, extracted so the layout maths is
/// unit-testable without mounting a component or measuring a real container.

/** A panel is a fixed pixel width, an `"Nfr"` share of leftover space, or `null` (= `"1fr"`). */
export type PanelWidthSpec = number | string | null | undefined;

/** Parses `"2fr"` → 2. Returns `null` when the spec is not a fraction. */
export function parseFraction(spec: PanelWidthSpec): number | null {
  if (spec == null) return 1; // A bare null means "take a share".
  if (typeof spec === "number") return null;
  const match = /^([0-9]*\.?[0-9]+)fr$/.exec(spec.trim());
  if (!match) return null;
  const value = parseFloat(match[1]);
  return Number.isFinite(value) && value > 0 ? value : 1;
}

/** Parses a fixed width: a number, `"260px"`, or `"25%"` of the container. */
export function parseFixed(spec: PanelWidthSpec, containerWidth: number): number | null {
  if (spec == null) return null;
  if (typeof spec === "number") return spec;
  const trimmed = spec.trim();
  if (trimmed.endsWith("px")) {
    const px = parseFloat(trimmed);
    return Number.isFinite(px) ? px : null;
  }
  if (trimmed.endsWith("%")) {
    const pct = parseFloat(trimmed);
    return Number.isFinite(pct) ? (containerWidth * pct) / 100 : null;
  }
  return null;
}

export interface ResolveOptions {
  specs: PanelWidthSpec[];
  containerWidth: number;
  minWidths: number[];
  /** Total width consumed by the resizer handles between panels. */
  handlesWidth: number;
}

/**
 * Resolves panel specs to pixel widths.
 *
 * Fixed panels keep their width; the leftover is split between the fractional
 * panels in proportion. This is intentionally *not* percentage-of-container: the
 * collections tree's useful width does not scale with the window, so only the
 * request/response split should absorb extra space (DEC-4).
 *
 * Every panel is guaranteed at least its `minWidth`. When the container cannot
 * fit the minimums, the result overflows rather than collapsing a pane to zero —
 * the container scrolls, which is recoverable; a zero-width pane is not.
 */
export function resolvePanelWidths(options: ResolveOptions): number[] {
  const { specs, containerWidth, minWidths, handlesWidth } = options;
  const available = Math.max(0, containerWidth - handlesWidth);

  const fractions = specs.map(parseFraction);
  const fixed = specs.map((spec, i) =>
    fractions[i] === null ? parseFixed(spec, containerWidth) : null,
  );

  const minOf = (i: number) => minWidths[i] ?? 0;

  // Clamp fixed panels to their minimum before measuring what is left.
  const resolved: number[] = specs.map((_, i) =>
    fixed[i] != null ? Math.max(fixed[i]!, minOf(i)) : 0,
  );

  const fixedTotal = resolved.reduce((sum, w) => sum + w, 0);
  const fractionIndexes = specs.map((_, i) => i).filter((i) => fractions[i] !== null);

  if (fractionIndexes.length === 0) return resolved;

  const fractionTotal = fractionIndexes.reduce((sum, i) => sum + fractions[i]!, 0);
  let leftover = Math.max(0, available - fixedTotal);

  // First pass: proportional share, floored at each panel's minimum.
  for (const i of fractionIndexes) {
    const share = (leftover * fractions[i]!) / fractionTotal;
    resolved[i] = Math.max(share, minOf(i));
  }

  // Flooring can overshoot the available space; reclaim from panels that still
  // have slack above their minimum, largest slack first.
  let overflow =
    resolved.reduce((sum, w) => sum + w, 0) - available;
  if (overflow > 0) {
    const slackOrder = [...fractionIndexes].sort(
      (a, b) => resolved[b] - minOf(b) - (resolved[a] - minOf(a)),
    );
    for (const i of slackOrder) {
      if (overflow <= 0) break;
      const slack = resolved[i] - minOf(i);
      const take = Math.min(slack, overflow);
      resolved[i] -= take;
      overflow -= take;
    }
  }

  return resolved.map((w) => Math.round(w));
}

/**
 * Redistributes width between two adjacent panels during a drag, honouring both
 * minimums. Returns the new widths for the pair.
 */
export function resizePair(
  leftStart: number,
  rightStart: number,
  delta: number,
  leftMin: number,
  rightMin: number,
): [number, number] {
  const combined = leftStart + rightStart;
  // A container too small for both minimums cannot be satisfied; keep the split.
  if (combined < leftMin + rightMin) return [leftStart, rightStart];

  let left = leftStart + delta;
  left = Math.max(leftMin, Math.min(left, combined - rightMin));
  return [Math.round(left), Math.round(combined - left)];
}
