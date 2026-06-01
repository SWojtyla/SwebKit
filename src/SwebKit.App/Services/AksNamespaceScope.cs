namespace SwebKit.App.Services;

public enum AksNamespaceScopeMode
{
    Single,
    Selected,
    All
}

public sealed record AksNamespaceScope(AksNamespaceScopeMode Mode, IReadOnlyList<string> Namespaces)
{
    public const string AllNamespacesToken = "*";

    public bool IsAll => Mode == AksNamespaceScopeMode.All;

    public bool IsMulti => IsAll || Namespaces.Count > 1;

    public string Primary => Namespaces.FirstOrDefault() ?? "default";

    public string SelectionToken => IsAll
        ? AllNamespacesToken
        : string.Join(",", Namespaces);

    public string CacheKeyPart => SelectionToken;

    public string DisplayText => IsAll
        ? AllNamespacesToken
        : Namespaces.Count switch
        {
            0 => string.Empty,
            1 => Namespaces[0],
            _ => $"{Namespaces.Count} namespaces"
        };

    public string DisplayTitle => IsAll
        ? "All namespaces"
        : string.Join(", ", Namespaces);

    public static AksNamespaceScope FromSelection(
        string? selection,
        IReadOnlyList<string> availableNamespaces,
        string fallbackNamespace = "default")
    {
        var available = NormalizeNamespaces(availableNamespaces).ToList();
        if (string.Equals(selection?.Trim(), AllNamespacesToken, StringComparison.Ordinal))
        {
            return new(AksNamespaceScopeMode.All, available);
        }

        var selected = ParseSelection(selection)
            .Where(ns => available.Count == 0 || available.Contains(ns, StringComparer.Ordinal))
            .ToList();

        if (selected.Count == 0)
        {
            selected = ResolveFallback(available, fallbackNamespace);
        }

        return FromNamespaces(selected);
    }

    public static AksNamespaceScope FromNamespaces(IEnumerable<string> namespaces)
    {
        var selected = NormalizeNamespaces(namespaces).ToList();
        return new(selected.Count > 1 ? AksNamespaceScopeMode.Selected : AksNamespaceScopeMode.Single, selected);
    }

    public static IReadOnlyList<string> ParseSelection(string? selection)
        => string.IsNullOrWhiteSpace(selection)
            ? []
            : selection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(ns => !string.IsNullOrWhiteSpace(ns) && ns != AllNamespacesToken)
                .Distinct(StringComparer.Ordinal)
                .ToList();

    public static string NormalizeSelection(
        string? selection,
        IReadOnlyList<string> availableNamespaces,
        string fallbackNamespace = "default")
        => FromSelection(selection, availableNamespaces, fallbackNamespace).SelectionToken;

    private static IEnumerable<string> NormalizeNamespaces(IEnumerable<string> namespaces)
        => namespaces
            .Where(ns => !string.IsNullOrWhiteSpace(ns))
            .Select(ns => ns.Trim())
            .Distinct(StringComparer.Ordinal);

    private static List<string> ResolveFallback(IReadOnlyList<string> availableNamespaces, string fallbackNamespace)
    {
        if (!string.IsNullOrWhiteSpace(fallbackNamespace))
        {
            var fallback = fallbackNamespace.Trim();
            if (availableNamespaces.Count == 0 || availableNamespaces.Contains(fallback, StringComparer.Ordinal))
            {
                return [fallback];
            }
        }

        return availableNamespaces.Count > 0 ? [availableNamespaces[0]] : [];
    }
}
