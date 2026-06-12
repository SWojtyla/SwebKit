using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Services;

public sealed class MonitoringMigrationService
{
    private readonly AppStateService _appState;
    private readonly IAlertRuleRepository _repository;
    private readonly ILogger<MonitoringMigrationService> _logger;

    public MonitoringMigrationService(
        AppStateService appState,
        IAlertRuleRepository repository,
        ILogger<MonitoringMigrationService> logger)
    {
        _appState = appState;
        _repository = repository;
        _logger = logger;
        _appState.Initialized += OnAppInitialized;
    }

    private void OnAppInitialized() => _ = RunMigrationAsync();

    private async Task RunMigrationAsync()
    {
        // Only migrate when no monitoring-alerts.json exists yet
        if (File.Exists(AppDataPaths.MonitoringAlertsJson))
            return;

        var namespaces = _appState.Config.AksConfig?.MonitoredNamespaces;
        if (namespaces is null || namespaces.Count == 0)
            return;

        var rules = namespaces.Select(ns => new MonitoringAlertRule
        {
            Source = AlertRuleSource.AksPodHealth,
            Name = $"Pod health \u2014 {ns}",
            AksPodParams = new AksPodAlertParams { Namespace = ns },
            IntervalSeconds = 60,
            CooldownMinutes = 5,
            Severity = AlertSeverity.Warning,
        }).ToList();

        await _repository.SaveAllAsync(rules);
        _logger.LogInformation("Migrated {Count} AKS namespace(s) to monitoring alert rules.", rules.Count);
    }
}
