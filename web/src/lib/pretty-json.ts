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
  const withoutBom = content.charCodeAt(0) === BOM ? content.slice(1) : content;
  return withoutBom.trim();
}

/**
 * Returns the indented form of `content`, or `null` when it is not JSON.
 *
 * Only object and array roots qualify. `JSON.parse` also accepts bare scalars, so
 * without that guard a plain text file containing `42` would be offered a Prettify
 * toggle that does nothing. Truncated content (the server caps large blobs) fails to
 * parse and returns `null` rather than throwing.
 */
export function tryPrettifyJson(content: string): string | null {
  const trimmed = stripPreamble(content);
  if (!trimmed.startsWith("{") && !trimmed.startsWith("[")) return null;

  try {
    return JSON.stringify(JSON.parse(trimmed), null, 2);
  } catch {
    return null;
  }
}
