/// Content-sniffing XML prettifier — sibling to `pretty-json.ts`.
///
/// Storage file previews are server-capped (~512 KB) but routinely arrive as a single
/// minified line, which renders as one horizontally-scrolling wall of text. This is a
/// single streaming pass over the string — no DOM parse — so it stays O(n) even on a
/// capped preview, tolerates input truncated mid-tag, and never throws: anything it
/// can't make sense of is emitted verbatim. Attribute whitespace is normalised outside
/// quotes only, so values keep their bytes.
///
/// Returns the indented form, or `null` when the content does not look like XML —
/// the caller then shows no Pretty toggle, matching `tryPrettifyJson`.

/** U+FEFF, the UTF-8 BOM .NET producers emit routinely. */
const BOM = 0xfeff;

/** Beyond this depth, indentation stops growing — pathological nesting then costs
 * constant space instead of O(tokens × depth) output. */
const MAX_INDENT_DEPTH = 32;

/** Bail-out bound on the formatted result — defends against adversarial input where
 * per-token newlines inflate the output far beyond the (already capped) input. */
const MAX_OUTPUT_LENGTH = 16 * 1024 * 1024;

const DEFAULT_MAX_INPUT = 2 * 1024 * 1024;

function stripPreamble(content: string): string {
    const withoutBom =
        content.charCodeAt(0) === BOM ? content.slice(1) : content;
    return withoutBom.trim();
}

/** Index just past the next `>` that is outside quoted attribute values. */
function tagEnd(src: string, start: number): number {
    let quote = 0;
    for (let i = start + 1; i < src.length; i++) {
        const c = src.charCodeAt(i);
        if (quote !== 0) {
            if (c === quote) quote = 0;
        } else if (c === 34 /* " */ || c === 39 /* ' */) {
            quote = c;
        } else if (c === 62 /* > */) {
            return i + 1;
        }
    }
    return src.length;
}

/** Collapses whitespace runs inside a tag, but never inside quoted attribute values. */
function normalizeTag(tag: string): string {
    const out: string[] = [];
    let quote = 0;
    let pendingSpace = false;
    for (let i = 0; i < tag.length; i++) {
        const c = tag.charCodeAt(i);
        const isSpace = c === 32 || c === 9 || c === 10 || c === 13;
        if (quote !== 0) {
            out.push(tag[i]);
            if (c === quote) quote = 0;
            continue;
        }
        if (c === 34 || c === 39) quote = c;
        if (isSpace) {
            pendingSpace = out.length > 0;
            continue;
        }
        if (pendingSpace) {
            out.push(" ");
            pendingSpace = false;
        }
        out.push(tag[i]);
    }
    return out.join("");
}

export function tryPrettifyXml(
    content: string,
    maxInputBytes: number = DEFAULT_MAX_INPUT,
): string | null {
    const src = stripPreamble(content);
    if (
        src.length === 0 ||
        src.length > maxInputBytes ||
        src[0] !== "<" ||
        !src.includes(">")
    )
        return null;

    const out: string[] = [];
    let outLength = 0;
    let depth = 0;
    /** What the last emitted unit was — drives same-line decisions for
     * `<a>text</a>` and empty `<a></a>` elements. */
    let last: "none" | "open" | "text" | "other" = "none";

    const line = (s: string): boolean => {
        if (out.length > 0) {
            out.push("\n");
            outLength++;
        }
        const indent = "  ".repeat(Math.min(depth, MAX_INDENT_DEPTH));
        out.push(indent, s);
        outLength += indent.length + s.length;
        return outLength <= MAX_OUTPUT_LENGTH;
    };

    const inline = (s: string): boolean => {
        out.push(s);
        outLength += s.length;
        return outLength <= MAX_OUTPUT_LENGTH;
    };

    let i = 0;
    while (i < src.length) {
        if (src[i] !== "<") {
            const next = src.indexOf("<", i);
            const end = next === -1 ? src.length : next;
            const text = src.slice(i, end);
            i = end;
            if (text.trim().length === 0) continue; // inter-element whitespace
            // Text after an open tag stays on that line (`<a>v</a>`); anything
            // else gets its own line at the current depth.
            if (!(last === "open" ? inline(text.trim()) : line(text)))
                return null;
            last = "text";
            continue;
        }

        let end: number;
        let kind: "open" | "close" | "self" | "misc";
        if (src.startsWith("<!--", i)) {
            const close = src.indexOf("-->", i + 4);
            end = close === -1 ? src.length : close + 3;
            kind = "misc";
        } else if (src.startsWith("<![CDATA[", i)) {
            const close = src.indexOf("]]>", i + 9);
            end = close === -1 ? src.length : close + 3;
            kind = "misc";
        } else if (src.startsWith("<?", i)) {
            const close = src.indexOf("?>", i + 2);
            end = close === -1 ? tagEnd(src, i) : close + 2;
            kind = "misc";
        } else if (src.startsWith("<!", i)) {
            end = tagEnd(src, i);
            kind = "misc";
        } else if (src.startsWith("</", i)) {
            end = tagEnd(src, i);
            kind = "close";
        } else {
            end = tagEnd(src, i);
            kind = end > 1 && src[end - 2] === "/" ? "self" : "open";
        }

        const raw = src.slice(i, end);
        i = end;

        if (kind === "misc") {
            if (!line(raw)) return null;
            last = "other";
            continue;
        }

        const tag = normalizeTag(raw);
        if (kind === "open") {
            if (!line(tag)) return null;
            depth++;
            last = "open";
        } else if (kind === "self") {
            if (!line(tag)) return null;
            last = "other";
        } else {
            depth = Math.max(0, depth - 1);
            // Close stays on the same line after an open tag or inline text
            // (`<a></a>`, `<a>v</a>`); otherwise it starts a fresh line.
            if (!(last === "open" || last === "text" ? inline(tag) : line(tag)))
                return null;
            last = "other";
        }
    }

    return out.join("");
}
