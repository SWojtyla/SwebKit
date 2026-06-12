namespace SwebKit.Core.Abstractions;

/// <summary>
/// Returns a preview of resolved variable tokens for display purposes only.
/// Secrets are masked as <c>••••••••</c>; no substitution side-effects occur.
/// </summary>
public interface IVariablePreviewService
{
    /// <summary>
    /// Returns a dictionary mapping every <c>{{token}}</c> key found in <paramref name="text"/>
    /// to its resolved display value. Unresolved tokens map to <c>null</c>.
    /// </summary>
    IReadOnlyDictionary<string, string?> Preview(
        string text,
        IReadOnlyDictionary<string, string?> scope);
}
