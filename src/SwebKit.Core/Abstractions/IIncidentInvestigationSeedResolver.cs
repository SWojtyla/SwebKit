using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IIncidentInvestigationSeedResolver
{
    IncidentInvestigationDraft Resolve(IncidentInvestigationSeed seed, IncidentTimelineConfig config);
}
