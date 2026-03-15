// Lightweight YAML syntax highlighter — no external dependencies.
// Used by the AKS YAML viewer panel via Blazor JSInterop.

window.yamlHighlight = {
    /**
     * Returns syntax-highlighted HTML for the given YAML text.
     * Call from C# with: JS.InvokeAsync<string>("yamlHighlight.highlight", yaml)
     * Render the result as a MarkupString so Blazor owns the DOM content.
     * @param {string} yaml - Raw YAML text.
     * @returns {string} HTML string with <span> tokens.
     */
    highlight: function (yaml) {
        return yamlToHtml(yaml || '');
    }
};

function escHtml(s) {
    return String(s)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

function span(cls, text) {
    return '<span class="yml-' + cls + '">' + escHtml(text) + '</span>';
}

function formatValue(val) {
    if (!val && val !== 0) return '';
    var v = val.trim();
    if (!v) return '';

    // Inline comment at end of value
    var commentIdx = findInlineCommentIdx(v);
    var comment = '';
    if (commentIdx > 0) {
        comment = span('comment', v.slice(commentIdx));
        v = v.slice(0, commentIdx).trimEnd();
    }

    var result;

    // Quoted strings
    if (/^["']/.test(v)) {
        result = span('string', v);
    }
    // Block scalars (| or >)
    else if (/^[|>][-+]?\s*$/.test(v)) {
        result = span('scalar', v);
    }
    // Anchors & aliases
    else if (/^[&*]/.test(v)) {
        result = span('anchor', v);
    }
    // Boolean
    else if (/^(true|false|yes|no|on|off)$/i.test(v)) {
        result = span('bool', v);
    }
    // Null
    else if (/^(null|~)$/i.test(v)) {
        result = span('null', v);
    }
    // Number (int, float, hex, octal, scientific)
    else if (/^-?(0x[0-9a-fA-F]+|0o[0-7]+|[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?)$/.test(v)) {
        result = span('number', v);
    }
    else {
        result = span('value', v);
    }

    return result + comment;
}

// Find an unquoted # that starts an inline comment.
function findInlineCommentIdx(s) {
    var inSingle = false, inDouble = false;
    for (var i = 0; i < s.length; i++) {
        var c = s[i];
        if (c === "'" && !inDouble) inSingle = !inSingle;
        if (c === '"' && !inSingle) inDouble = !inDouble;
        if (c === '#' && !inSingle && !inDouble && i > 0 && /\s/.test(s[i - 1])) return i;
    }
    return -1;
}

function yamlToHtml(text) {
    var lines = text.split('\n');
    return lines.map(function (line) {
        // Blank line
        if (!line.trim()) return '';

        // Full-line comment
        if (/^\s*#/.test(line)) return span('comment', line);

        // Document / directives markers
        if (/^(---|\.\.\.)\s*$/.test(line.trim())) return span('doc-marker', line);

        // YAML directive (e.g., %YAML 1.2)
        if (/^%/.test(line.trim())) return span('directive', line);

        // Key-value: indent + key + colon + optional value
        var kvMatch = line.match(/^(\s*)([\w\-\.\/]+)(\s*:\s*)(.*)$/);
        if (kvMatch) {
            var indent = kvMatch[1];
            var key = kvMatch[2];
            var colon = kvMatch[3];
            var rest = kvMatch[4];

            // Inline comment on key-only line (e.g., "key: # comment")
            var inlineComment = '';
            var commentIdx = findInlineCommentIdx(rest);
            if (commentIdx >= 0) {
                inlineComment = span('comment', rest.slice(commentIdx));
                rest = rest.slice(0, commentIdx).trimEnd();
            }

            return escHtml(indent) +
                span('key', key) +
                span('colon', colon) +
                formatValue(rest) +
                inlineComment;
        }

        // List item: indent + dash + optional value
        var listMatch = line.match(/^(\s*)-\s*(.*)$/);
        if (listMatch) {
            var listIndent = listMatch[1];
            var listRest = listMatch[2];

            // The rest could itself be a key-value
            var innerKv = listRest.match(/^([\w\-\.\/]+)(\s*:\s*)(.*)$/);
            if (innerKv) {
                return escHtml(listIndent) +
                    span('dash', '- ') +
                    span('key', innerKv[1]) +
                    span('colon', innerKv[2]) +
                    formatValue(innerKv[3]);
            }
            return escHtml(listIndent) + span('dash', '- ') + formatValue(listRest);
        }

        // Fallback
        return escHtml(line);
    }).join('\n');
}
