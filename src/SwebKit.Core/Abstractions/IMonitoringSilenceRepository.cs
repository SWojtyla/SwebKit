using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

/// <summary>Persistence for <see cref="MonitoringSilence"/> windows (monitoring-closed-loop
/// item 3). Silences are durable — a window created for tomorrow's deploy must still apply after
/// an app restart. Expired windows are retained until deleted (or trimmed by the caller) so the
/// Silences section can show history; matching at firing time is purely time-based.</summary>
public interface IMonitoringSilenceRepository
{
    /// <summary>All stored silences, soonest <see cref="MonitoringSilence.StartUtc"/> first.</summary>
    Task<IReadOnlyList<MonitoringSilence>> GetAllAsync();
    Task<MonitoringSilence?> GetByIdAsync(string id);
    /// <summary>Inserts or replaces the silence keyed on <see cref="MonitoringSilence.Id"/>.</summary>
    Task UpsertAsync(MonitoringSilence silence);
    Task DeleteAsync(string id);
}
