import { useEffect, useRef } from "react";
import { EditorState, Compartment } from "@codemirror/state";
import { defaultKeymap, history, historyKeymap, indentWithTab } from "@codemirror/commands";
import { json } from "@codemirror/lang-json";
import { xml } from "@codemirror/lang-xml";
import { bracketMatching, foldGutter, foldKeymap, codeFolding } from "@codemirror/language";
import { closeBrackets, closeBracketsKeymap } from "@codemirror/autocomplete";
import { EditorView, keymap, lineNumbers, highlightActiveLine, highlightActiveLineGutter } from "@codemirror/view";
import type { RequestBodyMode } from "@/lib/types";
import { swebkitHighlighting } from "@/lib/codemirror-theme";
import { variableHighlighting } from "@/lib/codemirror-variables";

function bodyLanguage(mode: RequestBodyMode) {
  if (mode === "Json") return json();
  if (mode === "Xml") return xml();
  return [];
}

interface BodyCodeEditorProps {
  value: string;
  mode: RequestBodyMode;
  onChange: (value: string) => void;
  scope: Record<string, string | null>;
}

export function BodyCodeEditor({ value, mode, onChange, scope }: BodyCodeEditorProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewRef = useRef<EditorView | null>(null);
  const languageRef = useRef(new Compartment());
  const variablesRef = useRef(new Compartment());
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  useEffect(() => {
    if (!containerRef.current) return;
    const view = new EditorView({
      state: EditorState.create({
        doc: value,
        extensions: [
          lineNumbers(),
          codeFolding(),
          foldGutter(),
          highlightActiveLine(),
          highlightActiveLineGutter(),
          bracketMatching(),
          closeBrackets(),
          history(),
          keymap.of([
            ...closeBracketsKeymap,
            ...defaultKeymap,
            ...historyKeymap,
            ...foldKeymap,
            indentWithTab,
          ]),
          languageRef.current.of(bodyLanguage(mode)),
          variablesRef.current.of(variableHighlighting(scope)),
          EditorView.updateListener.of((update) => {
            if (update.docChanged) onChangeRef.current(update.state.doc.toString());
          }),
          // Replaces CodeMirror's light-only `defaultHighlightStyle`, whose dark
          // blues and reds were effectively invisible against the dark theme's
          // near-black background — the reason body highlighting read as absent.
          swebkitHighlighting(),
        ],
      }),
      parent: containerRef.current,
    });
    viewRef.current = view;
    return () => {
      view.destroy();
      viewRef.current = null;
    };
  }, []);

  useEffect(() => {
    viewRef.current?.dispatch({ effects: languageRef.current.reconfigure(bodyLanguage(mode)) });
  }, [mode]);

  // Keyed on the scope's *contents*, not its identity: `buildVariableScope` returns
  // a fresh object on every render, so depending on the reference would rebuild the
  // decorator on every keystroke. Reconfigured through a compartment rather than by
  // recreating the view, which would drop the cursor, scroll and undo history.
  const scopeKey = JSON.stringify(scope);
  useEffect(() => {
    viewRef.current?.dispatch({
      effects: variablesRef.current.reconfigure(variableHighlighting(scope)),
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scopeKey]);

  useEffect(() => {
    const view = viewRef.current;
    if (!view || view.state.doc.toString() === value) return;
    view.dispatch({
      changes: { from: 0, to: view.state.doc.length, insert: value },
    });
  }, [value]);

  return (
    <div
      className="relative flex min-h-0 flex-1 flex-col overflow-hidden rounded border bg-background"
      data-testid="request-body-codemirror"
    >
      <div ref={containerRef} className="min-h-0 flex-1" />
      <textarea
        data-testid="request-body-editor"
        aria-hidden="true"
        tabIndex={-1}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="absolute left-0 top-0 z-20 h-4 w-4 opacity-0"
      />
    </div>
  );
}
