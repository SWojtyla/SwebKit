using SwebKit.Core.Domain;

namespace SwebKit.App.Components.ApiClient;

/// <summary>
/// Represents a single open tab in the optional request tab strip (Phase 3).
/// </summary>
/// <remarks>
/// Session-only per DEC-UX-7 (docs/features/active/api-client-ux-refactor/decisions.md): open
/// tabs are not persisted across app restart. <see cref="Request"/> holds a reference to the
/// live <see cref="HttpRequestEntry"/> (not a copy), matching the per-request dictionaries on
/// <see cref="ApiClientState"/>.
/// </remarks>
public sealed class ApiClientOpenTab
{
    public string RequestId { get; set; } = string.Empty;
    public HttpRequestEntry Request { get; set; } = null!;
}
