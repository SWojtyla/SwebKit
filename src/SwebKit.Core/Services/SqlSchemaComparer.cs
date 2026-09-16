using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// Pure catalog compare between two <see cref="SqlSchemaModel"/>s — objects only-in-source /
/// only-in-target / differing (columns, indexes, foreign keys). Report only; never generates
/// sync scripts. Shared by the real and demo clients, unit-testable without a database.
/// </summary>
public static class SqlSchemaComparer
{
    public static SqlSchemaCompareResult Compare(SqlSchemaModel source, SqlSchemaModel target)
    {
        var result = new SqlSchemaCompareResult();
        var sourceObjects = Flatten(source);
        var targetObjects = Flatten(target);

        foreach (var (key, sourceObject) in sourceObjects)
        {
            if (!targetObjects.TryGetValue(key, out var targetObject))
            {
                result.OnlyInSource.Add(new SqlObjectDiff
                {
                    Schema = sourceObject.Schema,
                    Name = sourceObject.Name,
                    Kind = sourceObject.Kind,
                });
                continue;
            }

            var diffs = DiffObject(sourceObject, targetObject);
            if (diffs.Count > 0)
            {
                result.Differing.Add(new SqlObjectDiff
                {
                    Schema = sourceObject.Schema,
                    Name = sourceObject.Name,
                    Kind = sourceObject.Kind,
                    Diffs = diffs,
                });
            }
        }

        foreach (var (key, targetObject) in targetObjects)
        {
            if (!sourceObjects.ContainsKey(key))
            {
                result.OnlyInTarget.Add(new SqlObjectDiff
                {
                    Schema = targetObject.Schema,
                    Name = targetObject.Name,
                    Kind = targetObject.Kind,
                });
            }
        }

        return result;
    }

    private static Dictionary<string, FlatObject> Flatten(SqlSchemaModel model)
    {
        var map = new Dictionary<string, FlatObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var schema in model.Schemas)
        foreach (var obj in schema.Objects)
        {
            var flat = new FlatObject(schema.Name, obj);
            map[$"{schema.Name}.{obj.Name}"] = flat;
        }
        return map;
    }

    private static List<SqlPropertyDiff> DiffObject(FlatObject source, FlatObject target)
    {
        var diffs = new List<SqlPropertyDiff>();

        var sourceColumns = source.Object.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var targetColumns = target.Object.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, sourceColumn) in sourceColumns)
        {
            if (!targetColumns.TryGetValue(name, out var targetColumn))
            {
                diffs.Add(new SqlPropertyDiff { Property = $"column {name}", SourceValue = Describe(sourceColumn), TargetValue = "(missing)" });
                continue;
            }
            var s = Describe(sourceColumn);
            var t = Describe(targetColumn);
            if (!string.Equals(s, t, StringComparison.OrdinalIgnoreCase))
                diffs.Add(new SqlPropertyDiff { Property = $"column {name}", SourceValue = s, TargetValue = t });
        }
        foreach (var name in targetColumns.Keys.Where(n => !sourceColumns.ContainsKey(n)))
            diffs.Add(new SqlPropertyDiff { Property = $"column {name}", SourceValue = "(missing)", TargetValue = Describe(targetColumns[name]) });

        DiffNamedSet(diffs, "index", source.Object.Indexes.Select(DescribeIndex), target.Object.Indexes.Select(DescribeIndex));
        DiffNamedSet(diffs, "foreign key", source.Object.ForeignKeys.Select(DescribeFk), target.Object.ForeignKeys.Select(DescribeFk));

        return diffs;
    }

    private static void DiffNamedSet(
        List<SqlPropertyDiff> diffs,
        string label,
        IEnumerable<(string Name, string Desc)> source,
        IEnumerable<(string Name, string Desc)> target)
    {
        var sourceMap = source.ToDictionary(x => x.Name, x => x.Desc, StringComparer.OrdinalIgnoreCase);
        var targetMap = target.ToDictionary(x => x.Name, x => x.Desc, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, s) in sourceMap)
        {
            if (!targetMap.TryGetValue(name, out var t))
                diffs.Add(new SqlPropertyDiff { Property = $"{label} {name}", SourceValue = s, TargetValue = "(missing)" });
            else if (!string.Equals(s, t, StringComparison.OrdinalIgnoreCase))
                diffs.Add(new SqlPropertyDiff { Property = $"{label} {name}", SourceValue = s, TargetValue = t });
        }
        foreach (var name in targetMap.Keys.Where(n => !sourceMap.ContainsKey(n)))
            diffs.Add(new SqlPropertyDiff { Property = $"{label} {name}", SourceValue = "(missing)", TargetValue = targetMap[name] });
    }

    private static string Describe(SqlColumnInfo c) =>
        $"{c.DataType}{(c.IsNullable ? " null" : " not null")}{(c.IsPrimaryKey ? " pk" : "")}";

    private static (string, string) DescribeIndex(SqlIndexInfo i) =>
        (i.Name, $"{(i.IsUnique ? "unique " : "")}{(i.IsPrimaryKey ? "pk " : "")}({string.Join(",", i.Columns)})");

    private static (string, string) DescribeFk(SqlForeignKeyInfo f) =>
        (f.Name, $"→ {f.ReferencedObject}");

    private sealed record FlatObject(string Schema, SqlObjectInfo Object)
    {
        public string Name => Object.Name;
        public string Kind => Object.Kind;
    }
}
