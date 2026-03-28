using System.Globalization;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Observability;

public sealed class GuidedKqlCompiler : IGuidedKqlCompiler
{
    private const int MinimumLimit = 1;
    private const int MaximumLimit = 10000;
    private const int BroadLimitWarningThreshold = 2000;

    private static readonly IReadOnlyDictionary<string, TableSchema> TableSchemas =
        new Dictionary<string, TableSchema>(StringComparer.OrdinalIgnoreCase)
        {
            ["traces"] = TableSchema.Create(
                "traces",
                [
                    Column("timestamp", GuidedKqlValueType.DateTime),
                    Column("message", GuidedKqlValueType.String),
                    Column("severityLevel", GuidedKqlValueType.Number),
                    Column("operation_Id", GuidedKqlValueType.String),
                    Column("operation_Name", GuidedKqlValueType.String),
                    Column("cloud_RoleName", GuidedKqlValueType.String),
                    Column("itemId", GuidedKqlValueType.String),
                ]),
            ["requests"] = TableSchema.Create(
                "requests",
                [
                    Column("timestamp", GuidedKqlValueType.DateTime),
                    Column("name", GuidedKqlValueType.String),
                    Column("resultCode", GuidedKqlValueType.String),
                    Column("success", GuidedKqlValueType.Boolean),
                    Column("duration", GuidedKqlValueType.Number),
                    Column("operation_Id", GuidedKqlValueType.String),
                    Column("operation_Name", GuidedKqlValueType.String),
                    Column("cloud_RoleName", GuidedKqlValueType.String),
                    Column("session_Id", GuidedKqlValueType.String),
                    Column("user_AuthenticatedId", GuidedKqlValueType.String),
                ]),
            ["exceptions"] = TableSchema.Create(
                "exceptions",
                [
                    Column("timestamp", GuidedKqlValueType.DateTime),
                    Column("type", GuidedKqlValueType.String),
                    Column("problemId", GuidedKqlValueType.String),
                    Column("innermostMessage", GuidedKqlValueType.String),
                    Column("severityLevel", GuidedKqlValueType.Number),
                    Column("operation_Id", GuidedKqlValueType.String),
                    Column("operation_Name", GuidedKqlValueType.String),
                    Column("cloud_RoleName", GuidedKqlValueType.String),
                ]),
            ["dependencies"] = TableSchema.Create(
                "dependencies",
                [
                    Column("timestamp", GuidedKqlValueType.DateTime),
                    Column("name", GuidedKqlValueType.String),
                    Column("target", GuidedKqlValueType.String),
                    Column("type", GuidedKqlValueType.String),
                    Column("resultCode", GuidedKqlValueType.String),
                    Column("success", GuidedKqlValueType.Boolean),
                    Column("duration", GuidedKqlValueType.Number),
                    Column("operation_Id", GuidedKqlValueType.String),
                ]),
            ["customEvents"] = TableSchema.Create(
                "customEvents",
                [
                    Column("timestamp", GuidedKqlValueType.DateTime),
                    Column("name", GuidedKqlValueType.String),
                    Column("operation_Id", GuidedKqlValueType.String),
                    Column("cloud_RoleName", GuidedKqlValueType.String),
                ]),
            ["availabilityResults"] = TableSchema.Create(
                "availabilityResults",
                [
                    Column("timestamp", GuidedKqlValueType.DateTime),
                    Column("name", GuidedKqlValueType.String),
                    Column("location", GuidedKqlValueType.String),
                    Column("success", GuidedKqlValueType.Boolean),
                    Column("duration", GuidedKqlValueType.Number),
                    Column("message", GuidedKqlValueType.String),
                ]),
        };

