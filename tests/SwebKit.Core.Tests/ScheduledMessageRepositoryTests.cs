using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.Core.Tests;

public class ScheduledMessageRepositoryTests
{
    private static ScheduledMessageEntry MakeEntry(Guid nsId, string entityPath, long seqNo = 1) =>
        new()
        {
            NamespaceId = nsId,
            EntityPath = entityPath,
            SequenceNumber = seqNo,
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddHours(1)
        };

    [Fact]
    public async Task AddAsync_AppearsInAll()
    {
        using var _ = new AppDataSandbox();
        var repo = new ScheduledMessageRepository();
        var entry = MakeEntry(Guid.NewGuid(), "orders");

        await repo.AddAsync(entry);

        Assert.Single(repo.All);
        Assert.Equal(entry.Id, repo.All[0].Id);
    }

    [Fact]
    public async Task RemoveAsync_EntryDisappearsFromAll()
    {
        using var _ = new AppDataSandbox();
        var repo = new ScheduledMessageRepository();
        var entry = MakeEntry(Guid.NewGuid(), "orders");
        await repo.AddAsync(entry);

        await repo.RemoveAsync(entry.Id);

        Assert.Empty(repo.All);
    }

    [Fact]
    public async Task RemoveAsync_UnknownId_DoesNotThrow()
    {
        using var _ = new AppDataSandbox();
        var repo = new ScheduledMessageRepository();

        await repo.RemoveAsync(Guid.NewGuid()); // should not throw
    }

    [Fact]
    public async Task GetByNamespace_ReturnsOnlyMatchingNamespace()
    {
        using var _ = new AppDataSandbox();
        var repo = new ScheduledMessageRepository();
        var ns1 = Guid.NewGuid();
        var ns2 = Guid.NewGuid();

        await repo.AddAsync(MakeEntry(ns1, "orders", 1));
        await repo.AddAsync(MakeEntry(ns1, "payments", 2));
        await repo.AddAsync(MakeEntry(ns2, "orders", 3));

        var result = repo.GetByNamespace(ns1);

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal(ns1, e.NamespaceId));
    }

    [Fact]
    public async Task GetByEntity_ReturnsOnlyMatchingEntityCaseInsensitive()
    {
        using var _ = new AppDataSandbox();
        var repo = new ScheduledMessageRepository();
        var ns = Guid.NewGuid();

        await repo.AddAsync(MakeEntry(ns, "Orders", 1));
        await repo.AddAsync(MakeEntry(ns, "orders", 2));
        await repo.AddAsync(MakeEntry(ns, "payments", 3));

        var result = repo.GetByEntity(ns, "ORDERS");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task PersistenceRoundtrip_LoadAsync_RestoresEntries()
    {
        using var _ = new AppDataSandbox();
        var ns = Guid.NewGuid();
        var entry = MakeEntry(ns, "orders");
        entry.MessageId = "msg-abc";
        entry.Subject = "test-subject";

        var writer = new ScheduledMessageRepository();
        await writer.AddAsync(entry);

        var reader = new ScheduledMessageRepository();
        await reader.LoadAsync();

        Assert.Single(reader.All);
        Assert.Equal(entry.Id, reader.All[0].Id);
        Assert.Equal("msg-abc", reader.All[0].MessageId);
        Assert.Equal("test-subject", reader.All[0].Subject);
        Assert.Equal(ns, reader.All[0].NamespaceId);
    }

    [Fact]
    public void NewInstance_StartsWithEmptyAll()
    {
        // Without calling LoadAsync, a new repository always starts with no entries.
        var repo = new ScheduledMessageRepository();
        Assert.Empty(repo.All);
    }
}
