/// Content-sniffing JSON prettifier.
///
/// Deliberately ignores the blob's name and Content-Type: storage blobs are routinely
/// JSON payloads stored as `.txt`/`.log` or served as `application/octet-stream`, so
/// extension-based detection would refuse to format exactly the payloads that most
/// need formatting.

/** U+FEFF, the UTF-8 BOM .NET producers emit routinely. */
const BOM = 0xfeff;

/** Strips a leading BOM and surrounding whitespace, which `JSON.parse` rejects. */
function stripPreamble(content: string): string {
    const withoutBom =
        content.charCodeAt(0) === BOM ? content.slice(1) : content;
    return withoutBom.trim();
}

/**
 * Returns the indented form of `content`, or `null` when it is not JSON.
 *
 * Only object and array roots qualify. `JSON.parse` also accepts bare scalars, so
 * without that guard a plain text file containing `42` would be offered a Prettify
 * toggle that does nothing. Truncated content (the server caps large blobs) fails to
 * parse and returns `null` rather than throwing.
 *
 * String roots get unwrapped: some producers store a JSON payload inside a JSON
 * string (double-encoded — a log blob holding `"{\"a\":1}"`, escapes and all).
 * Unwrapping the string layers and re-parsing shows the payload, not the envelope.
 * String values *inside* an object are untouched — only whole-document wrapping
 * is removed.
 */
export function tryPrettifyJson(content: string): string | null {
    const trimmed = stripPreamble(content);
    if (
        !trimmed.startsWith("{") &&
        !trimmed.startsWith("[") &&
        !trimmed.startsWith('"')
    )
        return null;

    try {
        let parsed: unknown = JSON.parse(trimmed);
        // Depth cap: each layer is smaller than the last in practice, but don't trust
        // that — a pathological input could ping-pong forever otherwise.
        for (let depth = 0; typeof parsed === "string" && depth < 4; depth++) {
            const inner = stripPreamble(parsed);
            // An inner layer may itself be a quoted string (triple-encoded), so
            // `"` qualifies as a re-parseable start too.
            if (
                !inner.startsWith("{") &&
                !inner.startsWith("[") &&
                !inner.startsWith('"')
            )
                return null;
            parsed = JSON.parse(inner);
        }
        if (typeof parsed !== "object" || parsed === null) return null;
        return JSON.stringify(parsed, null, 2);
    } catch {
        return null;
    }
}
