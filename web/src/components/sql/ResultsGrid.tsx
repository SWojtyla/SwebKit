import { Download } from "lucide-react";
import { EmptyState } from "@/components/shared/EmptyState";
import { downloadText } from "@/lib/download";
import { cellText, toCsv } from "@/lib/sql-csv";
import type { SqlQueryResult } from "@/lib/types";

interface ResultsGridProps {
    result: SqlQueryResult | undefined;
    isRunning: boolean;
    testId?: string;
}

/** Shared result grid for query output and table browsing — bounded by the server's
 * maxRows cap, so a plain table render (no virtualization needed at ≤5000 rows). */
export function ResultsGrid({
    result,
    isRunning,
    testId = "sql-results",
}: ResultsGridProps) {
    if (isRunning) {
        return (
            <div
                className="flex items-center justify-center p-6 text-sm text-muted-foreground"
                data-testid={`${testId}-loading`}
            >
                Running query…
            </div>
        );
    }

    if (!result) {
        return (
            <EmptyState
                title="No results yet"
                description="Run a query or pick a table to see rows here."
                testId={`${testId}-empty`}
            />
        );
    }

    if (result.columns.length === 0) {
        return (
            <div
                className="p-4 text-sm text-muted-foreground"
                data-testid={`${testId}-no-columns`}
            >
                Statement completed —{" "}
                {result.rowsAffected >= 0
                    ? `${result.rowsAffected} row(s) affected, `
                    : ""}
                {result.elapsedMs} ms.
            </div>
        );
    }

    return (
        <div className="flex min-h-0 flex-1 flex-col" data-testid={testId}>
            <div className="flex items-center gap-3 border-b px-3 py-1.5 text-xs text-muted-foreground">
                <span data-testid={`${testId}-count`}>
                    {result.rows.length} row(s)
                    {result.truncated ? " (truncated)" : ""} ·{" "}
                    {result.elapsedMs} ms
                    {result.rowsAffected >= 0
                        ? ` · ${result.rowsAffected} affected`
                        : ""}
                </span>
                <div className="ml-auto flex items-center gap-1">
                    <button
                        onClick={() =>
                            downloadText(
                                "query-results.csv",
                                toCsv(result),
                                "text/csv",
                            )
                        }
                        className="flex items-center gap-1 rounded border px-2 py-0.5 hover:bg-accent"
                        data-testid={`${testId}-export-csv`}
                    >
                        <Download className="h-3 w-3" /> CSV
                    </button>
                    <button
                        onClick={() =>
                            downloadText(
                                "query-results.json",
                                JSON.stringify(result.rows, null, 2),
                            )
                        }
                        className="flex items-center gap-1 rounded border px-2 py-0.5 hover:bg-accent"
                        data-testid={`${testId}-export-json`}
                    >
                        <Download className="h-3 w-3" /> JSON
                    </button>
                </div>
            </div>
            <div className="min-h-0 flex-1 overflow-auto">
                <table className="w-full border-collapse text-xs">
                    <thead className="sticky top-0 bg-card">
                        <tr>
                            {result.columns.map((col) => (
                                <th
                                    key={col.name}
                                    className="border-b px-2 py-1.5 text-left font-medium"
                                    title={col.typeName}
                                >
                                    {col.name}
                                </th>
                            ))}
                        </tr>
                    </thead>
                    <tbody>
                        {result.rows.map((row, i) => (
                            <tr key={i} className="odd:bg-muted/30">
                                {result.columns.map((col) => (
                                    <td
                                        key={col.name}
                                        className="max-w-64 truncate border-b px-2 py-1"
                                        title={cellText(row[col.name])}
                                    >
                                        {row[col.name] === null ||
                                        row[col.name] === undefined ? (
                                            <span className="italic text-muted-foreground">
                                                NULL
                                            </span>
                                        ) : (
                                            cellText(row[col.name])
                                        )}
                                    </td>
                                ))}
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    );
}

