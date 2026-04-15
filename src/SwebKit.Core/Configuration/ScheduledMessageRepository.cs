using System.Text.Json;
using SwebKit.Core.Models;

namespace SwebKit.Core.Configuration;

public class ScheduledMessageRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private List<ScheduledMessageEntry> _entries = [];

    public IReadOnlyList<ScheduledMessageEntry> All => _entries;

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!AppDataFileStore.Exists(AppDataPaths.ScheduledMessagesJson)) return;

        try
        {
            var loadResult = await AppDataFileStore.LoadAsync(AppDataPaths.ScheduledMessagesJson, DeserializeEntries);
            _entries = loadResult.Value;
        }
        catch
        {
            _entries = [];
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_entries, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.ScheduledMessagesJson, json);
    }

    public async Task AddAsync(ScheduledMessageEntry entry)
    {
        _entries.Add(entry);
        await SaveAsync();
    }

    public async Task RemoveAsync(Guid id)
    {
        _entries.RemoveAll(e => e.Id == id);
        await SaveAsync();
    }

    public IReadOnlyList<ScheduledMessageEntry> GetByNamespace(Guid namespaceId) =>
        _entries.Where(e => e.NamespaceId == namespaceId).ToList();

    public IReadOnlyList<ScheduledMessageEntry> GetByEntity(Guid namespaceId, string entityPath) =>
        _entries.Where(e => e.NamespaceId == namespaceId &&
                            string.Equals(e.EntityPath, entityPath, StringComparison.OrdinalIgnoreCase)).ToList();

    private static List<ScheduledMessageEntry> DeserializeEntries(string json) =>
        JsonSerializer.Deserialize<List<ScheduledMessageEntry>>(json, Options) ?? [];
}
