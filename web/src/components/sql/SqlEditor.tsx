import { useEffect, useRef, useState } from "react";
import { EditorState, Compartment } from "@codemirror/state";
import {
    defaultKeymap,
    history,
    historyKeymap,
    indentWithTab,
} from "@codemirror/commands";
import { bracketMatching } from "@codemirror/language";
import {
    closeBrackets,
    closeBracketsKeymap,
    autocompletion,
    type CompletionContext,
    type CompletionResult,
} from "@codemirror/autocomplete";
import {
    EditorView,
    keymap,
    lineNumbers,
    highlightActiveLine,
    highlightActiveLineGutter,
} from "@codemirror/view";
import { sql, MSSQL } from "@codemirror/lang-sql";
import { swebkitHighlighting } from "@/lib/codemirror-theme";
import { fetchSqlCompletionContext } from "@/lib/hooks/useSql";
import type { SqlSchemaModel } from "@/lib/types";

/** Name-level completion candidates built from the already-loaded schema tree —
 * feeds "kind: any" positions (keywords come from the dialect itself via lang-sql's
 * schema completion; these cover object/column names the dialect can't know). */
const sqlSnippets = [
    { label: "SELECT TOP", type: "keyword", detail: "Limited result set", apply: "SELECT TOP (100) *\nFROM " },
    { label: "INNER JOIN", type: "keyword", detail: "Join matching rows", apply: "INNER JOIN table_name AS t ON t.id = source.id" },
    { label: "LEFT JOIN", type: "keyword", detail: "Keep all source rows", apply: "LEFT JOIN table_name AS t ON t.id = source.id" },
    { label: "GROUP BY", type: "keyword", detail: "Aggregate rows", apply: "GROUP BY column_name\nORDER BY column_name" },
    { label: "COUNT", type: "function", detail: "Count rows", apply: "COUNT(*) AS row_count" },
];

function buildSchemaCompletionOptions(schema: SqlSchemaModel | undefined) {
    const tables: { label: string; type: string; detail?: string }[] = [];
    const columns: { label: string; type: string; detail?: string }[] = [];
    if (schema) {
        for (const group of schema.schemas) {
            for (const obj of group.objects) {
                tables.push({
                    label: obj.name,
                    type: obj.kind === "view" ? "class" : "property",
                    detail: `${group.name} (${obj.kind})`,
                });
                for (const col of obj.columns) {
                    columns.push({
                        label: col.name,
                        type: "variable",
                        detail: `${group.name}.${obj.name} · ${col.dataType}`,
                    });
                }
            }
        }
    }
    return { tables, columns };
}

interface SqlEditorProps {
    value: string;
    onChange: (value: string) => void;
    onRun: () => void;
    connectionId: string | null;
    schema: SqlSchemaModel | undefined;
}

/**
 * Query editor: CodeMirror + lang-sql for T-SQL highlighting and keyword completion,
 * plus a custom completion source that adds (a) name-level table/column suggestions
 * from the loaded schema tree and (b) alias-aware column suggestions resolved through
 * the sidecar's ScriptDom completion-context endpoint (`SELECT o.| FROM orders o`).
 */
