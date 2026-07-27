export function getLogLineClass(line: string): string {
  if (line.length === 0 || line[0] === "{") return "log-level-default";
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
