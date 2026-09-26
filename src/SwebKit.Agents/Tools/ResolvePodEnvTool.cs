using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;
using YamlDotNet.RepresentationModel;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Composite tool for the recurring "where does this parameter come from" investigation: parses a
/// pod's spec (<c>env</c>/<c>envFrom</c>) and resolves <c>configMapKeyRef</c>/<c>secretKeyRef</c>
/// references against the real ConfigMap/Secret objects — one call instead of the model chaining
/// <c>get_resource_yaml</c> for the pod, then again per referenced object. ConfigMap values are
/// resolved inline; Secret values are always masked (the security constraint is "never put secrets
/// in prompts" — the reference name/key answers the question; the UI secrets viewer holds values).
/// <c>pod_name</c>/<c>namespace</c> default to the UI selection, so a contextual "this pod" needs
/// zero arguments.
/// </summary>
public sealed class ResolvePodEnvTool(
    IAksClientFactory aksFactory,
    DemoAksClient demoAksClient,
    AppStateService appState) : IAgentTool
{
    /// <summary>Caps on a single resolved ConfigMap value and on envFrom key expansion — large
    /// blobs (an appsettings.json dumped into a ConfigMap) or wide envFrom prefixes would
    /// otherwise dominate the model's context for no diagnostic gain.</summary>
    private const int MaxValueCharacters = 200;
    private const int MaxEnvFromKeys = 100;

    public string Name => "resolve_pod_env";

    public string Description =>
        "Resolves a pod's environment variables end-to-end: every env/envFrom entry with its " +
        "literal value or the ConfigMap/Secret reference it comes from — ConfigMap values resolved " +
        "inline, Secret values masked. Answers 'where does variable X come from' in one call. " +
        "Omit pod_name/namespace to use the pod selected in the UI.";

    public FeatureArea FeatureArea => FeatureArea.Aks;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "pod_name": { "type": "string", "description": "Pod name. Omit to use the pod selected in the UI." },
            "namespace": { "type": "string", "description": "Kubernetes namespace. Omit to use the UI selection or configured default." },
            "env_name": { "type": "string", "description": "Optional: only return entries whose resolved name matches (e.g. 'Feature__X')." },
            "context": { "type": "string", "description": "Optional kubeconfig context — target this cluster instead of the globally configured one." }
          }
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var podName = GetString(arguments, "pod_name")
            ?? Selection("pod");
        if (string.IsNullOrWhiteSpace(podName))
            return "{\"error\":\"No pod_name given and no pod selected in the UI.\"}";

        var ns = GetAksResourceYamlTool.ResolveNamespace(arguments, appState);
        var envFilter = GetString(arguments, "env_name");
        var client = AksToolContext.ResolveClient(aksFactory, demoAksClient, appState, AksToolContext.GetContext(arguments));

        string yaml;
        try
        {
            yaml = await client.GetResourceYamlAsync(ns, "Pod", podName, ct);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, pod = podName, namespace_name = ns });
        }

        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root = (YamlMappingNode)stream.Documents[0].RootNode;
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Could not parse pod YAML: {ex.Message}", pod = podName });
        }

        var warnings = new List<string>();
        var configMapCache = new Dictionary<string, Dictionary<string, string>?>(StringComparer.Ordinal);
        var secretCache = new Dictionary<string, Dictionary<string, string>?>(StringComparer.Ordinal);

        var containers = new List<object>();
        foreach (var section in new[] { "containers", "initContainers" })
        {
            if (Child(Map(root, "spec"), section) is not YamlSequenceNode containerList)
                continue;

            foreach (var containerNode in containerList.Children.OfType<YamlMappingNode>())
            {
                var containerName = Scalar(Child(containerNode, "name")) ?? "(unnamed)";
                var env = await ResolveEnvEntries(
                    client, ns, containerNode, envFilter, warnings, configMapCache, secretCache, ct);
                var envFrom = await ResolveEnvFrom(
                    client, ns, containerNode, envFilter, warnings, configMapCache, secretCache, ct);
                containers.Add(new
                {
                    name = containerName,
                    init = section == "initContainers",
                    env,
                    envFrom,
                });
            }
        }

        var owner = Map(root, "metadata") is { } metadata &&
                    Child(metadata, "ownerReferences") is YamlSequenceNode owners &&
                    owners.Children.OfType<YamlMappingNode>().FirstOrDefault() is { } ownerRef
            ? new { kind = Scalar(Child(ownerRef, "kind")), name = Scalar(Child(ownerRef, "name")) }
            : null;

        return JsonSerializer.Serialize(new
        {
            pod = podName,
            namespace_name = ns,
            owner,
            containers,
            warnings = warnings.Count > 0 ? warnings : null,
        });
    }

    /// <summary>Per-container <c>env:</c> entries — literal values verbatim, refs resolved.</summary>
    private static async Task<List<object>> ResolveEnvEntries(
        IAksClient client, string ns, YamlMappingNode container, string? envFilter,
        List<string> warnings,
        Dictionary<string, Dictionary<string, string>?> configMapCache,
        Dictionary<string, Dictionary<string, string>?> secretCache,
        CancellationToken ct)
    {
        var entries = new List<object>();
        if (Child(container, "env") is not YamlSequenceNode envList)
            return entries;

        foreach (var entry in envList.Children.OfType<YamlMappingNode>())
        {
            var name = Scalar(Child(entry, "name"));
            if (name is null || (envFilter is not null && !name.Equals(envFilter, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (Scalar(Child(entry, "value")) is { } literal)
            {
                entries.Add(new { name, source = "literal", value = Truncate(literal), resolved = true });
                continue;
            }

            if (Map(entry, "valueFrom") is not { } valueFrom)
            {
                entries.Add(new { name, source = "declared-empty", resolved = true });
                continue;
            }

            if (Map(valueFrom, "configMapKeyRef") is { } cmRef)
            {
                var refName = Scalar(Child(cmRef, "name"))!;
                var key = Scalar(Child(cmRef, "key"))!;
                var data = await FetchValues(client.GetConfigMapValuesAsync, configMapCache, ns, refName, "ConfigMap", warnings, ct);
                entries.Add(Resolved(name, $"configMapKeyRef {refName}/{key}", data, key, mask: false));
            }
            else if (Map(valueFrom, "secretKeyRef") is { } secRef)
            {
                var refName = Scalar(Child(secRef, "name"))!;
                var key = Scalar(Child(secRef, "key"))!;
                var data = await FetchValues(client.GetSecretValuesAsync, secretCache, ns, refName, "Secret", warnings, ct);
                entries.Add(Resolved(name, $"secretKeyRef {refName}/{key}", data, key, mask: true));
            }
            else if (Map(valueFrom, "fieldRef") is { } fieldRef)
            {
                entries.Add(new
                {
                    name,
                    source = $"fieldRef {Scalar(Child(fieldRef, "fieldPath"))}",
                    resolved = true,
                    note = "runtime-resolved from pod metadata/status",
                });
            }
            else if (Map(valueFrom, "resourceFieldRef") is { } resRef)
            {
                entries.Add(new
                {
                    name,
                    source = $"resourceFieldRef {Scalar(Child(resRef, "resource"))}",
                    resolved = true,
                    note = "runtime-resolved from container resources",
                });
            }
            else
            {
                entries.Add(new { name, source = "unknown valueFrom", resolved = false });
            }
        }
        return entries;
    }

    /// <summary>Per-container <c>envFrom:</c> blocks — expands referenced objects to key names
    /// (ConfigMap values shown, Secret values masked), with the optional prefix applied.</summary>
    private static async Task<List<object>> ResolveEnvFrom(
        IAksClient client, string ns, YamlMappingNode container, string? envFilter,
        List<string> warnings,
        Dictionary<string, Dictionary<string, string>?> configMapCache,
        Dictionary<string, Dictionary<string, string>?> secretCache,
        CancellationToken ct)
    {
        var blocks = new List<object>();
        if (Child(container, "envFrom") is not YamlSequenceNode envFromList)
            return blocks;

        foreach (var block in envFromList.Children.OfType<YamlMappingNode>())
        {
            var prefix = Scalar(Child(block, "prefix")) ?? string.Empty;

            if (Map(block, "configMapRef") is { } cmRef)
            {
                var refName = Scalar(Child(cmRef, "name"))!;
                var data = await FetchValues(client.GetConfigMapValuesAsync, configMapCache, ns, refName, "ConfigMap", warnings, ct);
                blocks.Add(Expanded(prefix, $"configMapRef {refName}", data, envFilter, mask: false));
            }
            else if (Map(block, "secretRef") is { } secRef)
            {
                var refName = Scalar(Child(secRef, "name"))!;
                var data = await FetchValues(client.GetSecretValuesAsync, secretCache, ns, refName, "Secret", warnings, ct);
                blocks.Add(Expanded(prefix, $"secretRef {refName}", data, envFilter, mask: true));
            }
        }
        return blocks;
    }

    private static object Resolved(string name, string source, Dictionary<string, string>? data, string key, bool mask)
    {
        if (data is null)
            return new { name, source, resolved = false };
        if (!data.TryGetValue(key, out var value))
            return new { name, source, resolved = false, missingKey = true };
        return new { name, source, value = mask ? "***" : Truncate(value), resolved = true };
    }

    private static object Expanded(string prefix, string source, Dictionary<string, string>? data, string? envFilter, bool mask)
    {
        if (data is null)
            return new { source, resolved = false };

        var entries = data
            .Select(kv => new { envName = prefix + kv.Key, kv.Value })
            .Where(e => envFilter is null || e.envName.Equals(envFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.envName, StringComparer.Ordinal)
            .Take(MaxEnvFromKeys)
            .Select(e => (object)new { name = e.envName, value = mask ? "***" : Truncate(e.Value), resolved = true })
            .ToList();

        return new
        {
            source,
            prefix = prefix.Length > 0 ? prefix : null,
            resolved = true,
            injected = entries,
            truncated = data.Count > entries.Count,
        };
    }

    /// <summary>Fetches a referenced object's values once per call — a pod that references the
    /// same ConfigMap from five env entries costs one cluster round-trip, not five.</summary>
    private static async Task<Dictionary<string, string>?> FetchValues(
        Func<string, string, CancellationToken, Task<Dictionary<string, string>>> fetch,
        Dictionary<string, Dictionary<string, string>?> cache,
        string ns, string name, string kind, List<string> warnings, CancellationToken ct)
    {
        if (cache.TryGetValue(name, out var cached))
            return cached;
        try
        {
            var values = await fetch(ns, name, ct);
            cache[name] = values;
            if (values.Count == 0)
                warnings.Add($"{kind} '{name}' returned no data — check it exists in namespace '{ns}'.");
            return values;
        }
        catch (Exception ex)
        {
            warnings.Add($"{kind} '{name}' could not be read: {ex.Message}");
            cache[name] = null;
            return null;
        }
    }

    private static string? GetString(JsonElement arguments, string key) =>
        arguments.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(el.GetString())
            ? el.GetString()
            : null;

    private static string? Selection(string key) =>
        AgentExecutionContext.Selection?.TryGetValue(key, out var value) == true &&
        !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static YamlMappingNode? Map(YamlMappingNode? parent, string key) =>
        Child(parent, key) is YamlMappingNode m ? m : null;

    private static YamlNode? Child(YamlMappingNode? map, string key) =>
        map is not null && map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static string? Scalar(YamlNode? node) => (node as YamlScalarNode)?.Value;

    private static string Truncate(string value) =>
        value.Length <= MaxValueCharacters ? value : value[..MaxValueCharacters] + "…";
}