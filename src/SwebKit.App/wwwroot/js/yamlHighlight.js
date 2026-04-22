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
  },

  /**
   * Initialises the YAML editor overlay.
   * Sets the initial value, wires scroll-sync, live highlighting, and Tab-to-indent.
   * JS fully owns the textarea value from this point — Blazor does NOT re-render it on input.
   * @param {HTMLTextAreaElement} textareaEl
   * @param {HTMLElement} preEl
   * @param {string} initialValue - The YAML text to pre-populate.
   */
  initEditor: function (textareaEl, preEl, initialValue) {
    if (!textareaEl || !preEl) return;

    // Seed both textarea and pre with the initial value from C#.
    textareaEl.value = initialValue || '';
    preEl.innerHTML = yamlToHtml(textareaEl.value);

    function updateHighlight() {
      preEl.innerHTML = yamlToHtml(textareaEl.value || '');
      preEl.scrollTop = textareaEl.scrollTop;
      preEl.scrollLeft = textareaEl.scrollLeft;
    }

    // Scroll sync
    textareaEl.addEventListener('scroll', function () {
      preEl.scrollTop = textareaEl.scrollTop;
      preEl.scrollLeft = textareaEl.scrollLeft;
    });

    // Live highlighting — JS handles this; Blazor does not interfere on every keystroke.
    textareaEl.addEventListener('input', updateHighlight);

    // Tab inserts two spaces instead of moving focus.
    textareaEl.addEventListener('keydown', function (e) {
      if (e.key === 'Tab') {
        e.preventDefault();
        var start = textareaEl.selectionStart;
        var end = textareaEl.selectionEnd;
        var v = textareaEl.value;
        textareaEl.value = v.substring(0, start) + '  ' + v.substring(end);
        textareaEl.selectionStart = textareaEl.selectionEnd = start + 2;
        updateHighlight();
      }
    });
  },

  /**
   * Returns the current value of the editor textarea.
   * C# calls this on save instead of reading from a Blazor-bound field.
   * @param {HTMLTextAreaElement} textareaEl
   * @returns {string}
   */
  getEditorValue: function (textareaEl) {
    return textareaEl ? textareaEl.value : '';
  },

  /**
   * Programmatically sets a new value in the editor (e.g. after replace-all).
   * Updates both the textarea and the highlighted pre.
   * @param {HTMLTextAreaElement} textareaEl
   * @param {HTMLElement} preEl
   * @param {string} value
   */
  setEditorValue: function (textareaEl, preEl, value) {
    if (!textareaEl) return;
    textareaEl.value = value || '';
    if (preEl) preEl.innerHTML = yamlToHtml(textareaEl.value);
  },

  /**
   * Searches for query text inside a pre element by walking text nodes,
   * wrapping matches with <mark class="yml-search-match"> elements.
   * Returns the total count of matches found.
   * @param {HTMLElement} preEl  - The <pre> to search inside.
   * @param {string}      query  - The search string (case-insensitive).
   * @returns {number} Number of matches highlighted.
   */
  searchInPre: function (preEl, query) {
    yamlClearMarks(preEl);
    if (!preEl || !query || !query.trim()) return 0;

    var count = 0;
    var lower = query.toLowerCase();

    function walkNode(node) {
      if (node.nodeType === Node.TEXT_NODE) {
        var text = node.textContent || '';
        var lowerText = text.toLowerCase();
        var idx = lowerText.indexOf(lower);
        if (idx === -1) return;

        var frag = document.createDocumentFragment();
        var remaining = text;
        var searchLower = lowerText;
        var offset = 0;

        while (true) {
          var pos = searchLower.indexOf(lower, offset);
          if (pos === -1) {
            frag.appendChild(document.createTextNode(remaining.slice(offset)));
            break;
          }
          if (pos > offset) {
            frag.appendChild(
              document.createTextNode(remaining.slice(offset, pos)),
            );
          }
          var mark = document.createElement('mark');
          mark.className = 'yml-search-match';
          mark.textContent = remaining.slice(pos, pos + lower.length);
          frag.appendChild(mark);
          count++;
          offset = pos + lower.length;
        }

        node.parentNode.replaceChild(frag, node);
      } else if (
        node.nodeType === Node.ELEMENT_NODE &&
        node.tagName !== 'MARK'
      ) {
        // Clone children list since replaceChild can mutate childNodes during iteration
        var children = Array.from(node.childNodes);
        children.forEach(walkNode);
      }
    }

    walkNode(preEl);

    // Scroll first match into view
    var first = preEl.querySelector('.yml-search-match');
    if (first) first.scrollIntoView({ block: 'nearest' });

    return count;
  },

  /** Remove all <mark class="yml-search-match"> elements from the given container. */
  clearSearch: function (preEl) {
    yamlClearMarks(preEl);
  },
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
  else if (
    /^-?(0x[0-9a-fA-F]+|0o[0-7]+|[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?)$/.test(v)
  ) {
    result = span('number', v);
  } else {
    result = span('value', v);
  }

  return result + comment;
}

// Find an unquoted # that starts an inline comment.
function findInlineCommentIdx(s) {
  var inSingle = false,
    inDouble = false;
  for (var i = 0; i < s.length; i++) {
    var c = s[i];
    if (c === "'" && !inDouble) inSingle = !inSingle;
    if (c === '"' && !inSingle) inDouble = !inDouble;
    if (c === '#' && !inSingle && !inDouble && i > 0 && /\s/.test(s[i - 1]))
      return i;
  }
  return -1;
}

function yamlToHtml(text) {
  var lines = text.split('\n');
  return lines
    .map(function (line) {
      // Blank line — skip entirely (suppressed in viewer)
      if (!line.trim()) return null;

      // Full-line comment
      if (/^\s*#/.test(line)) return span('comment', line);

      // Document / directives markers
      if (/^(---|\.\.\.)\s*$/.test(line.trim()))
        return span('doc-marker', line);

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

        return (
          escHtml(indent) +
          span('key', key) +
          span('colon', colon) +
          formatValue(rest) +
          inlineComment
        );
      }

      // List item: indent + dash + optional value
      var listMatch = line.match(/^(\s*)-\s*(.*)$/);
      if (listMatch) {
        var listIndent = listMatch[1];
        var listRest = listMatch[2];

        // The rest could itself be a key-value
        var innerKv = listRest.match(/^([\w\-\.\/]+)(\s*:\s*)(.*)$/);
        if (innerKv) {
          return (
            escHtml(listIndent) +
            span('dash', '- ') +
            span('key', innerKv[1]) +
            span('colon', innerKv[2]) +
            formatValue(innerKv[3])
          );
        }
        return escHtml(listIndent) + span('dash', '- ') + formatValue(listRest);
      }

      // Fallback
      return escHtml(line);
    })
    .filter(function (l) {
      return l !== null;
    })
    .join('\n');
}

/** Unwraps all <mark class="yml-search-match"> elements, restoring plain text nodes. */
function yamlClearMarks(container) {
  if (!container) return;
  var marks = container.querySelectorAll('mark.yml-search-match');
  marks.forEach(function (mark) {
    var parent = mark.parentNode;
    if (!parent) return;
    parent.replaceChild(document.createTextNode(mark.textContent || ''), mark);
    parent.normalize();
  });
}
