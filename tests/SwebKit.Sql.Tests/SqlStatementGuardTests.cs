using SwebKit.Core.Models;
using SwebKit.Sql;

namespace SwebKit.Sql.Tests;

/// <summary>
/// <see cref="SqlStatementGuard"/> is the single enforcement point for read-only mode, so it
/// gets adversarial coverage: allowed statements, every obvious write, statement kinds hidden
/// inside control-flow blocks, and fail-closed behaviour on unparseable input.
/// </summary>
public class SqlStatementGuardTests
{
    [Theory]
    [InlineData("SELECT * FROM dbo.Customers")]
    [InlineData("select top 10 id, name from [dbo].[orders] order by id desc")]
    [InlineData("WITH cte AS (SELECT id FROM t) SELECT * FROM cte")]
    [InlineData("PRINT 'hello'")]
    [InlineData("USE mydb")]
    [InlineData("SET NOCOUNT ON")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED")]
    [InlineData("DECLARE @x int; SET @x = 1; SELECT @x")]
    [InlineData("SELECT 1; SELECT 2; SELECT 3")]
    [InlineData("IF @x = 1 SELECT 1 ELSE SELECT 2")]
    [InlineData("BEGIN SELECT 1 END")]
    [InlineData("BEGIN TRY SELECT 1 END TRY BEGIN CATCH SELECT 2 END CATCH")]
    [InlineData("  -- leading comment\nSELECT 1")]
    [InlineData("/* block */ SELECT 1")]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluate_AllowsReadOnlyStatements(string sql)
    {
        var result = SqlStatementGuard.Evaluate(sql);
        Assert.True(result.Allowed, $"Expected allowed, got: {result.Reason}");
    }

    [Theory]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("UPDATE t SET x = 1")]
    [InlineData("DELETE FROM t")]
    [InlineData("DROP TABLE t")]
    [InlineData("CREATE TABLE t (id int)")]
    [InlineData("ALTER TABLE t ADD c int")]
    [InlineData("TRUNCATE TABLE t")]
    [InlineData("EXEC sp_who")]
    [InlineData("EXECUTE my_proc")]
    [InlineData("BEGIN TRANSACTION")]
    [InlineData("COMMIT")]
    [InlineData("GRANT SELECT ON t TO u")]
    [InlineData("MERGE INTO t USING s ON t.id = s.id WHEN MATCHED THEN UPDATE SET t.x = 1")]
    public void Evaluate_DeniesMutatingStatements(string sql)
    {
        var result = SqlStatementGuard.Evaluate(sql);
        Assert.False(result.Allowed);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Evaluate_DeniesSelectInto_WhichCreatesATable()
    {
        var result = SqlStatementGuard.Evaluate("SELECT * INTO backup FROM dbo.Customers");
        Assert.False(result.Allowed);
    }

    [Fact]
    public void Evaluate_DeniesBatch_WhenAnyStatementMutates()
    {
        var result = SqlStatementGuard.Evaluate("SELECT 1; DELETE FROM t; SELECT 2");
        Assert.False(result.Allowed);
    }

    [Fact]
    public void Evaluate_DeniesWriteInsideIfBlock()
    {
        var result = SqlStatementGuard.Evaluate("IF @x = 1 DELETE FROM t");
        Assert.False(result.Allowed);
    }

    [Fact]
    public void Evaluate_DeniesWriteInsideWhileLoop()
    {
        var result = SqlStatementGuard.Evaluate("WHILE 1 = 1 UPDATE t SET x = 1");
        Assert.False(result.Allowed);
    }

    [Fact]
    public void Evaluate_DeniesWriteInsideTryCatch()
    {
        var result = SqlStatementGuard.Evaluate("BEGIN TRY INSERT INTO t VALUES (1) END TRY BEGIN CATCH SELECT 1 END CATCH");
        Assert.False(result.Allowed);
    }

    [Fact]
    public void Evaluate_FailsClosed_OnUnparseableSql()
    {
        var result = SqlStatementGuard.Evaluate("SELEC T FRM !!! @@##");
        Assert.False(result.Allowed);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Evaluate_CannotBeTrickedByComments()
    {
        // Comments before a write must not make it look read-only.
        var result = SqlStatementGuard.Evaluate("-- SELECT is nice\nDELETE FROM t");
        Assert.False(result.Allowed);
    }

    [Fact]
    public void ThrowIfNotReadOnly_ThrowsGuardException_OnWrite()
    {
        Assert.Throws<SqlWriteGuardException>(() => SqlStatementGuard.ThrowIfNotReadOnly("DELETE FROM t"));
    }

    [Fact]
    public void ThrowIfNotReadOnly_DoesNotThrow_OnSelect()
    {
        SqlStatementGuard.ThrowIfNotReadOnly("SELECT 1");
    }
}
