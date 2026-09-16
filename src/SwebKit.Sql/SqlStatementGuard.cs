using Microsoft.SqlServer.TransactSql.ScriptDom;
using SwebKit.Core.Models;

namespace SwebKit.Sql;

/// <summary>
/// The single security boundary for SwebKit's read-only SQL mode: classifies a T-SQL batch with
/// the real ScriptDom parser and decides whether it may run on a write-disabled connection.
/// Default-deny — only statement types explicitly known to be read-harmless pass; everything else
/// (DML, DDL, EXEC, transactions, batches containing any mutating statement) is rejected with a
/// user-facing reason. Parse failures fail closed.
/// </summary>
public static class SqlStatementGuard
{
    public readonly record struct Result(bool Allowed, string? Reason);

    /// <summary>Returns <see cref="Result.Allowed"/> true only when every statement in every
    /// batch is provably read-only.</summary>
    public static Result Evaluate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new Result(true, null);

        TSqlScript? script;
        IList<ParseError> errors;
        var parser = new TSql160Parser(initialQuotedIdentifiers: false);
        using (var reader = new StringReader(sql))
        {
            script = parser.Parse(reader, out errors) as TSqlScript;
        }

        if (errors.Count > 0)
            return new Result(false, $"Could not parse the SQL: {errors[0].Message}");

        if (script is null)
            return new Result(false, "Could not parse the SQL.");

        foreach (var batch in script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                if (!IsReadOnly(statement, out var reason))
                    return new Result(false, reason);
            }
        }

        return new Result(true, null);
    }

    /// <summary>Throws <see cref="SqlWriteGuardException"/> when the batch isn't read-only.</summary>
    public static void ThrowIfNotReadOnly(string sql)
    {
        var result = Evaluate(sql);
        if (!result.Allowed)
            throw new SqlWriteGuardException(result.Reason ?? "Statement is not allowed.");
    }

    /// <summary>Allow-list of provably read-harmless statement types. Anything else — including
    /// statement classes added by future ScriptDom versions — is denied by default.</summary>
    private static bool IsReadOnly(TSqlStatement statement, out string? reason)
    {
        switch (statement)
        {
            // SELECT … INTO creates a table — it parses as a SelectStatement but is a write.
            // SELECT … INTO creates a table — it parses as a SelectStatement but is a write.
            case SelectStatement { Into: null }:
            case PrintStatement:
            case UseStatement:
            case PredicateSetStatement:
            case SetCommandStatement:
            case SetOnOffStatement:
            case SetRowCountStatement:
            case SetTransactionIsolationLevelStatement:
            case DeclareVariableStatement:
            case SetVariableStatement:
                reason = null;
                return true;

            case IfStatement ifStatement:
                return AllReadOnly(
                    new[] { ifStatement.ThenStatement, ifStatement.ElseStatement },
                    out reason);
            case WhileStatement whileStatement:
                return IsReadOnly(whileStatement.Statement, out reason);
            case BeginEndBlockStatement block:
                return AllReadOnly(block.StatementList.Statements, out reason);
            case TryCatchStatement tryCatch:
                return AllReadOnly(
                    tryCatch.TryStatements.Statements.Concat(tryCatch.CatchStatements.Statements),
                    out reason);

            case SelectStatement:
                reason = "SELECT … INTO creates a table — enable writes on this connection to run it.";
                return false;

            default:
                var name = statement.GetType().Name;
                if (name.EndsWith("Statement", StringComparison.Ordinal))
                    name = name[..^"Statement".Length];
                reason = $"'{name}' statements require writes enabled on this connection.";
                return false;
        }
    }

    private static bool AllReadOnly(IEnumerable<TSqlStatement?> statements, out string? reason)
    {
        foreach (var statement in statements)
        {
            if (statement is null)
                continue;
            if (!IsReadOnly(statement, out reason))
                return false;
        }
        reason = null;
        return true;
    }
}
