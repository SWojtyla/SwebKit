using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public class SqlConfigTests
{
    [Fact]
    public void Validate_Throws_WhenNoConnections()
    {
        var config = new SqlConfig();
        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenConnectionHasNoServer()
    {
        var config = new SqlConfig { Connections = [new SqlConnectionEntry { DisplayName = "broken" }] };
        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void Validate_Passes_ForValidConnection()
    {
        var config = new SqlConfig { Connections = [new SqlConnectionEntry { Server = "s.database.windows.net" }] };
        config.Validate();
    }

    [Fact]
    public void ActiveConnection_PrefersActiveConnectionId()
    {
        var config = new SqlConfig
        {
            Connections =
            [
                new SqlConnectionEntry { Id = "a", Server = "s1" },
                new SqlConnectionEntry { Id = "b", Server = "s2" },
            ],
            ActiveConnectionId = "b",
        };

        Assert.Equal("b", config.ActiveConnection!.Id);
    }

    [Fact]
    public void ActiveConnection_FallsBackToFirst_WhenIdNotFound()
    {
        var config = new SqlConfig
        {
            Connections = [new SqlConnectionEntry { Id = "a", Server = "s1" }],
            ActiveConnectionId = "missing",
        };

        Assert.Equal("a", config.ActiveConnection!.Id);
    }

    [Fact]
    public void NewConnection_DefaultsToReadOnly()
    {
        Assert.False(new SqlConnectionEntry().AllowWrites);
        Assert.True(new SqlConnectionEntry().Active);
    }

    [Fact]
    public void ConnectionEntry_HasNoCredentialFields()
    {
        // Entra-only by design — a password/user field must never sneak onto the model.
        var propertyNames = typeof(SqlConnectionEntry).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(propertyNames, n => n.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, n => n.Contains("user", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, n => n.Contains("credential", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, n => n.Contains("connectionstring", StringComparison.OrdinalIgnoreCase));
    }

    // ── DeclaredObjects (access-awareness Phase 3b) ────────────────────────────

    [Fact]
    public void DeclaredObjects_DefaultsToEmpty()
    {
        Assert.Empty(new SqlConnectionEntry().DeclaredObjects);
    }

    [Fact]
    public void DeclaredObjects_NormalizesOnAssign_TrimsDedupesAndDropsMalformed()
    {
        var entry = new SqlConnectionEntry
        {
            Server = "s",
            DeclaredObjects =
            [
                " prd.v_orders ",            // edge whitespace trimmed
                "EXEC:prd.p_recalc",         // prefix canonicalized to lowercase exec:
                "prd.v_orders",              // case-insensitive duplicate of the first
                "bad name",                  // space — not a valid identifier
                "nodot",                     // unqualified
                "a.b.c",                     // three-part names aren't accepted
                "",                          // empty line
                "prd.[v_orders]",            // brackets aren't part of the grammar
            ],
        };

        Assert.Equal(["prd.v_orders", "exec:prd.p_recalc"], entry.DeclaredObjects);
    }

    [Theory]
    [InlineData("prd.v_orders", "prd", "v_orders", false)]
    [InlineData("exec:prd.p_recalc", "prd", "p_recalc", true)]
    [InlineData("EXEC:Sales.P1", "Sales", "P1", true)]
    public void SqlDeclaredObject_TryParse_ValidEntries(string raw, string schema, string name, bool isProcedure)
    {
        Assert.True(SqlDeclaredObject.TryParse(raw, out var parsed));
        Assert.Equal(schema, parsed.Schema);
        Assert.Equal(name, parsed.Name);
        Assert.Equal(isProcedure, parsed.IsProcedure);
        // Canonical form round-trips: stored form is lowercase-prefixed and unprefixed plainly.
        Assert.Equal(isProcedure ? $"exec:{schema}.{name}" : $"{schema}.{name}", parsed.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v_orders")]                 // unqualified
    [InlineData("prd.")]                     // missing object part
    [InlineData(".v_orders")]                // missing schema part
    [InlineData("a.b.c")]                    // three-part
    [InlineData("prd.v-audit")]              // '-' isn't an identifier character
    [InlineData("prd.9lives")]               // can't start with a digit
    [InlineData("[prd].[v_orders]")]         // bracket quoting isn't accepted input
    [InlineData("exec:")]                    // prefix with no object
    [InlineData("exec:prd")]                 // prefix but unqualified
    public void SqlDeclaredObject_TryParse_RejectsMalformed(string? raw)
    {
        Assert.False(SqlDeclaredObject.TryParse(raw, out _));
    }
}
