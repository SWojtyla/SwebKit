using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Generates candidate workload mapping proposals from already-loaded timeline evidence.
/// All proposals are advisory only. The generator must not persist or mutate any configuration.
/// </summary>
public interface IIncidentMappingProposalGenerator
{
    /// <summary>
    /// Inspects <paramref name="page"/> source statuses and compares them against
    /// <paramref name="config"/> to identify sources that are unmapped or unconfigured
    /// for the current workload scope.
    /// Returns a list of candidate proposals with rationale text.
    /// Returns an empty list when all sources are already mapped.
    /// </summary>
    IReadOnlyList<IncidentMappingProposal> Generate(
        IncidentTimelinePage page,
        IncidentTimelineConfig config);
}
