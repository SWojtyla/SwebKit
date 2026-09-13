/**
 * Storage blob browser breadcrumb label computation.
 *
 * `prefixHistory` (owned by `StoragePageContext`) records, for each navigation into a
 * deeper virtual folder, the prefix that was active *before* diving deeper — so
 * `prefixHistory[0]` is always `""` (the container root, already shown by its own
 * "container name" crumb) and `prefixHistory[i]` is the immediate parent of whatever
 * comes next (`prefixHistory[i + 1]`, or `currentPrefix` once `i` is the last index).
 *
 * The bug this replaces computed each ancestor crumb's label by string-replacing the
 * *current* (deepest) prefix out of the ancestor prefix — a reference that's constant
 * across the whole loop and, for any ancestor above the last one, isn't even a
 * substring of it. That produced a blank crumb one level deep (the root sentinel
 * `""` has nothing to strip) and concatenated labels two levels deep (multiple path
 * segments survive the failed replace, then lose their "/" separator when it's
 * blanket-stripped afterwards). The fix instead strips each prefix's own immediate
 * parent, which is always a real prefix of it.
 */

/** One clickable ancestor crumb, in left-to-right order (container root excluded). */
export interface StorageBreadcrumbAncestor {
  /** The prefix this crumb navigates back to when clicked. */
  prefix: string;
  /** Index to pass to `handleBreadcrumb` to navigate here. */
  navigateIndex: number;
  /** The single path segment this crumb represents, e.g. "b" for prefix "a/b/". */
  label: string;
}

/** The final `/`-delimited segment of `prefix`, relative to its immediate parent. */
function lastSegment(prefix: string, parentPrefix: string): string {
  const relative = prefix.startsWith(parentPrefix) ? prefix.slice(parentPrefix.length) : prefix;
  const segments = relative.split("/").filter(Boolean);
  return segments[segments.length - 1] ?? "";
}

/**
 * Builds the clickable ancestor crumbs between the container-root crumb and the
 * current (non-clickable) segment. `prefixHistory[0]` is always the root sentinel
 * `""` and never yields a real segment, so it's skipped rather than rendered blank.
 */
export function buildStorageBreadcrumbAncestors(prefixHistory: string[]): StorageBreadcrumbAncestor[] {
  const ancestors: StorageBreadcrumbAncestor[] = [];
  for (let i = 0; i < prefixHistory.length; i++) {
    const parentPrefix = i === 0 ? "" : prefixHistory[i - 1];
    const label = lastSegment(prefixHistory[i], parentPrefix);
    if (!label) continue;
    ancestors.push({ prefix: prefixHistory[i], navigateIndex: i + 1, label });
  }
  return ancestors;
}

/** Label for the trailing, non-clickable "you are here" segment. Empty string at the root. */
export function storageCurrentPrefixLabel(prefixHistory: string[], currentPrefix: string): string {
  if (!currentPrefix) return "";
  const parentPrefix = prefixHistory[prefixHistory.length - 1] ?? "";
  return lastSegment(currentPrefix, parentPrefix);
}
