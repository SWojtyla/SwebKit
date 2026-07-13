using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// Compares the intended release-component version against the observed AKS runtime image tag.
/// The service is stateless and pure; callers are responsible for resolving and passing the
/// IAksClient.  When no AKS client is available the caller should return NotConfigured results
/// directly rather than calling this service.
/// </summary>
public sealed class RuntimeDriftService
{
    /// <summary>
    /// Evaluates drift for a single component against the live AKS runtime.
    /// </summary>
    public async Task<RuntimeDriftResult> GetDriftAsync(
        ComponentScope component,
        IAksClient aksClient,
        CancellationToken ct = default)
    {
        var binding = component.RuntimeBinding;

        if (binding is null
            || string.IsNullOrWhiteSpace(binding.Namespace)
            || string.IsNullOrWhiteSpace(binding.WorkloadName))
        {
            return new RuntimeDriftResult(
                component.ComponentName,
                RuntimeDriftState.NotConfigured,
                component.TargetTag,
                null,
                null,
                "No runtime binding configured.");
        }

        try
        {
            // Find a running pod belonging to the workload by name prefix.
            var pods = await aksClient.GetPodsAsync(binding.Namespace, ct: ct).ConfigureAwait(false);
            var workloadPod = pods.FirstOrDefault(p =>
                p.Name.StartsWith(binding.WorkloadName, StringComparison.OrdinalIgnoreCase)
                && p.Phase is "Running" or "Pending");

            if (workloadPod is null)
            {
                return new RuntimeDriftResult(
                    component.ComponentName,
                    RuntimeDriftState.Unknown,
                    component.TargetTag,
                    null,
                    null,
                    $"No pods found for workload '{binding.WorkloadName}' in namespace '{binding.Namespace}'.");
            }

            var containers = await aksClient.GetContainerDetailsAsync(
                binding.Namespace, workloadPod.Name, ct).ConfigureAwait(false);

            var container = binding.ContainerName is not null
                ? containers.FirstOrDefault(c => string.Equals(c.Name, binding.ContainerName, StringComparison.OrdinalIgnoreCase))
                : containers.FirstOrDefault();

            if (container is null)
            {
                return new RuntimeDriftResult(
                    component.ComponentName,
                    RuntimeDriftState.Unknown,
                    component.TargetTag,
                    null,
                    $"aks/{binding.Namespace}/{workloadPod.Name}",
                    binding.ContainerName is not null
                        ? $"Container '{binding.ContainerName}' not found in pod."
                        : "No containers found in pod.");
            }

            var observedTag = container.ImageTag ?? ExtractTagFromImage(container.Image);
            var source = $"aks/{binding.Namespace}/{workloadPod.Name}";

            if (string.IsNullOrWhiteSpace(component.TargetTag))
            {
                return new RuntimeDriftResult(
                    component.ComponentName,
                    RuntimeDriftState.Unknown,
                    null,
                    observedTag,
                    source,
                    "No target tag set on release component.");
            }

            var state = string.Equals(component.TargetTag, observedTag, StringComparison.OrdinalIgnoreCase)
                ? RuntimeDriftState.Matched
                : RuntimeDriftState.Drifted;

            return new RuntimeDriftResult(
                component.ComponentName,
                state,
                component.TargetTag,
                observedTag,
                source,
                null);
        }
        catch (Exception ex)
        {
            return new RuntimeDriftResult(
                component.ComponentName,
                RuntimeDriftState.Unknown,
                component.TargetTag,
                null,
                null,
                $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Evaluates drift for all in-scope components that carry a runtime binding.
    /// Components without a binding receive NotConfigured without an AKS call.
    /// </summary>
    public async Task<IReadOnlyList<RuntimeDriftResult>> GetDriftAsync(
        IEnumerable<ComponentScope> components,
        IAksClient aksClient,
        CancellationToken ct = default)
    {
        var results = new List<RuntimeDriftResult>();
        foreach (var comp in components.Where(c => c.InScope))
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await GetDriftAsync(comp, aksClient, ct).ConfigureAwait(false));
        }
        return results;
    }

    private static string? ExtractTagFromImage(string image)
    {
        var idx = image.LastIndexOf(':');
        return idx >= 0 && idx < image.Length - 1 ? image[(idx + 1)..] : null;
    }
}
