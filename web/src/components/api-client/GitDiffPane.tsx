import { useEffect, useMemo, useRef, useState } from "react";
import { X, Columns2, Rows2 } from "lucide-react";
import { EditorState } from "@codemirror/state";
import { EditorView, lineNumbers } from "@codemirror/view";
import { json } from "@codemirror/lang-json";
import { xml } from "@codemirror/lang-xml";
import { swebkitHighlighting } from "@/lib/codemirror-theme";
import { selectBodyLanguage, HIGHLIGHT_MAX_BYTES } from "@/lib/response-body";
import { gitDiffFile, type GitFileDiff } from "@/lib/tauri-bridge";

interface GitDiffPaneProps {
  repoPath: string;
  file: string;
  onClose: () => void;
}

function languageFor(file: string, content: string) {
  // Reuses the response viewer's content sniffing, keyed off the file extension.
  const ext = file.split(".").pop()?.toLowerCase() ?? "";
  const pseudoContentType = ext === "json" ? "application/json" : ext === "xml" ? "application/xml" : null;
  const language = selectBodyLanguage(pseudoContentType, content);
  if (language === "json") return json();
  if (language === "xml") return xml();
  return [];
}

function ReadOnlyText({ content, file }: { content: string; file: string }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewRef = useRef<EditorView | null>(null);

  useEffect(() => {
    if (!containerRef.current) return;
    const view = new EditorView({
      state: EditorState.create({
        doc: content,
        extensions: [
          lineNumbers(),
          EditorState.readOnly.of(true),
          EditorView.editable.of(false),
          EditorView.lineWrapping,
          // Same size policy as the response viewer rather than a second one.
          content.length < HIGHLIGHT_MAX_BYTES ? languageFor(file, content) : [],
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
    if (!view || view.state.doc.toString() === content) return;
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: content } });
  }, [content]);

  return <div ref={containerRef} className="h-full min-h-0 w-full overflow-auto" />;
}

export function GitDiffPane({ repoPath, file, onClose }: GitDiffPaneProps) {
  const [diff, setDiff] = useState<GitFileDiff | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [sideBySide, setSideBySide] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setDiff(null);
    setError(null);
    gitDiffFile(repoPath, file)
      .then((d) => { if (!cancelled) setDiff(d); })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : String(e)); });
    return () => { cancelled = true; };
  }, [repoPath, file]);

  const isNew = useMemo(() => diff !== null && diff.original === null && !diff.isBinary, [diff]);

  return (
    <div className="flex min-h-0 flex-1 flex-col border-t" data-testid="git-diff-pane">
      <div className="flex items-center gap-2 border-b px-3 py-1.5">
        <span className="truncate font-mono text-xs font-medium" title={file}>{file}</span>
        <div className="ml-auto flex items-center gap-1">
          {!isNew && !diff?.isBinary && (
            <button
              onClick={() => setSideBySide(!sideBySide)}
              className="flex items-center gap-1 rounded border px-1.5 py-0.5 text-xs hover:bg-accent"
              data-testid="git-diff-layout-toggle"
              title={sideBySide ? "Show current only" : "Show original and current"}
            >
              {sideBySide ? <Rows2 className="h-3 w-3" /> : <Columns2 className="h-3 w-3" />}
              {sideBySide ? "Single" : "Compare"}
            </button>
          )}
          <button
            onClick={onClose}
            className="rounded p-1 text-muted-foreground hover:bg-accent"
            data-testid="git-diff-close"
            aria-label="Close diff"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      {error && (
        <div className="px-3 py-2 text-xs" style={{ color: "var(--destructive)" }} data-testid="git-diff-error">
          {error}
        </div>
      )}

      {!error && !diff && (
        <div className="px-3 py-2 text-xs text-muted-foreground">Loading diff…</div>
      )}

      {diff?.isBinary && (
        <div className="px-3 py-2 text-xs text-muted-foreground" data-testid="git-diff-binary">
          Binary file — no text diff available.
        </div>
      )}

      {diff && !diff.isBinary && (
        <div className="flex min-h-0 flex-1">
          {sideBySide && diff.original !== null && (
            <>
              <div className="flex min-h-0 w-1/2 flex-col border-r">
                <span className="border-b px-2 py-0.5 text-[10px] uppercase tracking-wide text-muted-foreground">
                  Original (HEAD)
                </span>
                <ReadOnlyText content={diff.original} file={file} />
              </div>
              <div className="flex min-h-0 w-1/2 flex-col">
                <span className="border-b px-2 py-0.5 text-[10px] uppercase tracking-wide text-muted-foreground">
                  Current
                </span>
                <ReadOnlyText content={diff.current} file={file} />
              </div>
            </>
          )}
          {(!sideBySide || diff.original === null) && (
            <div className="flex min-h-0 flex-1 flex-col">
              <span className="border-b px-2 py-0.5 text-[10px] uppercase tracking-wide text-muted-foreground">
                {isNew ? "New file" : "Current"}
              </span>
              <ReadOnlyText content={diff.current} file={file} />
            </div>
          )}
        </div>
      )}
    </div>
  );
}
