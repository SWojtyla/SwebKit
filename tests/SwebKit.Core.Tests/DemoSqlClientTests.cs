using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Security;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>The demo client powers demo mode's two-variant compare scenario end-to-end.</summary>
public class DemoSqlClientTests
{
    private static SqlConnectionEntry Entry() =>
        new() { Id = "demo-sql", DisplayName = "demo", Server = "demo.database.windows.net", Database = "orders" };

    [Fact]
    public async Task TestConnection_AlwaysTrue()
    {
        await using var client = new DemoSqlClient(Entry());
        Assert.True(await client.TestConnectionAsync());
    }

    [Fact]
    public async Task GetSchema_ContainsExpectedTablesAndColumns()
    {
        await using var client = new DemoSqlClient(Entry());
        var schema = await client.GetSchemaAsync("orders");

        var dbo = Assert.Single(schema.Schemas, s => s.Name == "dbo");
        var customers = Assert.Single(dbo.Objects, o => o.Name == "customers");
        Assert.Contains(customers.Columns, c => c.Name == "id" && c.IsPrimaryKey);
        Assert.Contains(dbo.Objects, o => o.Name == "orders");
        var sales = Assert.Single(schema.Schemas, s => s.Name == "sales");
        Assert.Contains(sales.Objects, o => o.Name == "invoices");
    }

    [Fact]
    public async Task ExecuteQuery_ReturnsRows_ForKnownTables()
    {
        await using var client = new DemoSqlClient(Entry());
        var result = await client.ExecuteQueryAsync("SELECT * FROM dbo.customers", "orders", 500, allowWrites: false);

        Assert.Equal(8, result.Rows.Count);
        Assert.Contains(result.Columns, c => c.Name == "name");
    }

