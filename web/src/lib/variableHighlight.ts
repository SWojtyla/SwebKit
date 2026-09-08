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

const TOKEN_RE = /\{\{([^{}]*?)\}\}/g;

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
