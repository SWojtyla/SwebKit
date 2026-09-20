import { useEffect, useRef } from "react";
import { EditorState, Compartment } from "@codemirror/state";
import { defaultKeymap, history, historyKeymap, indentWithTab } from "@codemirror/commands";
import { json } from "@codemirror/lang-json";
import { xml } from "@codemirror/lang-xml";
import { bracketMatching, foldGutter, foldKeymap, codeFolding } from "@codemirror/language";
import { closeBrackets, closeBracketsKeymap } from "@codemirror/autocomplete";
import { EditorView, keymap, lineNumbers, highlightActiveLine } from "@codemirror/view";
import { swebkitHighlighting } from "@/lib/codemirror-theme";

function bodyLanguage(contentType: string | null | undefined, value: string) {
  const ct = (contentType ?? "").toLowerCase();
  if (ct.includes("json")) return json();
  if (ct.includes("xml")) return xml();
  // Content type unset/plain: sniff the payload so a JSON body still gets
  // highlighting when the user never touched the content-type field. Strip a
  // leading UTF-8 BOM first — common in .NET-published payloads.
  const withoutBom = value.codePointAt(0) === 0xfeff ? value.slice(1) : value;
  const trimmed = withoutBom.trim();
  if (trimmed.startsWith("{") || trimmed.startsWith("[")) return json();
  if (trimmed.startsWith("<")) return xml();
  return [];
}

interface Props {
  value: string;
  contentType: string | null;
  onChange: (value: string) => void;
  /** testid for the hidden mirror textarea (see below). */
  mirrorTestId?: string;
  /** testid for the CodeMirror container div. */
  containerTestId?: string;
}

/**
 * CodeMirror body editor for the message composer — the same setup as the API
 * Client's BodyCodeEditor, minus variable highlighting.
 *
 * The `aria-hidden` mirror textarea carries the full document: CodeMirror only
 * renders the visible viewport, so Playwright assertions and assistive tech
 * can't see off-screen lines without it. Filling the mirror also drives
 * `onChange`, which is how the e2e suite types into the editor
 * (`docs/pitfalls/react-frontend.md`).
 */
export function MessageBodyEditor({
  value,
  contentType,
  onChange,
  mirrorTestId = "composer-body",
  containerTestId = "composer-body-codemirror",
}: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewRef = useRef<EditorView | null>(null);
  const languageRef = useRef(new Compartment());
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
          bracketMatching(),
          closeBrackets(),
          history(),
          keymap.of([...closeBracketsKeymap, ...defaultKeymap, ...historyKeymap, ...foldKeymap, indentWithTab]),
          languageRef.current.of(bodyLanguage(contentType, value)),
          EditorView.updateListener.of((update) => {
            if (update.docChanged) onChangeRef.current(update.state.doc.toString());
          }),
          // Replaces CodeMirror's light-only defaultHighlightStyle — its palette is
          // effectively invisible against the dark theme background.
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
    // Mount-once: content-type changes reconfigure the language compartment so
    // the view keeps cursor, scroll and undo history.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    const view = viewRef.current;
    if (!view) return;
    view.dispatch({
      effects: languageRef.current.reconfigure(bodyLanguage(contentType, view.state.doc.toString())),
    });
  }, [contentType]);

  useEffect(() => {
    const view = viewRef.current;
    if (!view || view.state.doc.toString() === value) return;
    view.dispatch({
      changes: { from: 0, to: view.state.doc.length, insert: value },
    });
  }, [value]);

  return (
    <div
      className="relative flex min-h-40 flex-1 flex-col overflow-hidden rounded-md border bg-background"
      data-testid={containerTestId}
    >
      <div ref={containerRef} className="min-h-0 flex-1" />
      <textarea
        data-testid={mirrorTestId}
        aria-hidden="true"
        tabIndex={-1}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="absolute left-0 top-0 z-20 h-4 w-4 opacity-0"
      />
    </div>
  );
}
