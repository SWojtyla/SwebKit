/// Token-level syntax highlighting for pod log lines.
///
/// Complements `logLevel.ts`, which only tints a whole line by severity — that
/// leaves the lines operators actually stare at (a .NET exception header, its
/// stack frames, an embedded JSON payload) as one undifferentiated wall of text.
///
/// Returns tokens rather than an HTML string, for the same reason
/// `bodyHighlight.ts` does: log text is arbitrary container output, so letting
/// React escape it removes any `dangerouslySetInnerHTML` question entirely.
///
/// Colours reuse the shared `--cm-*` custom properties, so logs, the API Client
/// body viewer and the AKS YAML viewer read as one system across every theme.

import { STACK_FRAME_RE } from "./logLevel";

export type LogTokenClass =
  | "timestamp"
  | "level-error"
  | "level-warn"
  | "level-info"
  | "level-debug"
  | "exception"
  | "symbol"
  | "keyword"
  | "separator"
  | "location"
  | "url"
  | "guid"
  | "key"
  | "string"
  | "number"
  | "bool"
  | "null"
  | "punct"
  | "plain";

export interface LogToken {
  text: string;
  cls: LogTokenClass;
}

/**
 * Beyond this length a line is almost certainly a serialized blob rather than
 * something a human reads token by token, and scanning it costs more than the
 * highlighting is worth. The render path only ever tokenizes the ~200 visible
 * lines, but a single multi-megabyte line would still stall a frame.
 */
const MAX_HIGHLIGHT_LENGTH = 4000;

const ERROR_LEVELS = new Set(["ERR", "ERROR", "FTL", "FATAL", "CRIT", "CRITICAL"]);
const WARN_LEVELS = new Set(["WRN", "WARN", "WARNING"]);
const INFO_LEVELS = new Set(["INF", "INFO", "INFORMATION"]);

function levelClass(raw: string): LogTokenClass {
  const word = raw.replace(/[[\]]/g, "").toUpperCase();
  if (ERROR_LEVELS.has(word)) return "level-error";
  if (WARN_LEVELS.has(word)) return "level-warn";
  if (INFO_LEVELS.has(word)) return "level-info";
  return "level-debug";
}

/// One pass, alternatives ordered most specific first: JS alternation takes the
/// leftmost match and, at a given position, the first alternative that matches —
/// not the longest — so e.g. `guid` must precede `number`, and `url` must precede
/// anything that would stop at the `:` in `https:`.
const TOKEN_RE = new RegExp(
  [
    // 2026-09-08T11:12:13.9556368Z / 2026-09-08 11:12:13+02:00
    String.raw`(?<timestamp>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?(?:Z|[+-]\d{2}:?\d{2})?)`,
    // Bare clock time, as Serilog's default console template emits.
    String.raw`(?<clock>\b\d{2}:\d{2}:\d{2}(?:[.,]\d{1,7})?\b)`,
    String.raw`(?<guid>\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b)`,
    String.raw`(?<url>\bhttps?:\/\/[^\s"'<>\\]+)`,
    // Source locations: /src/Foo.cs:line 141, C:\src\Foo.cs:line 141
    String.raw`(?<location>(?:[A-Za-z]:\\|\/)[^\s"'<>]*\.[A-Za-z][A-Za-z0-9]{0,7}(?::line\s+\d+)?)`,
    String.raw`(?<level>\[(?:ERR|ERROR|FTL|FATAL|CRIT|CRITICAL|WRN|WARN|WARNING|INF|INFO|INFORMATION|DBG|DEBUG|TRC|TRACE|VRB|VERBOSE)\]|\b(?:ERROR|FATAL|CRITICAL|WARNING|WARN|INFO|DEBUG|TRACE|VERBOSE)\b)`,
    // The inner-exception arrow reads as structure worth following, so it is a
    // keyword; the `--- End of … ---` rules between frame groups are scaffolding,
    // so they get their own muted class.
    String.raw`(?<keyword>-{2,3}>)`,
    String.raw`(?<separator>-{3}[^-\n]+-{3})`,
    // A JSON key: a quoted string whose next non-space character is a colon.
    String.raw`(?<key>"(?:[^"\\]|\\.)*"(?=\s*:))`,
    String.raw`(?<string>"(?:[^"\\]|\\.)*")`,
    String.raw`(?<bool>\b(?:true|false)\b)`,
    String.raw`(?<null>\bnull\b)`,
    // Dotted, capitalised identifiers — namespaces, types and method paths — plus
    // bare exception type names. Deliberately one alternative rather than two: an
    // `…(?:Exception|Error)` variant tried ahead of the general dotted rule makes
    // the engine re-walk every segment of a long namespace before failing, which
    // is quadratic on a stack trace. Matched once here, classified in `scan`.
    String.raw`(?<symbol>\b[A-Z][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+\b|\b[A-Z][A-Za-z0-9_]*(?:Exception|Error)\b)`,
    String.raw`(?<number>\b\d+(?:\.\d+)?\b)`,
    String.raw`(?<punct>[{}[\],:])`,
  ].join("|"),
  "g",
);

