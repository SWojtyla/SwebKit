using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IAlertRuleRepository
{
    Task<IReadOnlyList<MonitoringAlertRule>> GetAllAsync();
    Task SaveAllAsync(IReadOnlyList<MonitoringAlertRule> rules);
    Task<MonitoringAlertRule?> GetByIdAsync(string id);
    Task UpsertAsync(MonitoringAlertRule rule);
    Task DeleteAsync(string id);
}
