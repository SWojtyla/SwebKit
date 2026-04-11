using SwebKit.Core.Models;

namespace SwebKit.Observability;

internal static class LogQueryResultProjector
{
    public static LogQueryResult Project<TRow>(
        IReadOnlyList<string> columns,
        IEnumerable<TRow> rows,
        Func<TRow, int, object?> getValue,
        TimeSpan executionTime,
        int maxRows)
    {
        var effectiveMaxRows = Math.Max(0, maxRows);
        var projectedRows = new List<LogRow>(effectiveMaxRows);
        var truncated = false;

        foreach (var row in rows)
        {
            var values = new Dictionary<string, object?>(columns.Count);
            for (var i = 0; i < columns.Count; i++)
            {
                values[columns[i]] = getValue(row, i);
            }

            projectedRows.Add(new LogRow(values));
            if (projectedRows.Count > effectiveMaxRows)
            {
                truncated = true;
                projectedRows.RemoveAt(effectiveMaxRows);
                break;
            }
        }

        return new LogQueryResult(columns, projectedRows, executionTime, truncated);
    }
}