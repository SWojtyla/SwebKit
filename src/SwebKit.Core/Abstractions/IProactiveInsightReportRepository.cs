using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

/// <summary>Persistence for completed background proactive-investigation reports
/// (ai-insight-reports). Unlike the in-memory <c>AgentSessionStore</c> the seeded chat session
/// lives in, reports here survive restarts — they are the permanent record the Monitoring "AI
/// Reports" tab reads.</summary>
public interface IProactiveInsightReportRepository
{
    /// <summary>All stored reports, newest <see cref="ProactiveInsightReport.FiredAt"/> first.</summary>
    Task<IReadOnlyList<ProactiveInsightReport>> GetAllAsync();
    Task<ProactiveInsightReport?> GetByIdAsync(string id);
    /// <summary>Inserts or replaces the report keyed on <see cref="ProactiveInsightReport.Id"/>,
    /// then trims the store to <see cref="ProactiveInsightReportRepository.MaxReports"/>.</summary>
    Task UpsertAsync(ProactiveInsightReport report);
    Task DeleteAsync(string id);
}
