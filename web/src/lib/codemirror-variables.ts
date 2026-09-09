/// Colours `{{variable}}` tokens inside the CodeMirror request-body editor and
/// explains each one on hover.
///
/// Before this, the body editor had no variable awareness whatsoever: a token was
/// coloured by the JSON grammar as an ordinary *string*, so `"{{AUTH_SP}}"` looked
/// identical whether the variable existed or not. Since
/// `VariableSubstitutionService.Substitute` leaves an unknown token as its literal
/// text, an undefined variable was sent to the server verbatim as `{{AUTH_SP}}` and
/// came back a 400 with nothing in the UI having hinted at the cause.
///
/// The classification is delegated to `variableHighlight.ts`, the same module the
/// single-line `VariableInput` overlay uses, so the URL bar and the body can never
/// disagree about whether a name resolves.
///
/// Colours come from the `.var-tok-*` classes in `globals.css` rather than a
/// `HighlightStyle`. A `HighlightStyle` is baked into the `EditorState` at creation,
/// and the app has several `<html>`-class themes — see the CodeMirror section of
/// `docs/pitfalls/react-frontend.md`.

import { Decoration, EditorView, MatchDecorator, ViewPlugin, hoverTooltip } from "@codemirror/view";
import type { DecorationSet, ViewUpdate } from "@codemirror/view";
import type { Extension } from "@codemirror/state";
import { VARIABLE_TOKEN_SOURCE, variableHoverAt, variableMarkClass } from "./variableHighlight";

export type VariableScope = Record<string, string | null>;

/// Keyed by the class `variableMarkClass` returns, so the decoration and the
/// unit-tested classifier cannot drift apart.
const MARKS: Record<string, Decoration> = {
  "var-tok-resolved": Decoration.mark({ class: "var-tok-resolved" }),
  "var-tok-deferred": Decoration.mark({ class: "var-tok-deferred" }),
  "var-tok-unresolved": Decoration.mark({ class: "var-tok-unresolved" }),
};

const tooltipTheme = EditorView.theme({
  ".cm-tooltip.cm-tooltip-hover": {
    backgroundColor: "var(--card)",
    color: "var(--foreground)",
    border: "1px solid var(--border)",
    borderRadius: "4px",
  },
  ".cm-variable-tooltip": {
    padding: "2px 6px",
    fontSize: "12px",
    whiteSpace: "pre",
  },
});

/**
 * Builds the extension for one variable scope.
 *
 * The scope is captured, so a changed scope needs a fresh extension swapped in
 * through a `Compartment` — never a rebuilt `EditorView`, which would discard the
 * cursor, scroll position and undo history mid-edit.
 */
export function variableHighlighting(scope: VariableScope): Extension {
  const decorator = new MatchDecorator({
    // A fresh `RegExp`: `MatchDecorator` drives `lastIndex` itself, so sharing an
    // instance with `variableHighlight.ts` would have the two corrupt each other.
    regexp: new RegExp(VARIABLE_TOKEN_SOURCE, "g"),
    decoration: (match) => {
      const cls = variableMarkClass(match[1], scope);
      return cls === null ? null : MARKS[cls];
    },
  });

  const plugin = ViewPlugin.fromClass(
    class {
      decorations: DecorationSet;

      constructor(view: EditorView) {
        this.decorations = decorator.createDeco(view);
      }

      update(update: ViewUpdate) {
        if (update.docChanged || update.viewportChanged) {
          this.decorations = decorator.updateDeco(update, this.decorations);
        }
      }
    },
    { decorations: (v) => v.decorations },
  );

  const tooltip = hoverTooltip((view, pos) => {
    const line = view.state.doc.lineAt(pos);
    const hit = variableHoverAt(line.text, pos - line.from, scope);
    if (!hit) return null;
    return {
      pos: line.from + hit.from,
      end: line.from + hit.to,
      above: true,
      create: () => {
        const dom = document.createElement("div");
        dom.className = "cm-variable-tooltip";
        dom.textContent = hit.text;
        return { dom };
      },
    };
  });

  return [plugin, tooltip, tooltipTheme];
}
