using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Exercises <see cref="SidecarCredentialStore"/> against the real OS-backed keychain available in
/// this environment (Windows Credential Manager / Secret Service / in-memory fallback if neither is
/// reachable — the class handles all three transparently). Uses a uniquely-named, clearly-tagged
/// test key and always deletes it in a <c>finally</c> block so no residue is left in the developer's
/// real credential store regardless of assertion outcome.
/// </summary>
public class SidecarCredentialStoreTests
{
    private static string NewTestKey() => $"SwebKit.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void SaveThenGet_RoundTripsTheSecret()
    {
        var store = new SidecarCredentialStore(null);
        var key = NewTestKey();

        try
        {
            store.Save(key, "round-trip-secret");

            Assert.Equal("round-trip-secret", store.Get(key));
        }
        finally
        {
            store.Delete(key);
        }
    }

    [Fact]
    public void Get_UnknownKey_ReturnsNull()
    {
        var store = new SidecarCredentialStore(null);

        Assert.Null(store.Get(NewTestKey()));
    }

    [Fact]
    public void Delete_RemovesTheSecret_SoSubsequentGetReturnsNull()
    {
        var store = new SidecarCredentialStore(null);
        var key = NewTestKey();
        store.Save(key, "to-be-deleted");

        store.Delete(key);

        Assert.Null(store.Get(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Save_BlankKey_IsANoOp(string? blankKey)
    {
        var store = new SidecarCredentialStore(null);

        // Must not throw, and must not somehow store anything retrievable under the blank key.
        store.Save(blankKey!, "value");

        Assert.Null(store.Get(blankKey!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Get_BlankKey_ReturnsNull_WithoutThrowing(string? blankKey)
    {
        var store = new SidecarCredentialStore(null);

        Assert.Null(store.Get(blankKey!));
    }

    [Fact]
    public void ListKeys_NoPrefix_IncludesSavedKey()
    {
        var store = new SidecarCredentialStore(null);
        var key = NewTestKey();

        try
        {
            store.Save(key, "some-value");

            Assert.Contains(key, store.ListKeys());
        }
        finally
        {
            store.Delete(key);
        }
    }

    [Fact]
    public void ListKeys_WithPrefix_FiltersToMatchingKeys()
    {
        var store = new SidecarCredentialStore(null);
        var matchingKey = $"SwebKit.Tests.Prefixed.{Guid.NewGuid():N}";
        var nonMatchingKey = NewTestKey();

        try
        {
            store.Save(matchingKey, "v1");
            store.Save(nonMatchingKey, "v2");

            var result = store.ListKeys("SwebKit.Tests.Prefixed.");

            Assert.Contains(matchingKey, result);
            Assert.DoesNotContain(nonMatchingKey, result);
        }
        finally
        {
            store.Delete(matchingKey);
            store.Delete(nonMatchingKey);
        }
    }
}
