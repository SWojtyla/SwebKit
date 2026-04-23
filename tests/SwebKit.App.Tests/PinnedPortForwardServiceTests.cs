using SwebKit.App.Services;
using SwebKit.Core.Configuration;

namespace SwebKit.App.Tests;

[Collection("AppDataSerial")]
public sealed class PinnedPortForwardServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (PinnedPortForwardService service, UserSettingsRepository repo) Build()
    {
        var repo = new UserSettingsRepository();
        return (new PinnedPortForwardService(repo), repo);
    }

    private static PinnedPortForwardEntry MakeEntry(string label, int remotePort = 8080, int localPort = 8080, int seq = 0) =>
        new(label, "default", $"app={label}", remotePort, localPort,
            DateTimeOffset.UtcNow.AddSeconds(seq));

    // ── Cap enforcement ───────────────────────────────────────────────────────

    [Fact]
    public async Task AddPinAsync_20Entries_AllRetained()
    {
        using var _ = new AppDataSandbox();
        var (service, _) = Build();
        const string ctx = "test-context";

        for (var i = 0; i < 20; i++)
            await service.AddPinAsync(ctx, MakeEntry($"svc-{i}", 8080 + i, 8080 + i, i));

        Assert.Equal(20, service.GetPins(ctx).Count);
    }

    [Fact]
    public async Task AddPinAsync_21stEntry_OldestIsEvicted()
    {
        using var _ = new AppDataSandbox();
        var (service, _) = Build();
        const string ctx = "test-context";

        // Add 20 unique entries — unique selectors so no dedup fires
        for (var i = 0; i < 20; i++)
            await service.AddPinAsync(ctx, MakeEntry($"svc-{i}", 9000 + i, 9000 + i, i));

        var firstLabel = service.GetPins(ctx)[0].Label; // "svc-0"

        // Add 21st
        await service.AddPinAsync(ctx, MakeEntry("svc-20", 9020, 9020, 20));

        var pins = service.GetPins(ctx);
        Assert.Equal(20, pins.Count);
        Assert.DoesNotContain(pins, p => p.Label == firstLabel);
        Assert.Contains(pins, p => p.Label == "svc-20");
    }

    // ── Duplicate detection ───────────────────────────────────────────────────

    [Fact]
    public async Task AddPinAsync_DuplicateEntry_NoDuplicateAdded()
    {
        using var _ = new AppDataSandbox();
        var (service, _) = Build();
        const string ctx = "test-context";

        var entry = MakeEntry("svc-a", 8080, 8080);
        await service.AddPinAsync(ctx, entry);
        await service.AddPinAsync(ctx, entry);

        Assert.Single(service.GetPins(ctx));
    }

    [Fact]
    public async Task AddPinAsync_SameSelectorAndPorts_ReplacesOldEntry()
    {
        using var _ = new AppDataSandbox();
        var (service, _) = Build();
        const string ctx = "test-context";

        var first = new PinnedPortForwardEntry("first-label", "ns", "app=myapp", 8080, 8080, DateTimeOffset.UtcNow);
        var second = new PinnedPortForwardEntry("second-label", "ns", "app=myapp", 8080, 8080, DateTimeOffset.UtcNow.AddSeconds(1));

        await service.AddPinAsync(ctx, first);
        await service.AddPinAsync(ctx, second);

        var pins = service.GetPins(ctx);
        Assert.Single(pins);
        Assert.Equal("second-label", pins[0].Label);
    }

    // ── Removal ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemovePinAsync_ExistingEntry_IsRemoved()
    {
        using var _ = new AppDataSandbox();
        var (service, _) = Build();
        const string ctx = "test-context";

        var keep = MakeEntry("keep", 8081, 8081);
        var remove = MakeEntry("remove", 8082, 8082);

        await service.AddPinAsync(ctx, keep);
        await service.AddPinAsync(ctx, remove);
        await service.RemovePinAsync(ctx, remove);

        var pins = service.GetPins(ctx);
        Assert.Single(pins);
        Assert.Equal("keep", pins[0].Label);
    }

    [Fact]
    public async Task RemovePinAsync_NonExistentEntry_DoesNotThrow()
    {
        using var _ = new AppDataSandbox();
        var (service, _) = Build();
        const string ctx = "test-context";

        await service.AddPinAsync(ctx, MakeEntry("existing"));
        var ghost = MakeEntry("ghost", 9999, 9999);

        // Should not throw
        await service.RemovePinAsync(ctx, ghost);

        Assert.Single(service.GetPins(ctx));
    }

    // ── Sandbox ───────────────────────────────────────────────────────────────

    private sealed class AppDataSandbox : IDisposable
    {
        private readonly string? _original;
        private readonly string _temp;

        public AppDataSandbox()
        {
            _original = Environment.GetEnvironmentVariable("APPDATA");
            _temp = Path.Combine(Path.GetTempPath(), "SwebKit.AppTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
            Environment.SetEnvironmentVariable("APPDATA", _temp);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("APPDATA", _original);
            if (Directory.Exists(_temp))
                Directory.Delete(_temp, recursive: true);
        }
    }
}
