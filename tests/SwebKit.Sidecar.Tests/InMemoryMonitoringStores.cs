using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Sidecar.Tests;

/// <summary>In-memory <see cref="IMonitoringSilenceRepository"/> — tests construct the
/// evaluation engine directly and must not touch the file-backed store under
/// <see cref="SwebKit.Core.Configuration.AppDataPaths"/>.</summary>
internal sealed class InMemoryMonitoringSilenceRepository : IMonitoringSilenceRepository
{
    private readonly List<MonitoringSilence> _items = [];

    public Task<IReadOnlyList<MonitoringSilence>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<MonitoringSilence>>(_items.ToList());

    public Task<MonitoringSilence?> GetByIdAsync(string id) =>
        Task.FromResult(_items.FirstOrDefault(s => s.Id == id));

    public Task UpsertAsync(MonitoringSilence silence)
    {
        _items.RemoveAll(s => s.Id == silence.Id);
        _items.Add(silence);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id)
    {
        _items.RemoveAll(s => s.Id == id);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IAlertHistoryRepository"/> — captures appended entries so
/// engine tests can assert on durable history without file I/O.</summary>
internal sealed class InMemoryAlertHistoryRepository : IAlertHistoryRepository
{
    private readonly List<AlertHistoryEntry> _entries = [];

    public IReadOnlyList<AlertHistoryEntry> Entries => _entries;

    public Task<IReadOnlyList<AlertHistoryEntry>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<AlertHistoryEntry>>(_entries.ToList());

    public Task AppendAsync(AlertHistoryEntry entry)
    {
        _entries.Insert(0, entry);
        return Task.CompletedTask;
    }
}
