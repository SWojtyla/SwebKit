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
        if (!File.Exists(AppDataPaths.ScheduledMessagesJson)) return;

        try
        {
            var json = await File.ReadAllTextAsync(AppDataPaths.ScheduledMessagesJson);
            _entries = JsonSerializer.Deserialize<List<ScheduledMessageEntry>>(json, Options) ?? [];
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
        await File.WriteAllTextAsync(AppDataPaths.ScheduledMessagesJson, json);
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
}
