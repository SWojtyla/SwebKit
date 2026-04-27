using CommunityToolkit.Mvvm.ComponentModel;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.WinUI.ViewModels.Settings;

public sealed partial class IncidentTimelineMappingEditorItem : ObservableObject
{
    [ObservableProperty]
    public partial string Namespace { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IncidentWorkloadKind WorkloadKind { get; set; } = IncidentWorkloadKind.Deployment;

    [ObservableProperty]
    public partial string WorkloadName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ObservabilityResourceId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CloudRoleNamesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OperationNamesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ServiceBusEntitiesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DevOpsPipelinesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DevOpsEnvironmentNamesText { get; set; } = string.Empty;

    public string Title => string.IsNullOrWhiteSpace(DisplayName)
        ? (string.IsNullOrWhiteSpace(WorkloadName) ? "New workload mapping" : WorkloadName.Trim())
        : DisplayName.Trim();

    public string ScopeLabel => string.Join(
        " • ",
        string.IsNullOrWhiteSpace(Namespace) ? "Namespace not set" : Namespace.Trim(),
        WorkloadKind.ToString(),
        string.IsNullOrWhiteSpace(WorkloadName) ? "Workload not set" : WorkloadName.Trim());

    public bool Matches(string? namespaceName, IncidentWorkloadKind workloadKind, string? workloadName) =>
        string.Equals(NormalizeOptional(Namespace), NormalizeOptional(namespaceName), StringComparison.OrdinalIgnoreCase)
        && WorkloadKind == workloadKind
        && string.Equals(NormalizeOptional(WorkloadName), NormalizeOptional(workloadName), StringComparison.OrdinalIgnoreCase);

    public static IncidentTimelineMappingEditorItem CreateBlank() => new();

    public static IncidentTimelineMappingEditorItem CreateSuggested(string? namespaceName, IncidentWorkloadKind workloadKind, string? workloadName) =>
        new()
        {
            Namespace = NormalizeOptional(namespaceName) ?? string.Empty,
            WorkloadKind = workloadKind,
            WorkloadName = NormalizeOptional(workloadName) ?? string.Empty,
            DisplayName = NormalizeOptional(workloadName) ?? string.Empty,
        };

    public static IncidentTimelineMappingEditorItem FromDomain(IncidentTimelineWorkloadMapping mapping) =>
        new()
        {
            Namespace = mapping.Namespace,
            WorkloadKind = mapping.WorkloadKind,
            WorkloadName = mapping.WorkloadName,
            DisplayName = mapping.DisplayName ?? string.Empty,
            ObservabilityResourceId = mapping.Observability?.ResourceId ?? string.Empty,
            CloudRoleNamesText = string.Join(Environment.NewLine, mapping.Observability?.CloudRoleNames ?? []),
            OperationNamesText = string.Join(Environment.NewLine, mapping.Observability?.OperationNames ?? []),
            ServiceBusEntitiesText = string.Join(Environment.NewLine, mapping.ServiceBusEntities.Select(FormatServiceBusEntityLine)),
            DevOpsPipelinesText = string.Join(Environment.NewLine, mapping.DevOps?.Pipelines.Select(FormatPipelineLine) ?? []),
            DevOpsEnvironmentNamesText = string.Join(Environment.NewLine, mapping.DevOps?.EnvironmentNames ?? []),
        };

    public IncidentTimelineWorkloadMapping ToDomain()
    {
        var observability = BuildObservabilityMapping();
        var devOps = BuildDevOpsMapping();

        return new IncidentTimelineWorkloadMapping
        {
            Namespace = NormalizeOptional(Namespace) ?? string.Empty,
            WorkloadKind = WorkloadKind,
            WorkloadName = NormalizeOptional(WorkloadName) ?? string.Empty,
            DisplayName = NormalizeOptional(DisplayName),
            Observability = observability,
            ServiceBusEntities = ParseServiceBusEntities(ServiceBusEntitiesText),
            DevOps = devOps,
        };
    }

    partial void OnNamespaceChanged(string value) => NotifyLabelsChanged();

    partial void OnWorkloadKindChanged(IncidentWorkloadKind value) => NotifyLabelsChanged();

    partial void OnWorkloadNameChanged(string value) => NotifyLabelsChanged();

    partial void OnDisplayNameChanged(string value) => NotifyLabelsChanged();

    private void NotifyLabelsChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ScopeLabel));
    }

    private IncidentTimelineObservabilityMapping? BuildObservabilityMapping()
    {
        var resourceId = NormalizeOptional(ObservabilityResourceId);
        var cloudRoleNames = NormalizeLines(CloudRoleNamesText);
        var operationNames = NormalizeLines(OperationNamesText);

        return resourceId is null && cloudRoleNames.Count == 0 && operationNames.Count == 0
            ? null
            : new IncidentTimelineObservabilityMapping
            {
                ResourceId = resourceId,
                CloudRoleNames = cloudRoleNames,
                OperationNames = operationNames,
            };
    }

    private IncidentTimelineDevOpsMapping? BuildDevOpsMapping()
    {
        var pipelines = ParsePipelines(DevOpsPipelinesText);
        var environments = NormalizeLines(DevOpsEnvironmentNamesText);

        return pipelines.Count == 0 && environments.Count == 0
            ? null
            : new IncidentTimelineDevOpsMapping
            {
                Pipelines = pipelines,
                EnvironmentNames = environments,
            };
    }

    private static List<string> NormalizeLines(string value) => value
        .ReplaceLineEndings("\n")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(NormalizeOptional)
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(static value => value!)
        .ToList();

    private static List<SbEntityLink> ParseServiceBusEntities(string value)
    {
        var entities = new List<SbEntityLink>();
        foreach (var line in NormalizeLines(value))
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var namespaceId = parts.Length >= 1 && Guid.TryParse(parts[0], out var parsedNamespaceId)
                ? parsedNamespaceId
                : Guid.Empty;
            var entityPath = parts.Length >= 2 ? NormalizeOptional(parts[1]) : NormalizeOptional(parts[0]);
            if (string.IsNullOrWhiteSpace(entityPath))
            {
                continue;
            }

            entities.Add(new SbEntityLink
            {
                NamespaceId = namespaceId,
                EntityPath = entityPath!,
                Alias = parts.Length >= 3 ? NormalizeOptional(parts[2]) : null,
            });
        }

        return entities;
    }

    private static List<IncidentTimelinePipelineBinding> ParsePipelines(string value)
    {
        var pipelines = new List<IncidentTimelinePipelineBinding>();
        foreach (var line in NormalizeLines(value))
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var projectName = NormalizeOptional(parts[0]);
            if (string.IsNullOrWhiteSpace(projectName) || !int.TryParse(parts[1], out var pipelineId) || pipelineId <= 0)
            {
                continue;
            }

            pipelines.Add(new IncidentTimelinePipelineBinding
            {
                ProjectName = projectName!,
                PipelineId = pipelineId,
                Alias = parts.Length >= 3 ? NormalizeOptional(parts[2]) : null,
            });
        }

        return pipelines;
    }

    private static string FormatServiceBusEntityLine(SbEntityLink entity)
    {
        var parts = new List<string>
        {
            entity.NamespaceId == Guid.Empty ? string.Empty : entity.NamespaceId.ToString(),
            entity.EntityPath,
        };

        if (!string.IsNullOrWhiteSpace(entity.Alias))
        {
            parts.Add(entity.Alias!);
        }

        return string.Join('|', parts);
    }

    private static string FormatPipelineLine(IncidentTimelinePipelineBinding pipeline)
    {
        var parts = new List<string>
        {
            pipeline.ProjectName,
            pipeline.PipelineId.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(pipeline.Alias))
        {
            parts.Add(pipeline.Alias!);
        }

        return string.Join('|', parts);
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}