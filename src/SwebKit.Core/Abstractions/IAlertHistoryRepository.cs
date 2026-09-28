using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

/// <summary>Persistence for <see cref="AlertHistoryEntry"/> rows (monitoring-closed-loop
/// item 4a) — the durable incident record. The engine's 200-entry ring buffer is volatile and
/// loses both firings and recoveries on restart; this store is the permanent record the
/// alert-history views and ops summaries read.</summary>
public interface IAlertHistoryRepository
{
    /// <summary>All stored entries, newest <see cref="AlertHistoryEntry.At"/> first.</summary>
    Task<IReadOnlyList<AlertHistoryEntry>> GetAllAsync();
    /// <summary>Appends one entry, then trims the store to the repository's retention cap
    /// oldest-first. Entries are immutable once written — history is append-only by design.</summary>
    Task AppendAsync(AlertHistoryEntry entry);
}
