using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public sealed class IncidentInvestigationSeedResolver : IIncidentInvestigationSeedResolver
{
    private static readonly IReadOnlyList<IncidentTimelineSource> DefaultSources =
    [
        IncidentTimelineSource.Aks,
        IncidentTimelineSource.Observability,
        IncidentTimelineSource.ServiceBus,
        IncidentTimelineSource.Releases,
    ];

    public IncidentInvestigationDraft Resolve(IncidentInvestigationSeed seed, IncidentTimelineConfig config)
    {
        var resolvedScope = TryResolveScope(seed, config, out var fromMapping);
        var preselectedSources = ResolvePreselectedSources(seed);
        var (provenance, assumptions) = BuildProvenance(seed, resolvedScope, fromMapping);

        return new IncidentInvestigationDraft
        {
            Seed = seed,
            ResolvedScope = resolvedScope,
            ScopeFromMapping = fromMapping,
            PreselectedSources = preselectedSources,
            ProvenanceSummary = provenance,
            PendingAssumptions = assumptions,
        };
    }

    private static IncidentWorkloadScope? TryResolveScope(
        IncidentInvestigationSeed seed,
        IncidentTimelineConfig config,
        out bool fromMapping)
    {
        fromMapping = false;

        // If seed already carries a concrete candidate scope, try to match it against
        // an existing workload mapping first (improves display name and reduces assumptions).
        if (seed.CandidateScope is { } candidate)
        {
            var match = config.FindWorkloadMapping(candidate);
            if (match is not null)
            {
                fromMapping = true;
                return candidate;
            }

            // Candidate scope provided but no mapping exists — use it as-is with an assumption flag.
            return candidate;
        }

        // For Service Bus seeds, try to find a workload mapping that references the entity path.
        if (seed.SourceArea == IncidentInvestigationSourceArea.ServiceBus
            && seed.EvidenceRef?.EntityPath is { } entityPath)
        {
            var match = config.WorkloadMappings.FirstOrDefault(mapping =>
                mapping.ServiceBusEntities.Any(entity =>
                    string.Equals(entity.EntityPath, entityPath, StringComparison.OrdinalIgnoreCase)));

            if (match is not null)
            {
                fromMapping = true;
                return new IncidentWorkloadScope(
                    ClusterContext: null,
                    Namespace: match.Namespace,
                    WorkloadKind: match.WorkloadKind,
                    WorkloadName: match.WorkloadName);
            }
        }

        // For Observability seeds, try to match by resourceId or cloud role name.
        if (seed.SourceArea == IncidentInvestigationSourceArea.Observability
            && seed.EvidenceRef?.ResourceId is { } resourceId)
        {
            var match = config.WorkloadMappings.FirstOrDefault(mapping =>
                mapping.Observability?.ResourceId is { } mappedId
                && string.Equals(mappedId, resourceId, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                fromMapping = true;
                return new IncidentWorkloadScope(
                    ClusterContext: null,
                    Namespace: match.Namespace,
                    WorkloadKind: match.WorkloadKind,
                    WorkloadName: match.WorkloadName);
            }
        }

        // For Pipelines seeds, try to match by pipeline ID.
        if (seed.SourceArea == IncidentInvestigationSourceArea.Pipelines
            && seed.EvidenceRef?.PipelineId is { } pipelineId)
        {
            var match = config.WorkloadMappings.FirstOrDefault(mapping =>
                mapping.DevOps?.Pipelines.Any(p => p.PipelineId == pipelineId) == true);

            if (match is not null)
            {
                fromMapping = true;
                return new IncidentWorkloadScope(
                    ClusterContext: null,
                    Namespace: match.Namespace,
                    WorkloadKind: match.WorkloadKind,
                    WorkloadName: match.WorkloadName);
            }
        }

        return null;
    }

    private static IReadOnlyList<IncidentTimelineSource> ResolvePreselectedSources(IncidentInvestigationSeed seed)
    {
        if (seed.SuggestedSources is { Count: > 0 })
        {
            return seed.SuggestedSources
                .Where(static source => Enum.IsDefined(source))
                .Distinct()
                .ToList();
        }

        // Default set biased toward the source area.
        return seed.SourceArea switch
        {
            IncidentInvestigationSourceArea.Observability =>
            [
                IncidentTimelineSource.Aks,
                IncidentTimelineSource.Observability,
                IncidentTimelineSource.Releases,
            ],
            IncidentInvestigationSourceArea.ServiceBus =>
            [
                IncidentTimelineSource.Aks,
                IncidentTimelineSource.ServiceBus,
                IncidentTimelineSource.Releases,
            ],
            IncidentInvestigationSourceArea.Pipelines =>
            [
                IncidentTimelineSource.Aks,
                IncidentTimelineSource.Releases,
            ],
            _ => DefaultSources,
        };
    }

    private static (string provenance, IReadOnlyList<string> assumptions) BuildProvenance(
        IncidentInvestigationSeed seed,
        IncidentWorkloadScope? resolvedScope,
        bool fromMapping)
    {
        var assumptions = new List<string>();
        var parts = new List<string>
        {
            $"Investigation started from {seed.SourceAreaLabel}.",
        };

        // Evidence reference summary
        if (seed.EvidenceRef is { } evidence)
        {
            if (!string.IsNullOrWhiteSpace(evidence.ExceptionType))
                parts.Add($"Exception type: {evidence.ExceptionType}.");
            if (!string.IsNullOrWhiteSpace(evidence.EntityPath))
                parts.Add($"Service Bus entity: {evidence.EntityPath}.");
            if (!string.IsNullOrWhiteSpace(evidence.RunDisplayName))
                parts.Add($"Pipeline run: {evidence.RunDisplayName}.");
            if (!string.IsNullOrWhiteSpace(evidence.CorrelationId))
                parts.Add($"Correlation ID carried forward.");
        }

        // Scope provenance
        if (resolvedScope is not null)
        {
            if (fromMapping)
            {
                parts.Add($"Workload scope matched from existing mapping: {resolvedScope.Namespace}/{resolvedScope.WorkloadName}.");
            }
            else
            {
                parts.Add($"Workload scope prefilled from source page: {resolvedScope.Namespace}/{resolvedScope.WorkloadName}.");
                assumptions.Add("Workload scope was prefilled but has no confirmed mapping — verify the scope before refreshing.");
            }
        }
        else
        {
            assumptions.Add("No workload scope could be resolved — select a namespace and workload name before refreshing.");
        }

        return (string.Join(" ", parts), assumptions);
    }
}
