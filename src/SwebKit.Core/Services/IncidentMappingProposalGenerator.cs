using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// Generates candidate workload mapping proposals from source statuses in a loaded
/// <see cref="IncidentTimelinePage"/>. Proposals are advisory only and must not be
/// persisted without explicit operator acceptance.
/// </summary>
public sealed class IncidentMappingProposalGenerator : IIncidentMappingProposalGenerator
{
    public IReadOnlyList<IncidentMappingProposal> Generate(
        IncidentTimelinePage page,
        IncidentTimelineConfig config)
    {
        var scope = page.Query.Scope;
        var existingMapping = config.FindWorkloadMapping(scope);
        var proposals = new List<IncidentMappingProposal>();

        foreach (var status in page.SourceStatuses)
        {
            if (status.CoverageState is not (IncidentTimelineSourceCoverageState.Unmapped
                or IncidentTimelineSourceCoverageState.NotConfigured))
                continue;

            var proposal = BuildProposal(scope, status, existingMapping);
            if (proposal is not null)
                proposals.Add(proposal);
        }

        return proposals;
    }

    private static IncidentMappingProposal? BuildProposal(
        IncidentWorkloadScope scope,
        IncidentTimelineSourceStatus status,
        IncidentTimelineWorkloadMapping? existingMapping)
    {
        var (sourceArea, rationale) = status.Source switch
        {
            IncidentTimelineSource.Observability when status.CoverageState == IncidentTimelineSourceCoverageState.Unmapped =>
                ("Observability",
                 $"No Application Insights mapping exists for {scope.WorkloadKind} '{scope.WorkloadName}' in namespace '{scope.Namespace}'. " +
                 "Adding a mapping with the App Insights resource ID and cloud role name would include telemetry evidence in this investigation scope."),

            IncidentTimelineSource.Observability when status.CoverageState == IncidentTimelineSourceCoverageState.NotConfigured =>
                ("Observability",
                 $"A mapping entry exists for '{scope.WorkloadName}' but no Application Insights resource is configured. " +
                 "Providing the App Insights resource ID in Incident Timeline settings would enable telemetry evidence."),

            IncidentTimelineSource.ServiceBus when status.CoverageState == IncidentTimelineSourceCoverageState.Unmapped =>
                ("ServiceBus",
                 $"No Service Bus entity is linked to {scope.WorkloadKind} '{scope.WorkloadName}' in namespace '{scope.Namespace}'. " +
                 "Adding a queue or topic entity path to the workload mapping would include message-level evidence."),

            IncidentTimelineSource.Releases when status.CoverageState == IncidentTimelineSourceCoverageState.Unmapped =>
                ("Pipelines",
                 $"No Azure DevOps pipeline is mapped to {scope.WorkloadKind} '{scope.WorkloadName}'. " +
                 "Linking a pipeline and environment name would surface deployment evidence in this scope."),

            _ => (null, null),
        };

        if (sourceArea is null || rationale is null)
            return null;

        return new IncidentMappingProposal
        {
            ProposalId = $"{scope.Namespace}-{scope.WorkloadName}-{sourceArea}",
            Namespace = scope.Namespace,
            WorkloadKind = scope.WorkloadKind,
            WorkloadName = scope.WorkloadName,
            SourceArea = sourceArea,
            Rationale = rationale,
            EvidenceItemCount = status.ItemCount,
            Status = IncidentProposalStatus.Candidate,
        };
    }
}
