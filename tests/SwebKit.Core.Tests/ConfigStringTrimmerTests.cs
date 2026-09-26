using System.Text.Json;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

/// <summary>
/// A pasted value with a stray trailing space breaks connection tests and client
/// construction while being invisible in every text box — the profile graph must
/// never store edge whitespace, no matter which writer put it there.
/// </summary>
public sealed class ConfigStringTrimmerTests
{
    [Fact]
    public void Trim_TrimsNestedStringsListsAndDictionaryValues()
    {
        var data = new ProfileData
        {
            Config = new AppConfig
            {
                Name = "  Default  ",
                KeyVaults =
                [
                    new KeyVaultEntry { Name = " prod-kv ", Url = " https://prod.vault.azure.net/ " },
                ],
            },
            ServiceBusNamespaces =
            [
                new ServiceBusNamespace
                {
                    Alias = " orders ",
                    FullyQualifiedNamespace = " orders.servicebus.windows.net ",
                    CredentialKey = " sw-secret:sb ",
                },
            ],
        };

        ConfigStringTrimmer.Trim(data);

        Assert.Equal("Default", data.Config.Name);
        Assert.Equal("prod-kv", data.Config.KeyVaults[0].Name);
        Assert.Equal("https://prod.vault.azure.net/", data.Config.KeyVaults[0].Url);
        Assert.Equal("orders", data.ServiceBusNamespaces[0].Alias);
        Assert.Equal("orders.servicebus.windows.net", data.ServiceBusNamespaces[0].FullyQualifiedNamespace);
        Assert.Equal("sw-secret:sb", data.ServiceBusNamespaces[0].CredentialKey);
    }

    [Fact]
    public void Trim_TrimsStringsInsideListsAndDictionaryValues()
    {
        var favorite = new FavoriteResource
        {
            Name = " pinned ",
            Snapshot = new WorkspaceSnapshot
            {
                Resource = new OperatorResourceReference
                {
                    Metadata = new Dictionary<string, string> { [" entityPath "] = " orders/inbound " },
                },
                RestoreState = new Dictionary<string, string> { ["mode"] = " active " },
            },
        };
        var data = new ProfileData { Config = new AppConfig { FavoriteResources = [favorite] } };

        ConfigStringTrimmer.Trim(data);

        Assert.Equal("pinned", favorite.Name);
        // Keys stay raw — rewriting them could collide two entries into one — but the
        // lookup still succeeds, proving the whitespace key was preserved.
        Assert.Equal("orders/inbound", favorite.Snapshot.Resource.Metadata[" entityPath "]);
        Assert.Equal("active", favorite.Snapshot.RestoreState["mode"]);
    }

    [Fact]
    public void Trim_HandlesNullsCyclesAndLeavesCleanValuesUntouched()
    {
        var node = new TrimProbe { Name = " ok ", Self = null! };
        node.Self = node; // a cycle must terminate, not recurse forever

        ConfigStringTrimmer.Trim(node);
        ConfigStringTrimmer.Trim(null);
        ConfigStringTrimmer.Trim("not an object");

        Assert.Equal("ok", node.Name);
    }

    [Fact]
    public void Trim_DoesNotReflectIntoFrameworkTypes()
    {
        var probe = new TrimProbe { Stamp = DateTimeOffset.UtcNow, Name = "x" };

        ConfigStringTrimmer.Trim(probe);

        Assert.Equal("x", probe.Name); // completes without touching DateTimeOffset internals
    }

    [Fact]
    public async Task SaveAsync_PersistsTrimmedValues()
    {
        using var appDataRoot = new TemporaryAppDataRoot();
        var repo = new ProfileRepository();
        await repo.LoadAsync();

        repo.AddServiceBusNamespace(new ServiceBusNamespace
        {
            Alias = "orders",
            FullyQualifiedNamespace = "orders.servicebus.windows.net ",
        });
        await repo.SaveAsync();

        var json = await File.ReadAllTextAsync(Path.Combine(appDataRoot.Root, "profiles.json"));
        using var doc = JsonDocument.Parse(json);
        var ns = doc.RootElement.GetProperty("serviceBusNamespaces")[0];
        Assert.Equal("orders.servicebus.windows.net", ns.GetProperty("fullyQualifiedNamespace").GetString());
    }

    [Fact]
    public async Task LoadAsync_TrimsAHandEditedProfilesJson()
    {
        using var appDataRoot = new TemporaryAppDataRoot();
        var profilePath = Path.Combine(appDataRoot.Root, "profiles.json");
        await File.WriteAllTextAsync(profilePath, """
            {
              "schemaVersion": 3,
              "config": { "name": " Default " },
              "serviceBusNamespaces": [
                { "id": "00000000-0000-0000-0000-000000000001", "alias": "orders", "fullyQualifiedNamespace": " sb://orders.net " }
              ]
            }
            """);

        var repo = new ProfileRepository();
        var result = await repo.LoadAsync();

        Assert.Equal(ProfileLoadStatus.Loaded, result.Status);
        Assert.Equal("Default", repo.Config.Name);
        Assert.Equal("sb://orders.net", repo.ServiceBusNamespaces[0].FullyQualifiedNamespace);
    }

    private sealed class TrimProbe
    {
        public string? Name { get; set; }
        public DateTimeOffset Stamp { get; set; }
        public TrimProbe Self { get; set; } = null!;
    }

    private sealed class TemporaryAppDataRoot : IDisposable
    {
        private readonly string? _previousRoot;

        public TemporaryAppDataRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            _previousRoot = Environment.GetEnvironmentVariable("SWEBKIT_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _previousRoot);
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
