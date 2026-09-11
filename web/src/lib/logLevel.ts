/**
 * `   at Fully.Qualified.Method(...)` — the shape of a managed stack frame, with
 * the leading indent and the `at ` keyword captured for highlighting.
 *
 * The dotted-name-then-`(` lookahead is what separates a frame from a message
 * that merely opens with the word "at".
 */
export const STACK_FRAME_RE = /^(\s*)(at\s+)(?=[^\s(]*\.[^\s(]*\()/;

/** `--- End of inner exception stack trace ---` and its siblings. */
const TRACE_SEPARATOR_RE = /^\s*-{3}\s/;

export function getLogLineClass(line: string): string {
  if (line.length === 0 || line[0] === "{") return "log-level-default";
  // Frames and the separators between them are structurally part of an exception
  // but carry none of its message, so they are dimmed rather than coloured — a
  // 40-frame trace should not render as 40 red lines around one header.
  if (STACK_FRAME_RE.test(line) || TRACE_SEPARATOR_RE.test(line)) return "log-level-frame";
  const window = line.length > 120 ? line.slice(0, 120) : line;
  if (
    window.includes("[ERR]") ||
    window.includes("[FATAL]") ||
    window.includes("[CRIT]") ||
    window.includes("ERROR") ||
    window.includes("FATAL") ||
    window.includes("CRITICAL")
  ) {
    return "log-level-error";
  }
  if (
    window.includes("[WRN]") ||
    window.includes("[WARN]") ||
    window.includes("WARN") ||
    window.includes("WARNING")
  ) {
    return "log-level-warn";
  }
  if (
    window.includes("[DBG]") ||
    window.includes("[TRC]") ||
    window.includes("[VRB]") ||
    window.includes("DEBUG") ||
    window.includes("TRACE")
  ) {
    return "log-level-debug";
  }
  return "log-level-default";
}
