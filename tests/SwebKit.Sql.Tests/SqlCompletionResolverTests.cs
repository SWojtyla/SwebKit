using SwebKit.Sql;

namespace SwebKit.Sql.Tests;

/// <summary>Phase 2.2 completion context: table/alias extraction and cursor-scope hints.</summary>
public class SqlCompletionResolverTests
{
    [Fact]
    public void Resolve_ExtractsTableReferencesAndAliases()
    {
        var ctx = SqlCompletionResolver.Resolve(
            "SELECT * FROM dbo.Customers c JOIN sales.Orders o ON c.id = o.customer_id",
            20);

        Assert.Equal(2, ctx.Tables.Count);
        var customers = Assert.Single(ctx.Tables, t => t.Name == "Customers");
        Assert.Equal("dbo", customers.Schema);
        Assert.Equal("c", customers.Alias);
        var orders = Assert.Single(ctx.Tables, t => t.Name == "Orders");
        Assert.Equal("sales", orders.Schema);
        Assert.Equal("o", orders.Alias);
    }

    [Fact]
    public void Resolve_ReportsColumnScope_WhenCursorFollowsAliasDot()
    {
        var sql = "SELECT c. FROM dbo.Customers c";
        var ctx = SqlCompletionResolver.Resolve(sql, "SELECT c.".Length);

        Assert.Equal("column", ctx.Kind);
        Assert.Equal("c", ctx.ColumnScope);
    }

    [Fact]
    public void Resolve_StripsBrackets_FromColumnScope()
    {
        var sql = "SELECT [dbo].[c]. FROM x";
        var ctx = SqlCompletionResolver.Resolve(sql, "SELECT [dbo].[c].".Length);

        Assert.Equal("column", ctx.Kind);
        Assert.Equal("c", ctx.ColumnScope);
    }

    [Fact]
    public void Resolve_ReportsTableKind_AfterFromKeyword()
    {
        var sql = "SELECT * FROM ";
        var ctx = SqlCompletionResolver.Resolve(sql, sql.Length);

        Assert.Equal("table", ctx.Kind);
        Assert.Null(ctx.ColumnScope);
    }

    [Fact]
    public void Resolve_ReportsTableKind_AfterJoinKeyword()
    {
        var sql = "SELECT * FROM a JOIN ";
        var ctx = SqlCompletionResolver.Resolve(sql, sql.Length);

        Assert.Equal("table", ctx.Kind);
    }

    [Fact]
    public void Resolve_ToleratesUnparseableInput_ReturnsWhatItRecovered()
    {
        var ctx = SqlCompletionResolver.Resolve("SELECT * FRM @@!!", 10);
        Assert.Equal("any", ctx.Kind);
        Assert.Empty(ctx.Tables);
    }

    [Fact]
    public void Resolve_EmptySql_ReturnsAnyKind()
    {
        var ctx = SqlCompletionResolver.Resolve("", 0);
        Assert.Equal("any", ctx.Kind);
        Assert.Empty(ctx.Tables);
    }

    [Fact]
    public void Resolve_CursorOutOfRange_ReturnsAnyKind()
    {
        var ctx = SqlCompletionResolver.Resolve("SELECT 1", 500);
        Assert.Equal("any", ctx.Kind);
    }
}
