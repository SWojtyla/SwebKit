using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

public sealed class AlertRuleRepository : IAlertRuleRepository
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private List<MonitoringAlertRule> _rules = [];

    public async Task<IReadOnlyList<MonitoringAlertRule>> GetAllAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!File.Exists(AppDataPaths.MonitoringAlertsJson))
            return [];
        try
        {
            var json = await File.ReadAllTextAsync(AppDataPaths.MonitoringAlertsJson);
            _rules = JsonSerializer.Deserialize<List<MonitoringAlertRule>>(json, Options) ?? [];
            return _rules.AsReadOnly();
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveAllAsync(IReadOnlyList<MonitoringAlertRule> rules)
    {
        AppDataPaths.EnsureDirectoryExists();
        _rules = [.. rules];
        var json = JsonSerializer.Serialize(_rules, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.MonitoringAlertsJson, json);
    }

    public async Task<MonitoringAlertRule?> GetByIdAsync(string id)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(r => r.Id == id);
    }

    public async Task UpsertAsync(MonitoringAlertRule rule)
    {
        var all = (await GetAllAsync()).ToList();
        var idx = all.FindIndex(r => r.Id == rule.Id);
        if (idx >= 0) all[idx] = rule;
        else all.Add(rule);
        await SaveAllAsync(all);
    }

    public async Task DeleteAsync(string id)
    {
        var all = (await GetAllAsync()).ToList();
        all.RemoveAll(r => r.Id == id);
        await SaveAllAsync(all);
    }
}
