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
}
