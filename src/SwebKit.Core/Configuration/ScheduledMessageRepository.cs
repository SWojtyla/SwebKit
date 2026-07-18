using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Models;

namespace SwebKit.Core.Configuration;

public class ScheduledMessageRepository(ILogger<ScheduledMessageRepository>? logger = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private List<ScheduledMessageEntry> _entries = [];

    public IReadOnlyList<ScheduledMessageEntry> All => _entries;

    public IReadOnlyList<ScheduledMessageEntry> GetEntries() => _entries;

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!AppDataFileStore.Exists(AppDataPaths.ScheduledMessagesJson)) return;

        try
        {
            var loadResult = await AppDataFileStore.LoadAsync(AppDataPaths.ScheduledMessagesJson, DeserializeEntries).ConfigureAwait(false);
            _entries = loadResult.Value;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load scheduled messages from '{File}'; falling back to an empty list.", AppDataPaths.ScheduledMessagesJson);
            _entries = [];
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_entries, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.ScheduledMessagesJson, json).ConfigureAwait(false);
    }

    public void ReplaceEntries(IEnumerable<ScheduledMessageEntry>? entries)
    {
        _entries = entries?.ToList() ?? [];
    }

    public async Task ImportAsync(IEnumerable<ScheduledMessageEntry>? entries)
    {
        ReplaceEntries(entries);
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task AddAsync(ScheduledMessageEntry entry)
    {
        _entries.Add(entry);
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id)
    {
        _entries.RemoveAll(e => e.Id == id);
        await SaveAsync().ConfigureAwait(false);
    }

    public IReadOnlyList<ScheduledMessageEntry> GetByNamespace(Guid namespaceId) =>
        _entries.Where(e => e.NamespaceId == namespaceId).ToList();

    public IReadOnlyList<ScheduledMessageEntry> GetByEntity(Guid namespaceId, string entityPath) =>
        _entries.Where(e => e.NamespaceId == namespaceId &&
                            string.Equals(e.EntityPath, entityPath, StringComparison.OrdinalIgnoreCase)).ToList();

    private static List<ScheduledMessageEntry> DeserializeEntries(string json) =>
        JsonSerializer.Deserialize<List<ScheduledMessageEntry>>(json, Options) ?? [];
}
