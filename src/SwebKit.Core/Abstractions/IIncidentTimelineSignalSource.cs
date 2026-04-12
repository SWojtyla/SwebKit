using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IIncidentTimelineSignalSource
{
    IncidentTimelineSource Source { get; }

    Task<IncidentTimelineSourceResult> FetchAsync(IncidentTimelineQuery query, CancellationToken ct = default);
}