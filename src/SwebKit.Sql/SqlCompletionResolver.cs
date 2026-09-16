using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SwebKit.Sql;

/// <summary>
/// Server-side context resolution for the query editor's autocomplete (Phase 2.2): parses the
/// (possibly mid-typing, unparseable) editor text and reports which table references and aliases
/// are in scope, plus what kind of completion the cursor position suggests. The frontend merges
/// the table list with the schema model it already loaded to produce column suggestions.
/// </summary>
public static class SqlCompletionResolver
{
    public sealed record TableRef(string? Schema, string Name, string? Alias);

    public sealed record Context(List<TableRef> Tables, string? ColumnScope, string Kind);

    /// <summary>
    /// Analyzes <paramref name="sql"/> around <paramref name="cursorOffset"/>.
    /// <paramref name="sql"/> mid-typing often won't parse — tolerant mode returns whatever table
    /// references the parser still recovered (ScriptDom's parse is error-tolerant: it produces a
    /// partial tree plus parse errors rather than failing outright).
    /// </summary>
    public static Context Resolve(string sql, int cursorOffset)
    {
        var tables = new List<TableRef>();

        if (!string.IsNullOrWhiteSpace(sql))
        {
            var parser = new TSql160Parser(initialQuotedIdentifiers: false);
            using var reader = new StringReader(sql);
            var script = parser.Parse(reader, out _) as TSqlScript;
            if (script is not null)
            {
                var visitor = new TableReferenceVisitor();
                script.Accept(visitor);
                tables.AddRange(visitor.Tables);
            }
        }

        var kind = "any";
        string? columnScope = null;
        if (cursorOffset > 0 && cursorOffset <= sql.Length)
        {
            // Identifier + '.' right before the cursor → column suggestions scoped to that
            // alias/table name.
            var before = sql[..cursorOffset];
            var dotMatch = System.Text.RegularExpressions.Regex.Match(
                before, @"([A-Za-z_][A-Za-z0-9_\[\]]*)\.\s*$");
            if (dotMatch.Success)
            {
                kind = "column";
                columnScope = dotMatch.Groups[1].Value.Trim('[', ']');
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(before, @"(from|join|into|update)\s+[A-Za-z0-9_\[\].]*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                kind = "table";
            }
        }

        return new Context(tables, columnScope, kind);
    }

    /// <summary>Collects <see cref="NamedTableReference"/>s (FROM/JOIN targets) with their
    /// schema and alias across the whole script.</summary>
    private sealed class TableReferenceVisitor : TSqlFragmentVisitor
    {
        public readonly List<TableRef> Tables = [];

        public override void ExplicitVisit(NamedTableReference node)
        {
            var parts = node.SchemaObject.Identifiers.Select(i => i.Value).ToList();
            var name = parts.Count > 0 ? parts[^1] : string.Empty;
            var schema = parts.Count > 1 ? parts[^2] : null;
            var alias = node.Alias?.Value;
            if (name.Length > 0)
                Tables.Add(new TableRef(schema, name, alias));
            base.ExplicitVisit(node);
        }
    }
}
