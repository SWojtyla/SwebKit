import { useEffect, useMemo, useRef, useState } from "react";
import { EditorState, Compartment } from "@codemirror/state";
import { EditorView, keymap, lineNumbers, highlightActiveLine, highlightActiveLineGutter } from "@codemirror/view";
import { defaultKeymap } from "@codemirror/commands";
import { foldGutter, foldKeymap, codeFolding, bracketMatching } from "@codemirror/language";
import { search, searchKeymap, highlightSelectionMatches } from "@codemirror/search";
import { json } from "@codemirror/lang-json";
import { xml } from "@codemirror/lang-xml";
import { swebkitHighlighting } from "@/lib/codemirror-theme";
import {
  selectBodyLanguage,
  selectBodyRenderMode,
  HIGHLIGHT_MAX_BYTES,
  type BodyLanguage,
} from "@/lib/response-body";
import { tokenizeBody } from "@/lib/bodyHighlight";

interface ResponseBodyViewerProps {
  body: string;
  contentType: string | null;
  wrap: boolean;
}

function languageExtension(language: BodyLanguage) {
  if (language === "json") return json();
  if (language === "xml") return xml();
  return [];
}

/**
 * Small-body path: a plain `<pre>` with span-based highlighting sharing the same
 * `--cm-*` tokens as the editor path, so both look identical (DEC-3).
 */
function PreBody({ body, language, wrap }: { body: string; language: BodyLanguage; wrap: boolean }) {
  const tokens = useMemo(() => tokenizeBody(body, language), [body, language]);
  return (
    <pre
      className={`body-viewer min-w-0 w-full font-mono text-[0.8125rem] leading-[1.55] ${
        wrap ? "whitespace-pre-wrap break-words" : "overflow-x-auto whitespace-pre"
      }`}
    >
      {tokens.map((token, i) => (
        <span key={i} className={`tok-${token.cls}`}>
          {token.text}
        </span>
      ))}
    </pre>
  );
}

/** Read-only CodeMirror path: virtualized, foldable, searchable. */
function EditorBody({
  body,
  language,
  wrap,
}: {
  body: string;
  language: BodyLanguage;
  wrap: boolean;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewRef = useRef<EditorView | null>(null);
  const languageRef = useRef(new Compartment());
  const wrapRef = useRef(new Compartment());

  // Build once; document, language and wrapping are then updated in place so the
  // user's scroll position and any open search panel survive a re-render.
  useEffect(() => {
    if (!containerRef.current) return;
    const view = new EditorView({
      state: EditorState.create({
        doc: body,
        extensions: [
          lineNumbers(),
          codeFolding(),
          foldGutter(),
          highlightActiveLine(),
          highlightActiveLineGutter(),
          bracketMatching(),
          highlightSelectionMatches(),
          search({ top: false }),
          keymap.of([...defaultKeymap, ...searchKeymap, ...foldKeymap]),
          EditorState.readOnly.of(true),
          EditorView.editable.of(false),
          languageRef.current.of(languageExtension(language)),
          wrapRef.current.of(wrap ? EditorView.lineWrapping : []),
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    const view = viewRef.current;
    if (!view || view.state.doc.toString() === body) return;
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: body } });
  }, [body]);

  useEffect(() => {
    viewRef.current?.dispatch({
      effects: languageRef.current.reconfigure(languageExtension(language)),
    });
  }, [language]);

  useEffect(() => {
    viewRef.current?.dispatch({
      effects: wrapRef.current.reconfigure(wrap ? EditorView.lineWrapping : []),
    });
  }, [wrap]);

  return <div ref={containerRef} className="h-full min-h-0 w-full" />;
}

export function ResponseBodyViewer({ body, contentType, wrap }: ResponseBodyViewerProps) {
  const [forceHighlight, setForceHighlight] = useState(false);

  // A new response resets the user's per-response override.
  useEffect(() => {
    setForceHighlight(false);
  }, [body]);

  const detectedLanguage = useMemo(() => selectBodyLanguage(contentType, body), [contentType, body]);
  const mode = useMemo(() => selectBodyRenderMode(body.length, forceHighlight), [body.length, forceHighlight]);
  const language: BodyLanguage = mode === "codemirror-plain" ? "none" : detectedLanguage;

  return (
    <div className="flex h-full min-h-0 min-w-0 flex-col" data-testid="response-body">
      {mode === "codemirror-plain" && (
        <div
          className="mb-2 flex items-center gap-2 rounded border border-warning/30 bg-warning/10 px-2 py-1 text-xs"
          style={{ color: "var(--warning)" }}
          data-testid="response-body-large-notice"
        >
          <span className="flex-1">
            Highlighting disabled for responses over {Math.round(HIGHLIGHT_MAX_BYTES / 1024)} kB.
          </span>
          <button
            onClick={() => setForceHighlight(true)}
            className="rounded border px-1.5 py-0.5 hover:bg-accent"
            data-testid="response-body-force-highlight"
          >
            Highlight anyway
          </button>
        </div>
      )}

      <div className="min-h-0 min-w-0 flex-1 overflow-auto">
        {mode === "pre" ? (
          <PreBody body={body} language={language} wrap={wrap} />
        ) : (
          <EditorBody body={body} language={language} wrap={wrap} />
        )}
      </div>

      {/*
        CodeMirror only renders the visible viewport, so off-screen lines are not
        in the DOM. This mirror keeps the full text assertable by e2e tests and
        readable by assistive tech, matching the pattern BodyCodeEditor already
        uses for the request body.
      */}
      <div aria-hidden="true" className="sr-only" data-testid="response-body-text">
        {body}
      </div>
    </div>
  );
}
