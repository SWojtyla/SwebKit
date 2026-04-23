using SwebKit.Core.Models;

namespace SwebKit.Core.Domain;

public class IncidentTimelineConfig
{
    public List<IncidentTimelineWorkloadMapping> WorkloadMappings { get; set; } = [];

    public IncidentTimelineWorkloadMapping? FindWorkloadMapping(IncidentWorkloadScope scope) =>
        WorkloadMappings.FirstOrDefault(mapping => mapping.Matches(scope));
}

public class IncidentTimelineWorkloadMapping
{
    public string Namespace { get; set; } = string.Empty;
    public IncidentWorkloadKind WorkloadKind { get; set; } = IncidentWorkloadKind.Deployment;
    public string WorkloadName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public IncidentTimelineObservabilityMapping? Observability { get; set; }
    public List<SbEntityLink> ServiceBusEntities { get; set; } = [];
    public IncidentTimelineDevOpsMapping? DevOps { get; set; }

    public bool Matches(IncidentWorkloadScope scope) =>
        string.Equals(Namespace, scope.Namespace, StringComparison.OrdinalIgnoreCase)
        && WorkloadKind == scope.WorkloadKind
        && string.Equals(WorkloadName, scope.WorkloadName, StringComparison.OrdinalIgnoreCase);
}

public class IncidentTimelineObservabilityMapping
{
    public string? ResourceId { get; set; }
    public List<string> CloudRoleNames { get; set; } = [];
    public List<string> OperationNames { get; set; } = [];
}

public class IncidentTimelineDevOpsMapping
{
    public List<IncidentTimelinePipelineBinding> Pipelines { get; set; } = [];
    public List<string> EnvironmentNames { get; set; } = [];
}

public class IncidentTimelinePipelineBinding
{
    public string ProjectName { get; set; } = string.Empty;
    public int PipelineId { get; set; }
    public string? Alias { get; set; }
}