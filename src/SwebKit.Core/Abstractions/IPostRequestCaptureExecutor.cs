using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Extracts values from an <see cref="HttpRequestResult"/> according to <see cref="CaptureRule"/> definitions
/// and writes the captured values back into the collection or the active environment.
/// </summary>
public interface IPostRequestCaptureExecutor
{
    /// <summary>
    /// Applies all enabled capture rules against <paramref name="result"/>.
    /// Mutates <paramref name="collection"/> and/or <paramref name="activeEnvironment"/> in place.
    /// Returns a (possibly empty) list of per-rule warning messages for display in the UI.
    /// Never throws — failed rules produce a warning entry.
    /// </summary>
    Task<IReadOnlyList<string>> ExecuteAsync(
        HttpRequestResult result,
        HttpRequestEntry request,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default);
}
