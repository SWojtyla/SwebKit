/// Shared CodeMirror theme and highlight style for the API Client editors.
///
/// Every colour is a `var(--cm-*)` custom property defined per theme class in
/// `styles/globals.css`. That is deliberate: SwebKit has three themes applied as
/// a class on `<html>`, and a JS-valued HighlightStyle is baked into the
/// EditorState at creation — switching themes would need a Compartment
/// reconfigure or a full view rebuild, losing cursor/scroll/undo state. CSS
/// custom properties are re-resolved by the browser for free.
///
/// See docs/features/active/api-client-ux-overhaul/decisions.md DEC-1.

import { HighlightStyle, syntaxHighlighting } from "@codemirror/language";
import { EditorView } from "@codemirror/view";
import { tags as t } from "@lezer/highlight";

export const swebkitHighlightStyle = HighlightStyle.define([
  // JSON object keys arrive as propertyName; XML/HTML attribute values as string.
  { tag: t.propertyName, color: "var(--cm-key)", fontWeight: "600" },
  { tag: [t.string, t.special(t.string)], color: "var(--cm-string)" },
  { tag: [t.number, t.integer, t.float], color: "var(--cm-number)" },
  { tag: [t.bool, t.keyword, t.atom], color: "var(--cm-bool)", fontWeight: "600" },
  { tag: t.null, color: "var(--cm-null)", fontStyle: "italic" },
  { tag: [t.punctuation, t.separator, t.bracket], color: "var(--cm-punct)" },
  { tag: [t.comment, t.lineComment, t.blockComment], color: "var(--cm-comment)", fontStyle: "italic" },
  { tag: [t.tagName, t.angleBracket], color: "var(--cm-tag)", fontWeight: "600" },
  { tag: [t.attributeName], color: "var(--cm-attr)" },
  { tag: t.invalid, color: "var(--cm-invalid)" },
]);

/**
 * Structural theme shared by both editors. Backgrounds stay transparent so the
 * surrounding pane's `bg-card` / `bg-background` shows through and the editor
 * never fights the theme.
 */
export const swebkitEditorTheme = EditorView.theme({
  "&": {
    height: "100%",
    fontSize: "0.8125rem",
    backgroundColor: "transparent",
    color: "var(--foreground)",
  },
  "&.cm-focused": { outline: "none" },
  ".cm-scroller": {
    overflow: "auto",
    fontFamily: "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace",
    lineHeight: "1.55",
  },
  ".cm-content": { caretColor: "var(--foreground)" },
  ".cm-gutters": {
    backgroundColor: "transparent",
    border: "none",
    color: "var(--cm-gutter)",
  },
  ".cm-lineNumbers .cm-gutterElement": { padding: "0 0.5rem 0 0.75rem" },
  ".cm-activeLine": { backgroundColor: "var(--cm-active-line)" },
  ".cm-activeLineGutter": { backgroundColor: "var(--cm-active-line)" },
  ".cm-foldGutter .cm-gutterElement": { cursor: "pointer" },
  ".cm-selectionBackground, ::selection": { backgroundColor: "var(--primary-glow)" },
  "&.cm-focused .cm-selectionBackground": { backgroundColor: "var(--primary-glow)" },
  ".cm-cursor, .cm-dropCursor": { borderLeftColor: "var(--foreground)" },
  ".cm-matchingBracket, .cm-nonmatchingBracket": {
    backgroundColor: "var(--accent)",
    outline: "1px solid var(--border)",
  },
  // Search panel — themed so Ctrl+F does not drop an unstyled browser-grey bar in.
  ".cm-panels": { backgroundColor: "var(--card)", color: "var(--foreground)" },
  ".cm-panels.cm-panels-bottom": { borderTop: "1px solid var(--border)" },
  ".cm-searchMatch": {
    backgroundColor: "color-mix(in oklch, var(--warning) 30%, transparent)",
  },
  ".cm-searchMatch.cm-searchMatch-selected": {
    backgroundColor: "color-mix(in oklch, var(--primary) 40%, transparent)",
  },
  ".cm-panel input, .cm-panel button": {
    backgroundColor: "var(--background)",
    color: "var(--foreground)",
    border: "1px solid var(--border)",
    borderRadius: "0.25rem",
    padding: "0.125rem 0.375rem",
  },
});

/** Extensions applying SwebKit's syntax colours. Use in every API Client editor. */
export function swebkitHighlighting() {
  return [syntaxHighlighting(swebkitHighlightStyle), swebkitEditorTheme];
}
