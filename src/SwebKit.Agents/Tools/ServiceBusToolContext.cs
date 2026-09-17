using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

public static class ServiceBusToolContext
{
    public sealed record Resolution(ServiceBusNamespace? Namespace, string? Error)
    {
        public bool IsSuccess => Namespace is not null;
    }

    public static Resolution ResolveNamespace(AppStateService appState, string? requested)
    {
        var namespaces = appState.ServiceBusNamespaces;
        if (namespaces.Count == 0)
            return new(null, "Service Bus not configured. Add a namespace in settings.");

        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = Find(namespaces, requested);
            return match is not null
                ? new(match, null)
                : new(null, Unknown(requested, namespaces));
        }

        var selection = AgentExecutionContext.Selection;
        if (selection is not null)
        {
            foreach (var key in new[] { "nsId", "namespace", "nsAlias" })
            {
                if (!selection.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) continue;
                var match = Find(namespaces, value);
                if (match is not null) return new(match, null);
                return new(null, Unknown(value, namespaces));
            }
        }

        return namespaces.Count == 1
            ? new(namespaces[0], null)
            : new(null, $"Service Bus namespace is ambiguous. Specify one of: {Aliases(namespaces)}.");
    }

    private static ServiceBusNamespace? Find(IEnumerable<ServiceBusNamespace> namespaces, string requested) =>
        namespaces.FirstOrDefault(ns =>
            string.Equals(ns.Alias, requested, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ns.FullyQualifiedNamespace, requested, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ns.Id.ToString(), requested, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ns.Id.ToString("N"), requested, StringComparison.OrdinalIgnoreCase));

    private static string Unknown(string requested, IEnumerable<ServiceBusNamespace> namespaces) =>
        $"Service Bus namespace '{requested}' was not found. Configured aliases: {Aliases(namespaces)}.";

    private static string Aliases(IEnumerable<ServiceBusNamespace> namespaces) =>
        string.Join(", ", namespaces.Select(ns => ns.Alias).OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase));
}
