function escapeHtml(s: string): string {
  return String(s)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function span(cls: string, text: string): string {
  return `<span class="yml-${cls}">${escapeHtml(text)}</span>`;
}

function findInlineCommentIdx(s: string): number {
  let inSingle = false;
  let inDouble = false;
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (c === "'" && !inDouble) inSingle = !inSingle;
    if (c === '"' && !inSingle) inDouble = !inDouble;
    if (c === "#" && !inSingle && !inDouble && i > 0 && /\s/.test(s[i - 1])) return i;
  }
  return -1;
}

function formatValue(val: string): string {
  let v = val.trimEnd();
  if (!v && v !== "0") return "";

  const commentIdx = findInlineCommentIdx(v);
  let comment = "";
  if (commentIdx > 0) {
    comment = span("comment", v.slice(commentIdx));
    v = v.slice(0, commentIdx).trimEnd();
  }
  if (!v && v !== "0") return comment;

  let result: string;
  if (/^["']/.test(v)) {
    result = span("string", v);
  } else if (/^[|>][-+]?\s*$/.test(v)) {
    result = span("scalar", v);
  } else if (/^[&*]/.test(v)) {
    result = span("anchor", v);
  } else if (/^(true|false|yes|no|on|off)$/i.test(v)) {
    result = span("bool", v);
  } else if (/^(null|~)$/i.test(v)) {
    result = span("null", v);
  } else if (/^-?(0x[0-9a-fA-F]+|0o[0-7]+|[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?)$/.test(v)) {
    result = span("number", v);
  } else {
    result = span("value", v);
  }

  return result + comment;
}

export function highlightYaml(text: string, preserveBlankLines = false): string {
  const lines = text.split("\n");
  return lines
    .map((line) => {
      if (!line.trim()) return preserveBlankLines ? "" : null;

      if (/^\s*#/.test(line)) return span("comment", line);
      if (/^(---|\.\.\.)\s*$/.test(line.trim())) return span("doc-marker", line);
      if (/^%/.test(line.trim())) return span("directive", line);

      const kvMatch = line.match(/^(\s*)([\w\-\.\/]+)(\s*:\s*)(.*)$/);
      if (kvMatch) {
        const [, indent, key, colon, rest] = kvMatch;
        let inlineComment = "";
        const commentIdx = findInlineCommentIdx(rest);
        if (commentIdx >= 0) {
          inlineComment = span("comment", rest.slice(commentIdx));
          const restTrimmed = rest.slice(0, commentIdx).trimEnd();
          return escapeHtml(indent ?? "") + span("key", key ?? "") + span("colon", colon ?? "") + formatValue(restTrimmed) + inlineComment;
        }
        return escapeHtml(indent ?? "") + span("key", key ?? "") + span("colon", colon ?? "") + formatValue(rest) + inlineComment;
      }

      const listMatch = line.match(/^(\s*)-\s*(.*)$/);
      if (listMatch) {
        const [, listIndent, listRest] = listMatch;
        const innerKv = listRest.match(/^([\w\-\.\/]+)(\s*:\s*)(.*)$/);
        if (innerKv) {
          const [, innerKey, innerColon, innerRest] = innerKv;
          return (
            escapeHtml(listIndent ?? "") +
            span("dash", "- ") +
            span("key", innerKey ?? "") +
            span("colon", innerColon ?? "") +
            formatValue(innerRest ?? "")
          );
        }
        return escapeHtml(listIndent ?? "") + span("dash", "- ") + formatValue(listRest);
      }

      return escapeHtml(line);
    })
    .filter((l) => l !== null)
    .join("\n");
}
