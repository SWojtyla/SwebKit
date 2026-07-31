/// Lightweight JSON/XML tokenizer for the small-body `<pre>` rendering path.
///
/// Returns tokens rather than an HTML string (unlike `yamlHighlight.ts`, whose
/// input is YAML SwebKit itself fetched) because response bodies come from
/// arbitrary third-party APIs. Letting React escape the text removes any
/// `dangerouslySetInnerHTML` question entirely.
///
/// Token classes map onto the same `--cm-*` custom properties the CodeMirror
/// path uses, so the two rendering paths are visually identical.
/// See docs/features/active/api-client-ux-overhaul/decisions.md DEC-3.

export type TokenClass =
  | "key"
  | "string"
  | "number"
  | "bool"
  | "null"
  | "punct"
  | "tag"
  | "attr"
  | "comment"
  | "plain";

export interface BodyToken {
  text: string;
  cls: TokenClass;
}

const JSON_PUNCT = new Set(["{", "}", "[", "]", ",", ":"]);

function readJsonString(src: string, start: number): number {
  // start points at the opening quote.
  let i = start + 1;
  while (i < src.length) {
    const c = src[i];
    if (c === "\\") {
      i += 2;
      continue;
    }
    if (c === '"') return i + 1;
    i++;
  }
  return src.length; // Unterminated — consume the remainder rather than looping.
}

/** True when the next non-whitespace character is `:`, making this string a key. */
function isFollowedByColon(src: string, from: number): boolean {
  for (let i = from; i < src.length; i++) {
    const c = src[i];
    if (c === " " || c === "\t" || c === "\n" || c === "\r") continue;
    return c === ":";
  }
  return false;
}

export function tokenizeJson(src: string): BodyToken[] {
  const tokens: BodyToken[] = [];
  let i = 0;
  let plainStart = -1;

  const flushPlain = (end: number) => {
    if (plainStart >= 0) {
      tokens.push({ text: src.slice(plainStart, end), cls: "plain" });
      plainStart = -1;
    }
  };

  while (i < src.length) {
    const c = src[i];

    if (c === '"') {
      flushPlain(i);
      const end = readJsonString(src, i);
      const cls: TokenClass = isFollowedByColon(src, end) ? "key" : "string";
      tokens.push({ text: src.slice(i, end), cls });
      i = end;
      continue;
    }

    if (JSON_PUNCT.has(c)) {
      flushPlain(i);
      tokens.push({ text: c, cls: "punct" });
      i++;
      continue;
    }

    if (c === "-" || (c >= "0" && c <= "9")) {
      flushPlain(i);
      let end = i + 1;
      while (end < src.length && /[0-9.eE+-]/.test(src[end])) end++;
      tokens.push({ text: src.slice(i, end), cls: "number" });
      i = end;
      continue;
    }

    if (src.startsWith("true", i) || src.startsWith("false", i)) {
      flushPlain(i);
      const word = src.startsWith("true", i) ? "true" : "false";
      tokens.push({ text: word, cls: "bool" });
      i += word.length;
      continue;
    }

    if (src.startsWith("null", i)) {
      flushPlain(i);
      tokens.push({ text: "null", cls: "null" });
      i += 4;
      continue;
    }

    if (plainStart < 0) plainStart = i;
    i++;
  }

  flushPlain(src.length);
  return tokens;
}

export function tokenizeXml(src: string): BodyToken[] {
  const tokens: BodyToken[] = [];
  let i = 0;
  let textStart = -1;

  const flushText = (end: number) => {
    if (textStart >= 0) {
      tokens.push({ text: src.slice(textStart, end), cls: "plain" });
      textStart = -1;
    }
  };

  while (i < src.length) {
    if (src.startsWith("<!--", i)) {
      flushText(i);
      const close = src.indexOf("-->", i);
      const end = close === -1 ? src.length : close + 3;
      tokens.push({ text: src.slice(i, end), cls: "comment" });
      i = end;
      continue;
    }

    if (src[i] === "<") {
      flushText(i);
      const close = src.indexOf(">", i);
      const end = close === -1 ? src.length : close + 1;
      tokens.push(...tokenizeXmlTag(src.slice(i, end)));
      i = end;
      continue;
    }

    if (textStart < 0) textStart = i;
    i++;
  }

  flushText(src.length);
  return tokens;
}

function tokenizeXmlTag(tag: string): BodyToken[] {
  const tokens: BodyToken[] = [];
  let i = 0;

  // Opening punctuation: "<", "</", "<?", "<!".
  let open = "<";
  i = 1;
  if (tag[i] === "/" || tag[i] === "?" || tag[i] === "!") {
    open += tag[i];
    i++;
  }
  tokens.push({ text: open, cls: "punct" });

  // Tag name.
  const nameStart = i;
  while (i < tag.length && /[^\s/>?]/.test(tag[i])) i++;
  if (i > nameStart) tokens.push({ text: tag.slice(nameStart, i), cls: "tag" });

  // Attributes and the closing bracket.
  while (i < tag.length) {
    const c = tag[i];

    if (/\s/.test(c)) {
      const start = i;
      while (i < tag.length && /\s/.test(tag[i])) i++;
      tokens.push({ text: tag.slice(start, i), cls: "plain" });
      continue;
    }

    if (c === '"' || c === "'") {
      const quote = c;
      let end = i + 1;
      while (end < tag.length && tag[end] !== quote) end++;
      if (end < tag.length) end++;
      tokens.push({ text: tag.slice(i, end), cls: "string" });
      i = end;
      continue;
    }

    if (c === "=" || c === ">" || c === "/" || c === "?") {
      tokens.push({ text: c, cls: "punct" });
      i++;
      continue;
    }

    const start = i;
    while (i < tag.length && /[^\s=/>?"']/.test(tag[i])) i++;
    if (i === start) {
      // Defensive: never loop on an unexpected character.
      tokens.push({ text: tag[i], cls: "plain" });
      i++;
      continue;
    }
    tokens.push({ text: tag.slice(start, i), cls: "attr" });
  }

  return tokens;
}

/** Tokenizes according to the selected language; unknown languages stay plain. */
export function tokenizeBody(src: string, language: "json" | "xml" | "none"): BodyToken[] {
  if (language === "json") return tokenizeJson(src);
  if (language === "xml") return tokenizeXml(src);
  return [{ text: src, cls: "plain" }];
}