export function SqlEditor({
    value,
    onChange,
    onRun,
    connectionId,
    schema,
}: SqlEditorProps) {
    const containerRef = useRef<HTMLDivElement>(null);
    const viewRef = useRef<EditorView | null>(null);
    const [helpOpen, setHelpOpen] = useState(false);
    const completionRef = useRef(new Compartment());
    const onChangeRef = useRef(onChange);
    const onRunRef = useRef(onRun);
    const connectionRef = useRef(connectionId);
    const schemaRef = useRef(schema);
    useEffect(() => {
        onChangeRef.current = onChange;
        onRunRef.current = onRun;
        connectionRef.current = connectionId;
        schemaRef.current = schema;
    });

    useEffect(() => {
        if (!containerRef.current) return;

        const completionSource = async (
            context: CompletionContext,
        ): Promise<CompletionResult | null> => {
            const { tables, columns } = buildSchemaCompletionOptions(
                schemaRef.current,
            );
            const connId = connectionRef.current;
            const pos = context.pos;

            // "alias." or "table." right before the cursor → column list for that one object.
            const dotted = context.matchBefore(
                /[A-Za-z_][\w[\]]*\.\s*[\w[\]]*$/,
            );
            if (dotted && connId) {
                try {
                    const resolved = await fetchSqlCompletionContext(
                        connId,
                        context.state.doc.toString(),
                        pos,
                    );
                    const scope = resolved.columnScope;
                    if (scope) {
                        const tableRef = resolved.tables.find(
                            (t) =>
                                (t.alias ?? t.name).toLowerCase() ===
                                    scope.toLowerCase() ||
                                t.name.toLowerCase() === scope.toLowerCase(),
                        );
                        const schemaModel = schemaRef.current;
                        const group = schemaModel?.schemas.find(
                            (s) =>
                                s.name.toLowerCase() ===
                                (tableRef?.schema ?? "dbo").toLowerCase(),
                        );
                        const obj = group?.objects.find(
                            (o) =>
                                o.name.toLowerCase() ===
                                (tableRef?.name ?? scope).toLowerCase(),
                        );
                        if (obj) {
                            const partial = context.matchBefore(/[\w[\]]*$/)!;
                            return {
                                from: partial.from,
                                options: obj.columns.map((c) => ({
                                    label: c.name,
                                    type: "variable",
                                    detail: c.dataType,
                                })),
                                validFor: /^[\w[\]]*$/,
                            };
                        }
                    }
                } catch {
                    // Completion is best-effort — a failed context call falls through to name-level.
                }
            }

            const word = context.matchBefore(/[\w[\]]*/);
            if (!word || (word.from === word.to && !context.explicit))
                return null;

            // After FROM/JOIN/INTO/UPDATE → table names only.
            const before = context.state.sliceDoc(
                Math.max(0, word.from - 40),
                word.from,
            );
            if (/\b(from|join|into|update)\s+[\w[\].]*$/i.test(before)) {
                return {
                    from: word.from,
                    options: tables,
                    validFor: /^[\w[\]]*$/,
                };
            }

            return {
                from: word.from,
                options: [...sqlSnippets, ...tables, ...columns],
                validFor: /^[\w[\]]*$/,
            };
        };

        const view = new EditorView({
            state: EditorState.create({
                doc: value,
                extensions: [
                    lineNumbers(),
                    highlightActiveLine(),
                    highlightActiveLineGutter(),
                    bracketMatching(),
                    closeBrackets(),
                    history(),
                    keymap.of([
                        {
                            key: "Ctrl-Enter",
                            mac: "Cmd-Enter",
                            run: () => {
                                onRunRef.current();
                                return true;
                            },
                        },
                        ...closeBracketsKeymap,
                        ...defaultKeymap,
                        ...historyKeymap,
                        indentWithTab,
                    ]),
                    sql({ dialect: MSSQL, upperCaseKeywords: true }),
                    completionRef.current.of(
                        autocompletion({ override: [completionSource] }),
                    ),
                    EditorView.updateListener.of((update) => {
                        if (update.docChanged)
                            onChangeRef.current(update.state.doc.toString());
                    }),
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
        // Mount-once — schema/connection changes only feed the completion source via refs.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    useEffect(() => {
        const view = viewRef.current;
        if (!view || view.state.doc.toString() === value) return;
        view.dispatch({
            changes: { from: 0, to: view.state.doc.length, insert: value },
        });
    }, [value]);

    useEffect(() => {
        if (!helpOpen) return;
        const closeOnEscape = (event: KeyboardEvent) => {
            if (event.key === "Escape") setHelpOpen(false);
        };
        document.addEventListener("keydown", closeOnEscape);
        return () => document.removeEventListener("keydown", closeOnEscape);
    }, [helpOpen]);

    return (
        <div
            className="relative flex min-h-0 flex-1 flex-col overflow-hidden rounded border bg-background"
            data-testid="sql-editor"
        >
            <div className="absolute right-2 top-1 z-30">
                <button
                    onClick={() => setHelpOpen((open) => !open)}
                    className="rounded border bg-card px-2 py-0.5 text-xs font-medium hover:bg-accent"
                    aria-expanded={helpOpen}
                    aria-label="SQL syntax help"
                    data-testid="sql-syntax-help-toggle"
                >
                    ?
                </button>
                {helpOpen && (
                    <div
                        className="absolute right-0 top-full mt-1 w-80 space-y-2 rounded-md border bg-popover p-3 text-xs shadow-lg"
                        data-testid="sql-syntax-help"
                    >
                        <div className="flex items-center justify-between">
                            <strong>Common T-SQL patterns</strong>
                            <button
                                onClick={() => setHelpOpen(false)}
                                className="text-muted-foreground hover:text-foreground"
                                data-testid="sql-syntax-help-close"
                            >
                                Close
                            </button>
                        </div>
                        <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
                            <dt className="font-mono">SELECT TOP (100)</dt><dd>Limit returned rows</dd>
                            <dt className="font-mono">WHERE x = value</dt><dd>Filter rows</dd>
                            <dt className="font-mono">LIKE '%text%'</dt><dd>Search text</dd>
                            <dt className="font-mono">IS NULL</dt><dd>Match missing values</dd>
                            <dt className="font-mono">INNER JOIN</dt><dd>Rows present on both sides</dd>
                            <dt className="font-mono">LEFT JOIN</dt><dd>Keep all source rows</dd>
                            <dt className="font-mono">GROUP BY</dt><dd>Aggregate with COUNT/SUM/AVG</dd>
                            <dt className="font-mono">ORDER BY x DESC</dt><dd>Sort newest/highest first</dd>
                        </dl>
                        <p className="text-muted-foreground">Press Ctrl+Space in the editor for snippets and schema suggestions.</p>
                    </div>
                )}
            </div>
            <div ref={containerRef} className="min-h-0 flex-1" />
            {/* Playwright/a11y mirror — same trick as BodyCodeEditor: a real textarea tests can
          type into and screen readers can find, without CodeMirror's contenteditable. */}
            <textarea
                data-testid="sql-editor-input"
                aria-label="SQL query editor"
                tabIndex={-1}
                value={value}
                onChange={(e) => onChange(e.target.value)}
                className="absolute left-0 top-0 z-20 h-4 w-4 opacity-0"
            />
        </div>
    );
}

