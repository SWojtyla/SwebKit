using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class SqlDataComparerTests
{
    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Key, c => c.Value);

    [Fact]
    public void Compare_IdenticalSets_ProducesNoDiffs()
    {
        var rows = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "a")), Row(("id", 2), ("name", "b")) };

        var result = SqlDataComparer.Compare(rows, [.. rows.Select(r => new Dictionary<string, object?>(r))], ["id"], 500);

        Assert.Equal(0, result.TotalOnlyInSource);
        Assert.Equal(0, result.TotalOnlyInTarget);
        Assert.Equal(0, result.TotalChanged);
        Assert.False(result.Truncated);
        Assert.Empty(result.SchemaWarnings);
    }

    [Fact]
    public void Compare_ReportsOnlyInSourceAndTarget()
    {
        var source = new List<Dictionary<string, object?>> { Row(("id", 1)), Row(("id", 2)) };
        var target = new List<Dictionary<string, object?>> { Row(("id", 1)), Row(("id", 3)) };

        var result = SqlDataComparer.Compare(source, target, ["id"], 500);

        Assert.Equal(1, result.TotalOnlyInSource);
        Assert.Equal(1, result.TotalOnlyInTarget);
        Assert.Equal(2, result.OnlyInSource[0]["id"]);
        Assert.Equal(3, result.OnlyInTarget[0]["id"]);
    }

    [Fact]
    public void Compare_ReportsChangedRows_WithColumnDiffs()
    {
        var source = new List<Dictionary<string, object?>> { Row(("id", 1), ("price", 10.5m)) };
        var target = new List<Dictionary<string, object?>> { Row(("id", 1), ("price", 99.99m)) };

        var result = SqlDataComparer.Compare(source, target, ["id"], 500);

        Assert.Equal(1, result.TotalChanged);
        var changed = Assert.Single(result.Changed);
        Assert.Equal(1, changed.Key["id"]);
        var diff = Assert.Single(changed.Diffs);
        Assert.Equal("price", diff.Column);
        Assert.Equal(10.5m, diff.SourceValue);
        Assert.Equal(99.99m, diff.TargetValue);
    }

    [Fact]
    public void Compare_IgnoresKeyColumns_WhenDiffingValues()
    {
        // Same key expressed differently (int vs string) should still match as the same row,
        // and the key column itself must not appear as a diff.
        var source = new List<Dictionary<string, object?>> { Row(("id", 1), ("v", "same")) };
        var target = new List<Dictionary<string, object?>> { Row(("id", "1"), ("v", "same")) };

        var result = SqlDataComparer.Compare(source, target, ["id"], 500);

        Assert.Equal(0, result.TotalChanged);
        Assert.Equal(0, result.TotalOnlyInSource);
        Assert.Equal(0, result.TotalOnlyInTarget);
    }

    [Fact]
    public void Compare_SupportsCompositeKeys()
    {
        var source = new List<Dictionary<string, object?>> { Row(("a", 1), ("b", 2), ("v", "x")) };
        var target = new List<Dictionary<string, object?>> { Row(("a", 1), ("b", 2), ("v", "y")) };

        var result = SqlDataComparer.Compare(source, target, ["a", "b"], 500);

        Assert.Equal(1, result.TotalChanged);
    }

    [Fact]
    public void Compare_WarnsAboutOneSidedColumns_AndExcludesThem()
    {
        var source = new List<Dictionary<string, object?>> { Row(("id", 1), ("extra_src", "x")) };
        var target = new List<Dictionary<string, object?>> { Row(("id", 1), ("extra_tgt", "y")) };

        var result = SqlDataComparer.Compare(source, target, ["id"], 500);

        Assert.Equal(2, result.SchemaWarnings.Count);
        Assert.Equal(0, result.TotalChanged); // one-sided columns are excluded, not diffs
    }

    [Fact]
    public void Compare_CapsRows_ButKeepsExactCounts()
    {
        var source = Enumerable.Range(0, 10).Select(i => Row(("id", i))).ToList();
        var result = SqlDataComparer.Compare(source, [], ["id"], 3);

        Assert.Equal(10, result.TotalOnlyInSource);
        Assert.Equal(3, result.OnlyInSource.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Compare_EmptyKeyColumns_ReturnsEmptyResult()
    {
        var result = SqlDataComparer.Compare([Row(("id", 1))], [Row(("id", 1))], [], 500);
        Assert.Equal(0, result.TotalChanged);
        Assert.Equal(0, result.TotalOnlyInSource);
    }

    [Fact]
    public void Canonicalize_DistinguishesNullFromStringNull_AndFormatsBytes()
    {
        Assert.NotEqual(SqlDataComparer.Canonicalize(null), SqlDataComparer.Canonicalize("null"));
        Assert.Equal("0x0AFF", SqlDataComparer.Canonicalize(new byte[] { 0x0A, 0xFF }));
        Assert.Equal("1", SqlDataComparer.Canonicalize(true));
        Assert.Equal("1.5", SqlDataComparer.Canonicalize(1.5m));
    }
}
