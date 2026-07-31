/// Pure decisions about how a response body should be rendered: which language
/// to highlight it as, and how much editor machinery to spend on it.

export type BodyLanguage = "json" | "xml" | "none";

export type BodyRenderMode =
  /** Plain <pre> with span-based highlighting. Cheap, for small payloads. */
  | "pre"
  /** Full CodeMirror: virtualized, folding, search, language parsing. */
  | "codemirror"
  /** CodeMirror without a language extension — still virtualized, no parse cost. */
  | "codemirror-plain";

/**
 * Below this, constructing an EditorView costs more than it returns: most
 * interactive debugging responses are small and should feel instant.
 */
export const PRE_MAX_BYTES = 2 * 1024;

/**
 * Above this, the language parse dominates. CodeMirror still virtualizes, so the
 * body stays scrollable — only the colours are dropped, and the UI says so.
 */
export const HIGHLIGHT_MAX_BYTES = 512 * 1024;

/**
 * Picks the highlight language from the declared content type, falling back to
 * sniffing the body's first non-whitespace character.
 *
 * The sniff matters because plenty of APIs return JSON as `text/plain` or with
 * no content type at all.
 */
export function selectBodyLanguage(contentType: string | null, body: string): BodyLanguage {
  const trimmed = body.trimStart();
  if (trimmed.length === 0) return "none";

  const ct = contentType?.toLowerCase() ?? "";

  // Binary responses are hex-encoded by the sidecar — highlighting them is noise.
  if (ct.includes("octet-stream")) return "none";

  // Matches application/json, application/problem+json, text/json, …
  if (ct.includes("json")) return "json";
  if (ct.includes("xml") || ct.includes("html")) return "xml";

  if (ct.length === 0 || ct.includes("text/plain")) {
    const first = trimmed[0];
    if (first === "{" || first === "[") return "json";
    if (first === "<") return "xml";
  }

  return "none";
}

/**
 * Chooses the rendering strategy for a body of `length` characters.
 *
 * `forceHighlight` is the user's explicit override from the large-response
 * notice — they asked for it, so they get it.
 */
export function selectBodyRenderMode(length: number, forceHighlight = false): BodyRenderMode {
  if (length < PRE_MAX_BYTES) return "pre";
  if (forceHighlight) return "codemirror";
  if (length < HIGHLIGHT_MAX_BYTES) return "codemirror";
  return "codemirror-plain";
}

/** File extension for the response download action. */
export function downloadExtension(language: BodyLanguage): string {
  if (language === "json") return "json";
  if (language === "xml") return "xml";
  return "txt";
}
