using System.Text.Json;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public class StorageConfigTests
{
    // UT-C1: StorageConfig round-trips through System.Text.Json
    [Fact]
    public void StorageConfig_JsonRoundTrip_PreservesAllFields()
    {
        var config = new StorageConfig
        {
            AccountName = "myaccount",
            ConnectionStringRef = "storage:myaccount",
            UseAad = true
        };

        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<StorageConfig>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(config.AccountName, deserialized.AccountName);
        Assert.Equal(config.ConnectionStringRef, deserialized.ConnectionStringRef);
        Assert.Equal(config.UseAad, deserialized.UseAad);
    }

    // UT-C2: Missing ConnectionStringRef in JSON deserializes to null
    [Fact]
    public void StorageConfig_MissingConnectionStringRef_DeserializesToNull()
    {
        const string json = """{"AccountName":"myaccount","UseAad":true}""";

        var config = JsonSerializer.Deserialize<StorageConfig>(json);

        Assert.NotNull(config);
        Assert.Equal("myaccount", config.AccountName);
        Assert.True(config.UseAad);
        Assert.Null(config.ConnectionStringRef);
    }

    // UT-C3: ProjectEnvironment with Storage = null serializes and deserializes without error
    [Fact]
    public void ProjectEnvironment_NullStorage_RoundTripsCleanly()
    {
        var env = new ProjectEnvironment
        {
            Name = "dev",
            StorageAccounts = []
        };

        var json = JsonSerializer.Serialize(env);
        var deserialized = JsonSerializer.Deserialize<ProjectEnvironment>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("dev", deserialized.Name);
        Assert.Equal(0, deserialized.StorageAccounts.Count);
    }
}
