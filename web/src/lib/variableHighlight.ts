/// Tokenizes `{{variable}}` templates so an input can colour each token by
/// whether it will actually resolve.
///
/// Before this, a URL like `{{Phone_Api_Url}}/incomingcall` rendered as plain
/// white text: a typo in a variable name looked exactly like a correct one until
/// you opened the preview panel or sent the request and got a 404.
///
/// Returns tokens rather than an HTML string, for the same reason
/// `bodyHighlight.ts` does — the text is user input, so letting React escape it
/// removes any `dangerouslySetInnerHTML` question.

import { isLikelySecret } from "./variable-utils";

/**
 * - `resolved` — in scope with a previewable value.
 * - `deferred` — in scope, but the value is only known at send time (a generated
 *   value, a Windows credential-store entry, a Key Vault secret). Deliberately
 *   distinct from `unresolved`: flagging these red would cry wolf on every
 *   correctly-configured secret.
 * - `unresolved` — not in scope at all. This is the one that is a mistake.
 */
export type VariableTokenKind = "text" | "resolved" | "deferred" | "unresolved";

export interface VariableToken {
  text: string;
  kind: VariableTokenKind;
  /** The variable name, for token kinds other than `text`. */
  name?: string;
  /** The resolved value, when previewable. */
  value?: string | null;
}

/// Source text for the `{{name}}` pattern. Exported because CodeMirror's
/// `MatchDecorator` needs to own a `RegExp` instance with its own `lastIndex` —
/// sharing this module's would have the two scanners corrupt each other's cursor.
/// Keeping the source in one place is what stops the body editor and the URL
/// field from disagreeing about what counts as a token.
export const VARIABLE_TOKEN_SOURCE = "\\{\\{([^{}]*?)\\}\\}";

const TOKEN_RE = new RegExp(VARIABLE_TOKEN_SOURCE, "g");

export function classifyVariable(
  name: string,
  scope: Record<string, string | null>,
): Exclude<VariableTokenKind, "text"> {
  if (!Object.prototype.hasOwnProperty.call(scope, name)) return "unresolved";
  return scope[name] == null ? "deferred" : "resolved";
}

/** Splits template text into plain runs and `{{variable}}` tokens. Concatenating them returns the input verbatim. */
export function tokenizeVariables(
  text: string,
  scope: Record<string, string | null>,
): VariableToken[] {
  if (!text) return [];
  if (!text.includes("{{")) return [{ text, kind: "text" }];

  const tokens: VariableToken[] = [];
  let cursor = 0;
  let match: RegExpExecArray | null;
  TOKEN_RE.lastIndex = 0;
  while ((match = TOKEN_RE.exec(text)) !== null) {
    if (match.index > cursor) {
      tokens.push({ text: text.slice(cursor, match.index), kind: "text" });
    }
    const name = match[1].trim();
    // `{{}}` and `{{   }}` name nothing, so there is nothing to resolve — leave
    // them as plain text rather than reporting a missing variable called "".
    if (name === "") {
      tokens.push({ text: match[0], kind: "text" });
    } else {
      tokens.push({
        text: match[0],
        kind: classifyVariable(name, scope),
        name,
        value: scope[name] ?? null,
      });
    }
    cursor = match.index + match[0].length;
  }
  if (cursor < text.length) {
    tokens.push({ text: text.slice(cursor), kind: "text" });
  }
  return tokens;
}

/** True when any `{{token}}` in the text names a variable that is not in scope. */
export function hasUnresolvedVariables(text: string, scope: Record<string, string | null>): boolean {
  return tokenizeVariables(text, scope).some((t) => t.kind === "unresolved");
}

/** The names of every `{{token}}` that is not in scope, in order, without duplicates. */
export function unresolvedVariableNames(
  text: string,
  scope: Record<string, string | null>,
): string[] {
  const seen = new Set<string>();
  for (const token of tokenizeVariables(text, scope)) {
    if (token.kind === "unresolved" && token.name) seen.add(token.name);
  }
  return [...seen];
}

/**
 * The CSS class a `{{name}}` token should carry, or `null` for a token that names
 * nothing (`{{}}`) — left as plain text rather than reported as a missing variable
 * called "".
 *
 * Lives here rather than beside the CodeMirror extension that consumes it so it can
 * be unit-tested: vitest runs node-only, with no DOM for `@codemirror/view`.
 */
export function variableMarkClass(
  rawName: string,
  scope: Record<string, string | null>,
): string | null {
  const name = rawName.trim();
  if (name === "") return null;
  return `var-tok-${classifyVariable(name, scope)}`;
}

/**
 * The hover target and description for the token covering `offset` within a single
 * line, or `null` when the offset is not inside one. `from`/`to` are offsets within
 * `lineText`.
 */
export function variableHoverAt(
  lineText: string,
  offset: number,
  scope: Record<string, string | null>,
): { from: number; to: number; text: string } | null {
  let cursor = 0;
  // Tokens concatenate back to the source verbatim, so a running cursor over them
  // yields exact offsets without a second scan.
  for (const token of tokenizeVariables(lineText, scope)) {
    const from = cursor;
    const to = cursor + token.text.length;
    if (token.kind !== "text" && offset >= from && offset < to) {
      const text = describeVariableToken(token);
      return text === null ? null : { from, to, text };
    }
    cursor = to;
  }
  return null;
}

/// The hover description for a single token, shared by the `VariableInput` overlay
/// and the CodeMirror body editor. Both surfaces must word the same state
/// identically — a variable that reads "not defined" in the URL bar and shows
/// nothing in the body is exactly the inconsistency this exists to prevent.
export function describeVariableToken(token: VariableToken): string | null {
  if (!token.name) return null;
  if (token.kind === "unresolved") return `${token.name} — not defined`;
  if (token.kind === "deferred") return `${token.name} — resolved when sent`;
  return `${token.name} = ${isLikelySecret(token.name) ? "••••••••" : token.value}`;
}