    public GuidedKqlCompileResult Compile(GuidedKqlQueryDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var issues = new List<GuidedKqlCompileIssue>();
        var schema = GetSchema(definition.Table, issues);
        var normalizedLimit = NormalizeLimit(definition.Limit, issues);
        var filters = definition.Filters ?? [];
        var projectionsInput = definition.Projections ?? [];
        var sortInput = definition.Sort ?? new GuidedKqlSort();

        var filterExpressions = schema is null
            ? []
            : CompileFilters(filters, schema, issues);

        var projections = schema is null
            ? []
            : CompileProjections(projectionsInput, schema, issues);

        var sort = schema is null
            ? null
            : CompileSort(sortInput, schema, issues);

        if (normalizedLimit >= BroadLimitWarningThreshold)
        {
            issues.Add(new GuidedKqlCompileIssue(
                Severity: GuidedKqlCompileIssueSeverity.Warning,
                Code: "LIMIT_BROAD",
                Message: $"Row limit {normalizedLimit} can produce expensive queries.",
                Field: "limit"));
        }

        if (schema is null || sort is null || issues.Any(static issue => issue.IsError))
        {
            return GuidedKqlCompileResult.Invalid(issues);
        }

        var lines = new List<string>
        {
            schema.Name,
        };

        foreach (var expression in filterExpressions)
        {
            lines.Add($"| where {expression}");
        }

        if (projections.Count > 0)
        {
            lines.Add($"| project {string.Join(", ", projections)}");
        }

        lines.Add($"| order by {sort.Column} {(sort.Descending ? "desc" : "asc")}");
        lines.Add($"| take {normalizedLimit.ToString(CultureInfo.InvariantCulture)}");

        return GuidedKqlCompileResult.Success(string.Join('\n', lines), issues);
    }

    private static TableSchema? GetSchema(string table, ICollection<GuidedKqlCompileIssue> issues)
    {
        var normalizedTable = (table ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedTable))
        {
            issues.Add(new GuidedKqlCompileIssue(
                Severity: GuidedKqlCompileIssueSeverity.Error,
                Code: "TABLE_REQUIRED",
                Message: "A source table is required.",
                Field: "table"));
            return null;
        }

        if (!TableSchemas.TryGetValue(normalizedTable, out var schema))
        {
            issues.Add(new GuidedKqlCompileIssue(
                Severity: GuidedKqlCompileIssueSeverity.Error,
                Code: "TABLE_INVALID",
                Message: $"Table '{normalizedTable}' is not supported.",
                Field: "table"));
            return null;
        }

