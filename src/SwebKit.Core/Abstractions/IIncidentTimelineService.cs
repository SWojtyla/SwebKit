using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IIncidentTimelineService
{
    Task<IncidentTimelinePage> GetTimelineAsync(IncidentTimelineQuery query, CancellationToken ct = default);
}