    [Fact]
    public async Task ExecuteQuery_RespectsMaxRows()
    {
        await using var client = new DemoSqlClient(Entry());
        var result = await client.ExecuteQueryAsync("SELECT * FROM dbo.orders", "orders", 5, allowWrites: false);

        Assert.Equal(5, result.Rows.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task ExecuteQuery_ThrowsGuard_OnWrite_WhenWritesDisabled()
    {
        await using var client = new DemoSqlClient(Entry());
        await Assert.ThrowsAsync<SqlWriteGuardException>(
            () => client.ExecuteQueryAsync("DELETE FROM customers", "orders", 500, allowWrites: false));
    }

    [Fact]
    public async Task ExecuteQuery_AllowsWrite_WhenWritesEnabled()
    {
        await using var client = new DemoSqlClient(Entry());
        // Demo client doesn't actually mutate — it must simply not reject the statement.
        var result = await client.ExecuteQueryAsync("UPDATE customers SET name = 'x'", "orders", 500, allowWrites: true);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ExecuteQuery_RejectsWriteHiddenBehindComment()
    {
        await using var client = new DemoSqlClient(Entry());
        await Assert.ThrowsAsync<SqlWriteGuardException>(
            () => client.ExecuteQueryAsync("-- SELECT looks nice\nDELETE FROM customers", "orders", 500, allowWrites: false));
    }

    [Fact]
    public async Task GetTableRows_FiltersAndSorts()
    {
        await using var client = new DemoSqlClient(Entry());
        var result = await client.GetTableRowsAsync("dbo", "customers", "orders",
            filterColumn: "city", filterText: "brussels", orderByColumn: null, descending: false,
            skip: 0, take: 100);

        Assert.Single(result.Rows);
        Assert.Equal("Brussels", result.Rows[0]["city"]);
    }

    [Fact]
    public async Task CompareData_VariantDifference_ShowsChangedRow()
    {
        await using var source = new DemoSqlClient(Entry(), variant: 0);
        await using var target = new DemoSqlClient(Entry(), variant: 1);

        var result = await source.CompareDataAsync(target, "dbo", "products", ["id"], "orders", "orders", 500);

        // Variant 1 changes the price of the third product.
        Assert.Equal(1, result.TotalChanged);
        var changed = Assert.Single(result.Changed);
        Assert.Contains(changed.Diffs, d => d.Column == "price");
        // Variant 0 has a "stock" column the variant 1 rows lack (or vice versa) — a warning is emitted.
        Assert.NotEmpty(result.SchemaWarnings);
    }

    [Fact]
    public async Task CompareSchema_VariantDifference_ShowsObjectDiffs()
    {
        await using var source = new DemoSqlClient(Entry(), variant: 0);
        await using var target = new DemoSqlClient(Entry(), variant: 1);

        var result = await source.CompareSchemaAsync(target, "orders", "orders");

        // Variant 1 adds sales.returns (only in target) and drops products.stock (differing).
        Assert.Contains(result.OnlyInTarget, o => o.Name == "returns");
        Assert.Contains(result.Differing, o => o.Name == "products");
    }

    // ── Declared objects (access-awareness Phase 3b) ───────────────────────────

    [Fact]
    public async Task GetSchema_RestrictedVariant_MergesDeclaredObjects()
    {
        var entry = Entry();
        entry.DeclaredObjects = ["prd.v_orders", "exec:prd.p_recalc"];
        await using var client = new DemoSqlClient(entry, variant: 2);

        var schema = await client.GetSchemaAsync("orders");

        // The catalog is empty for the hidden variant — declared objects are the whole tree.
        var prd = Assert.Single(schema.Schemas);
        Assert.Equal("prd", prd.Name);
        var view = Assert.Single(prd.Objects, o => o.Name == "v_orders");
        Assert.True(view.IsDeclared);
        Assert.Empty(view.Columns); // columns load lazily via GetObjectColumnsAsync
        var proc = Assert.Single(prd.Objects, o => o.Name == "p_recalc");
        Assert.Equal("proc", proc.Kind);
        Assert.True(proc.IsDeclared);
    }

    [Fact]
    public async Task GetSchema_DeclaredObject_NeverShadowsCatalogDiscovery()
    {
        var entry = Entry();
        entry.DeclaredObjects = ["dbo.customers", "audit.v_log"];
        await using var client = new DemoSqlClient(entry, variant: 0);

        var schema = await client.GetSchemaAsync("orders");

        var dbo = Assert.Single(schema.Schemas, s => s.Name == "dbo");
        var customers = Assert.Single(dbo.Objects, o => o.Name == "customers");
        Assert.False(customers.IsDeclared); // catalog discovery wins over declaration
        Assert.NotEmpty(customers.Columns);

        var audit = Assert.Single(schema.Schemas, s => s.Name == "audit");
        var log = Assert.Single(audit.Objects);
        Assert.True(log.IsDeclared);
    }

    [Fact]
    public async Task GetObjectColumns_RestrictedVariant_ServesGranted_AndDeniesRest()
    {
        await using var client = new DemoSqlClient(Entry(), variant: 2);

        var columns = await client.GetObjectColumnsAsync("prd", "v_orders", "orders");
        Assert.Contains(columns, c => c.Name == "id" && c.IsPrimaryKey);

        // prd.v_audit carries an object-level DENY in the demo scenario — the exception
        // must classify as access-denied the same way a real SqlException 229 does.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetObjectColumnsAsync("prd", "v_audit", "orders"));
        Assert.True(AccessAdvisor.IsAccessDenied(ex));
    }

    [Fact]
    public async Task GetObjectColumns_KnownCatalogObject_ReturnsItsColumns()
    {
        await using var client = new DemoSqlClient(Entry(), variant: 0);

        var columns = await client.GetObjectColumnsAsync("dbo", "customers", "orders");

        Assert.Contains(columns, c => c.Name == "email" && c.IsNullable);
    }

    [Fact]
    public void ApplyPermissionAnalysis_StillHidden_WhenTreeHoldsOnlyDeclarations()
    {
        var model = new SqlSchemaModel();
        model.MergeDeclaredObjects(["prd.v_orders", "exec:prd.p_recalc"]);

        model.ApplyPermissionAnalysis(["EXECUTE", "SELECT"]);

        // A tree built entirely from declarations must still report hidden metadata —
        // otherwise the UI can't tell a partial tree from a full catalog browse.
        Assert.True(model.MetadataHidden);
        Assert.Single(model.Schemas);
    }

    [Fact]
    public void MergeDeclaredObjects_IgnoresMalformedEntries()
    {
        var model = new SqlSchemaModel();
        model.MergeDeclaredObjects(["prd.v_orders", "not-an-object", "a.b.c", " "]);

        var prd = Assert.Single(model.Schemas);
        Assert.Single(prd.Objects);
    }

    [Fact]
    public async Task CheckHealth_VariantOne_ReportsBlockingSessions()
    {
        await using var client = new DemoSqlClient(Entry(), variant: 1);
        var health = await client.CheckHealthAsync("orders");

        Assert.True(health.Connected);
        Assert.Equal(2, health.BlockingSessionCount);
        Assert.NotEmpty(health.Notes);
    }
}
