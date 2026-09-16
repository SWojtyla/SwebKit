using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// Pure row-level compare between two row sets keyed on <paramref name="keyColumns"/>.
/// Shared by <c>SqlDatabaseClient</c> (real compare) and <c>DemoSqlClient</c> (demo compare);
/// kept in Core so both, and the unit tests, can reach it without the SqlClient dependency.
/// </summary>
public static class SqlDataComparer
{
    /// <summary>Compares <paramref name="sourceRows"/> to <paramref name="targetRows"/>.
    /// Key equality uses <see cref="Canonicalize"/>; non-key columns are compared over the
    /// intersection of both sides' column names, with one-sided columns reported as
    /// <see cref="SqlDataCompareResult.SchemaWarnings"/>.</summary>
    public static SqlDataCompareResult Compare(
        IReadOnlyList<Dictionary<string, object?>> sourceRows,
        IReadOnlyList<Dictionary<string, object?>> targetRows,
        IReadOnlyList<string> keyColumns,
        int maxDiffRows)
    {
        var result = new SqlDataCompareResult();
        if (keyColumns.Count == 0)
            return result;

        var sourceColumns = ColumnUnion(sourceRows);
        var targetColumns = ColumnUnion(targetRows);
        var sharedColumns = sourceColumns.Intersect(targetColumns, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var column in sourceColumns.Where(c => !sharedColumns.Contains(c, StringComparer.OrdinalIgnoreCase)))
            result.SchemaWarnings.Add($"Column '{column}' exists only in source — excluded from comparison.");
        foreach (var column in targetColumns.Where(c => !sharedColumns.Contains(c, StringComparer.OrdinalIgnoreCase)))
            result.SchemaWarnings.Add($"Column '{column}' exists only in target — excluded from comparison.");

        var comparedColumns = sharedColumns
            .Where(c => !keyColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var sourceByKey = IndexByKey(sourceRows, keyColumns);
        var targetByKey = IndexByKey(targetRows, keyColumns);

        foreach (var (key, row) in sourceByKey)
        {
            if (!targetByKey.ContainsKey(key))
            {
                result.TotalOnlyInSource++;
                if (result.OnlyInSource.Count < maxDiffRows)
                    result.OnlyInSource.Add(row);
            }
        }

        foreach (var (key, row) in targetByKey)
        {
            if (!sourceByKey.ContainsKey(key))
            {
                result.TotalOnlyInTarget++;
                if (result.OnlyInTarget.Count < maxDiffRows)
                    result.OnlyInTarget.Add(row);
            }
        }

        foreach (var (key, sourceRow) in sourceByKey)
        {
            if (!targetByKey.TryGetValue(key, out var targetRow))
                continue;

            var diffs = new List<SqlColumnDiff>();
            foreach (var column in comparedColumns)
            {
                var sourceValue = sourceRow.GetValueOrDefault(column);
                var targetValue = targetRow.GetValueOrDefault(column);
                if (Canonicalize(sourceValue) != Canonicalize(targetValue))
                    diffs.Add(new SqlColumnDiff { Column = column, SourceValue = sourceValue, TargetValue = targetValue });
            }

            if (diffs.Count == 0)
                continue;

            result.TotalChanged++;
            if (result.Changed.Count < maxDiffRows)
            {
                result.Changed.Add(new SqlChangedRow
                {
                    Key = keyColumns.ToDictionary(k => k, k => sourceRow.GetValueOrDefault(k)),
                    Diffs = diffs,
                });
            }
        }

        result.Truncated =
            result.OnlyInSource.Count < result.TotalOnlyInSource ||
            result.OnlyInTarget.Count < result.TotalOnlyInTarget ||
            result.Changed.Count < result.TotalChanged;

        return result;
    }

    /// <summary>Canonical string form used for key building and value equality —
    /// type-faithful enough that int 10 vs nvarchar "10" still compare equal (a compare is
    /// about values, not storage types) while null stays distinct from "null".</summary>
    public static string Canonicalize(object? value) => value switch
    {
        null or DBNull => "␀null␀",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        DateTime dt => dt.ToUniversalTime().ToString("O"),
        DateTimeOffset dto => dto.UtcDateTime.ToString("O"),
        bool b => b ? "1" : "0",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static Dictionary<string, Dictionary<string, object?>> IndexByKey(
        IReadOnlyList<Dictionary<string, object?>> rows, IReadOnlyList<string> keyColumns)
    {
        var index = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = string.Join("␟", keyColumns.Select(c => Canonicalize(row.GetValueOrDefault(c))));
            index.TryAdd(key, row); // duplicate keys: keep the first — keys are expected unique
        }
        return index;
    }

    private static List<string> ColumnUnion(IReadOnlyList<Dictionary<string, object?>> rows) =>
        rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