        return schema;
    }

    private static int NormalizeLimit(int limit, ICollection<GuidedKqlCompileIssue> issues)
    {
        if (limit < MinimumLimit)
        {
            issues.Add(new GuidedKqlCompileIssue(
                Severity: GuidedKqlCompileIssueSeverity.Error,
                Code: "LIMIT_INVALID",
                Message: $"Limit must be at least {MinimumLimit}.",
                Field: "limit"));
            return MinimumLimit;
        }

        if (limit > MaximumLimit)
        {
            issues.Add(new GuidedKqlCompileIssue(
                Severity: GuidedKqlCompileIssueSeverity.Error,
                Code: "LIMIT_TOO_HIGH",
                Message: $"Limit cannot exceed {MaximumLimit}.",
                Field: "limit"));
            return MaximumLimit;
        }

        return limit;
    }

    private static IReadOnlyList<string> CompileFilters(
        IReadOnlyList<GuidedKqlFilter> filters,
        TableSchema schema,
        ICollection<GuidedKqlCompileIssue> issues)
    {
        if (filters.Count == 0)
        {
            return [];
        }

        var expressions = new List<string>(filters.Count);

        for (var index = 0; index < filters.Count; index++)
        {
            var filter = filters[index];
            var fieldPrefix = $"filters[{index}]";

            var columnName = (filter.Column ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(columnName))
            {
                issues.Add(new GuidedKqlCompileIssue(
                    Severity: GuidedKqlCompileIssueSeverity.Error,
                    Code: "FILTER_COLUMN_REQUIRED",
                    Message: "Filter column is required.",
                    Field: $"{fieldPrefix}.column"));
                continue;
            }

            if (!schema.TryGetColumn(columnName, out var column))
            {
                issues.Add(new GuidedKqlCompileIssue(
                    Severity: GuidedKqlCompileIssueSeverity.Error,
                    Code: "FILTER_COLUMN_INVALID",
                    Message: $"Column '{columnName}' is not valid for table '{schema.Name}'.",
                    Field: $"{fieldPrefix}.column"));
                continue;
            }

            if (!Enum.IsDefined(filter.Operator))
            {
                issues.Add(new GuidedKqlCompileIssue(
                    Severity: GuidedKqlCompileIssueSeverity.Error,
                    Code: "FILTER_OPERATOR_INVALID",
                    Message: "Filter operator is invalid.",
                    Field: $"{fieldPrefix}.operator"));
                continue;
            }

            var value = (filter.Value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new GuidedKqlCompileIssue(
                    Severity: GuidedKqlCompileIssueSeverity.Error,
                    Code: "FILTER_VALUE_REQUIRED",
                    Message: "Filter value is required.",
                    Field: $"{fieldPrefix}.value"));
                continue;
            }

            if (!IsOperatorSupported(filter.Operator, column.Type))
            {
                issues.Add(new GuidedKqlCompileIssue(
                    Severity: GuidedKqlCompileIssueSeverity.Error,
                    Code: "FILTER_OPERATOR_UNSUPPORTED",
                    Message: $"Operator '{filter.Operator}' is not supported for column '{column.Name}'.",
                    Field: $"{fieldPrefix}.operator"));
                continue;
            }

            if (!TryFormatLiteral(value, column.Type, fieldPrefix, issues, out var literal))
            {
                continue;
            }

            expressions.Add(BuildFilterExpression(column.Name, filter.Operator, literal));
        }

        return expressions;
    }

    private static IReadOnlyList<string> CompileProjections(
        IReadOnlyList<string> projections,
        TableSchema schema,
        ICollection<GuidedKqlCompileIssue> issues)
    {
        if (projections.Count == 0)
        {
            return [];
        }

        var normalized = new List<string>(projections.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < projections.Count; index++)
        {
            var projection = (projections[index] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(projection))
            {
                issues.Add(new GuidedKqlCompileIssue(
                    Severity: GuidedKqlCompileIssueSeverity.Error,
                    Code: "PROJECTION_COLUMN_REQUIRED",
                    Message: "Projection column is required.",
                    Field: $"projections[{index}]"));
                continue;
            }

            if (!schema.TryGetColumn(projection, out var column))
            {
                issues.Add(new GuidedKqlCompileIssue(
                    Severity: GuidedKqlCompileIssueSeverity.Error,
                    Code: "PROJECTION_COLUMN_INVALID",
                    Message: $"Projection column '{projection}' is not valid for table '{schema.Name}'.",
                    Field: $"projections[{index}]"));
                continue;
            }

            if (seen.Add(column.Name))
            {
                normalized.Add(column.Name);
            }
        }

        return normalized;
    }

    private static CompiledSort? CompileSort(
        GuidedKqlSort sort,
        TableSchema schema,
        ICollection<GuidedKqlCompileIssue> issues)
    {
        var normalizedColumn = string.IsNullOrWhiteSpace(sort.Column)
            ? "timestamp"
            : sort.Column.Trim();

        if (!schema.TryGetColumn(normalizedColumn, out var column))
        {
            issues.Add(new GuidedKqlCompileIssue(
                Severity: GuidedKqlCompileIssueSeverity.Error,
                Code: "SORT_COLUMN_INVALID",
                Message: $"Sort column '{normalizedColumn}' is not valid for table '{schema.Name}'.",
                Field: "sort.column"));
            return null;
        }

        return new CompiledSort(column.Name, sort.Descending);
    }

    private static bool IsOperatorSupported(GuidedKqlFilterOperator @operator, GuidedKqlValueType valueType)
    {
        return @operator switch
        {
            GuidedKqlFilterOperator.Equals => true,
            GuidedKqlFilterOperator.NotEquals => true,
            GuidedKqlFilterOperator.Contains => valueType == GuidedKqlValueType.String,
            GuidedKqlFilterOperator.StartsWith => valueType == GuidedKqlValueType.String,
            GuidedKqlFilterOperator.EndsWith => valueType == GuidedKqlValueType.String,
            GuidedKqlFilterOperator.GreaterThan => valueType is GuidedKqlValueType.Number or GuidedKqlValueType.DateTime,
            GuidedKqlFilterOperator.GreaterThanOrEqual => valueType is GuidedKqlValueType.Number or GuidedKqlValueType.DateTime,
            GuidedKqlFilterOperator.LessThan => valueType is GuidedKqlValueType.Number or GuidedKqlValueType.DateTime,
            GuidedKqlFilterOperator.LessThanOrEqual => valueType is GuidedKqlValueType.Number or GuidedKqlValueType.DateTime,
            _ => false,
        };
    }

    private static bool TryFormatLiteral(
        string value,
        GuidedKqlValueType valueType,
        string fieldPrefix,
        ICollection<GuidedKqlCompileIssue> issues,
        out string literal)
    {
        switch (valueType)
        {
            case GuidedKqlValueType.Number:
                if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number))
                {
                    literal = number.ToString("G", CultureInfo.InvariantCulture);
                    return true;
                }

                issues.Add(new GuidedKqlCompileIssue(
                    Severity: GuidedKqlCompileIssueSeverity.Error,
                    Code: "FILTER_VALUE_INVALID",
                    Message: $"'{value}' is not a valid number.",
                    Field: $"{fieldPrefix}.value"));
                literal = string.Empty;
                return false;

            case GuidedKqlValueType.Boolean:
                if (bool.TryParse(value, out var booleanValue))
                {
                    literal = booleanValue ? "true" : "false";
                    return true;
                }

                issues.Add(new GuidedKqlCompileIssue(
                    Severity: GuidedKqlCompileIssueSeverity.Error,
                    Code: "FILTER_VALUE_INVALID",
                    Message: $"'{value}' is not a valid boolean.",
                    Field: $"{fieldPrefix}.value"));
                literal = string.Empty;
                return false;

            case GuidedKqlValueType.DateTime:
                if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
                {
                    literal = $"datetime({timestamp.UtcDateTime:O})";
                    return true;
                }

                issues.Add(new GuidedKqlCompileIssue(
                    Severity: GuidedKqlCompileIssueSeverity.Error,
                    Code: "FILTER_VALUE_INVALID",
                    Message: $"'{value}' is not a valid date/time.",
                    Field: $"{fieldPrefix}.value"));
                literal = string.Empty;
                return false;

            default:
                literal = $"'{EscapeStringLiteral(value)}'";
                return true;
        }
    }

    private static string BuildFilterExpression(string column, GuidedKqlFilterOperator @operator, string literal)
    {
        return @operator switch
        {
            GuidedKqlFilterOperator.Equals => $"{column} == {literal}",
            GuidedKqlFilterOperator.NotEquals => $"{column} != {literal}",
            GuidedKqlFilterOperator.Contains => $"{column} contains {literal}",
            GuidedKqlFilterOperator.StartsWith => $"{column} startswith {literal}",
            GuidedKqlFilterOperator.EndsWith => $"{column} endswith {literal}",
            GuidedKqlFilterOperator.GreaterThan => $"{column} > {literal}",
            GuidedKqlFilterOperator.GreaterThanOrEqual => $"{column} >= {literal}",
            GuidedKqlFilterOperator.LessThan => $"{column} < {literal}",
            GuidedKqlFilterOperator.LessThanOrEqual => $"{column} <= {literal}",
            _ => throw new ArgumentOutOfRangeException(nameof(@operator), @operator, "Unsupported operator."),
        };
    }

    private static string EscapeStringLiteral(string value) => value.Replace("'", "''");

    private static ColumnDefinition Column(string name, GuidedKqlValueType type) => new(name, type);

    private enum GuidedKqlValueType
    {
        String = 0,
        Number = 1,
        DateTime = 2,
        Boolean = 3,
    }

    private sealed record ColumnDefinition(string Name, GuidedKqlValueType Type);

    private sealed record CompiledSort(string Column, bool Descending);

    private sealed class TableSchema
    {
        private readonly IReadOnlyDictionary<string, ColumnDefinition> _columns;

        private TableSchema(string name, IReadOnlyDictionary<string, ColumnDefinition> columns)
        {
            Name = name;
            _columns = columns;
        }

        public string Name { get; }

        public static TableSchema Create(string name, IReadOnlyList<ColumnDefinition> columns)
        {
            var dictionary = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                dictionary[column.Name] = column;
            }

            return new TableSchema(name, dictionary);
        }

        public bool TryGetColumn(string name, out ColumnDefinition column) => _columns.TryGetValue(name, out column!);
    }
}