const GROUP_CLASSES: Record<string, LogTokenClass> = {
  timestamp: "timestamp",
  clock: "timestamp",
  guid: "guid",
  url: "url",
  location: "location",
  keyword: "keyword",
  separator: "separator",
  key: "key",
  string: "string",
  bool: "bool",
  null: "null",
  number: "number",
  punct: "punct",
};

const GROUP_NAMES = Object.keys(GROUP_CLASSES);

const EXCEPTION_SUFFIX_RE = /(?:Exception|Error)$/;


function pushPlain(tokens: LogToken[], text: string): void {
  if (!text) return;
  const last = tokens[tokens.length - 1];
  if (last?.cls === "plain") last.text += text;
  else tokens.push({ text, cls: "plain" });
}

function scan(tokens: LogToken[], text: string): void {
  TOKEN_RE.lastIndex = 0;
  let cursor = 0;
  let match: RegExpExecArray | null;
  while ((match = TOKEN_RE.exec(text)) !== null) {
    // No alternative can match empty, but a zero-length match would spin forever;
    // guard rather than rely on that staying true.
    if (match[0].length === 0) {
      TOKEN_RE.lastIndex += 1;
      continue;
    }
    pushPlain(tokens, text.slice(cursor, match.index));
    const groups = match.groups ?? {};
    let cls: LogTokenClass;
    if (groups.level !== undefined) {
      cls = levelClass(match[0]);
    } else if (groups.symbol !== undefined) {
      cls = EXCEPTION_SUFFIX_RE.test(match[0]) ? "exception" : "symbol";
    } else {
      const name = GROUP_NAMES.find((g) => groups[g] !== undefined);
      cls = name ? GROUP_CLASSES[name] : "plain";
    }
    tokens.push({ text: match[0], cls });
    cursor = match.index + match[0].length;
  }
  pushPlain(tokens, text.slice(cursor));
}

/** Splits one log line into coloured tokens. Concatenating them returns the input verbatim. */
export function tokenizeLogLine(line: string): LogToken[] {
  if (line.length === 0) return [];
  if (line.length > MAX_HIGHLIGHT_LENGTH) return [{ text: line, cls: "plain" }];

  const tokens: LogToken[] = [];
  const frame = STACK_FRAME_RE.exec(line);
  if (frame) {
    // Emitted up front rather than as a TOKEN_RE alternative, because a bare `at`
    // is far too common in log prose to keyword-highlight anywhere else.
    pushPlain(tokens, frame[1]);
    tokens.push({ text: frame[2], cls: "keyword" });
    scan(tokens, line.slice(frame[0].length));
  } else {
    scan(tokens, line);
  }
  return tokens;
}
