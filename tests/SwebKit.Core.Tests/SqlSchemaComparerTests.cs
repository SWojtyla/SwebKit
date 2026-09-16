using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class SqlSchemaComparerTests
{
    private static SqlSchemaModel Model(params (string Schema, SqlObjectInfo Object)[] objects)
    {
        var model = new SqlSchemaModel();
        foreach (var group in objects.GroupBy(o => o.Schema))
            model.Schemas.Add(new SqlSchemaGroup { Name = group.Key, Objects = group.Select(g => g.Object).ToList() });
        return model;
    }

    private static SqlObjectInfo Table(string name, params SqlColumnInfo[] columns) =>
        new() { Name = name, Kind = "table", Columns = [.. columns] };

    private static SqlColumnInfo Col(string name, string type, bool nullable = false, bool pk = false) =>
        new() { Name = name, DataType = type, IsNullable = nullable, IsPrimaryKey = pk };

    [Fact]
    public void Compare_IdenticalModels_ProduceEmptyReport()
    {
        var model = Model(("dbo", Table("t", Col("id", "int", pk: true))));
        var result = SqlSchemaComparer.Compare(model, model);

        Assert.Empty(result.OnlyInSource);
        Assert.Empty(result.OnlyInTarget);
        Assert.Empty(result.Differing);
    }

    [Fact]
    public void Compare_ReportsObjectsOnlyOnOneSide()
    {
        var source = Model(("dbo", Table("a")), ("dbo", Table("b")));
        var target = Model(("dbo", Table("a")), ("dbo", Table("c")));

        var result = SqlSchemaComparer.Compare(source, target);

        Assert.Equal("b", Assert.Single(result.OnlyInSource).Name);
        Assert.Equal("c", Assert.Single(result.OnlyInTarget).Name);
        Assert.Empty(result.Differing);
    }

    [Fact]
    public void Compare_SameNameDifferentSchema_AreDifferentObjects()
    {
        var source = Model(("dbo", Table("t")), ("sales", Table("t")));
        var target = Model(("dbo", Table("t")));

        var result = SqlSchemaComparer.Compare(source, target);

        var missing = Assert.Single(result.OnlyInSource);
        Assert.Equal("sales", missing.Schema);
    }

    [Fact]
    public void Compare_ReportsColumnTypeDiffs()
    {
        var source = Model(("dbo", Table("t", Col("id", "int", pk: true), Col("name", "nvarchar"))));
        var target = Model(("dbo", Table("t", Col("id", "int", pk: true), Col("name", "varchar"))));

        var result = SqlSchemaComparer.Compare(source, target);

        var differing = Assert.Single(result.Differing);
        var diff = Assert.Single(differing.Diffs);
        Assert.Equal("column name", diff.Property);
        Assert.Contains("nvarchar", diff.SourceValue);
        Assert.Contains("varchar", diff.TargetValue);
    }

    [Fact]
    public void Compare_ReportsMissingAndNewColumns()
    {
        var source = Model(("dbo", Table("t", Col("id", "int"), Col("gone", "int"))));
        var target = Model(("dbo", Table("t", Col("id", "int"), Col("added", "bit"))));

        var result = SqlSchemaComparer.Compare(source, target);

        var differing = Assert.Single(result.Differing);
        Assert.Equal(2, differing.Diffs.Count);
        Assert.Contains(differing.Diffs, d => d.Property == "column gone" && d.TargetValue == "(missing)");
        Assert.Contains(differing.Diffs, d => d.Property == "column added" && d.SourceValue == "(missing)");
    }

    [Fact]
    public void Compare_ReportsIndexAndForeignKeyDiffs()
    {
        var sourceObj = Table("t", Col("id", "int"));
        sourceObj.Indexes.Add(new SqlIndexInfo { Name = "IX_t", Columns = ["id"], IsUnique = true });
        var source = Model(("dbo", sourceObj));

        var targetObj = Table("t", Col("id", "int"));
        targetObj.ForeignKeys.Add(new SqlForeignKeyInfo { Name = "FK_t", ReferencedObject = "dbo.other" });
        var target = Model(("dbo", targetObj));

        var result = SqlSchemaComparer.Compare(source, target);

        var differing = Assert.Single(result.Differing);
        Assert.Contains(differing.Diffs, d => d.Property == "index IX_t" && d.TargetValue == "(missing)");
        Assert.Contains(differing.Diffs, d => d.Property == "foreign key FK_t" && d.SourceValue == "(missing)");
    }

    [Fact]
    public void Compare_IsCaseInsensitiveOnNames()
    {
        var source = Model(("dbo", Table("Customers", Col("Id", "int"))));
        var target = Model(("DBO", Table("customers", Col("id", "int"))));

        var result = SqlSchemaComparer.Compare(source, target);

        Assert.Empty(result.OnlyInSource);
        Assert.Empty(result.OnlyInTarget);
        Assert.Empty(result.Differing);
    }
}
