using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// Validates that the observed AKS runtime image tag matches the intended tag for a release
/// component.  The result is a <see cref="DeploymentValidationSnapshot"/> ready to be persisted
/// by the caller after setting <c>ReleaseId</c>.
///
/// The service is stateless and pure.  Callers are responsible for resolving and passing the
/// <see cref="IAksClient"/>.  When no AKS client is available the caller should persist a
/// Partial snapshot directly rather than calling this service.
/// </summary>
public sealed class DeploymentValidationService
{
    public async Task<DeploymentValidationSnapshot> ValidateAsync(
        ComponentScope component,
        IAksClient aksClient,
        CancellationToken ct = default)
    {
        var binding = component.RuntimeBinding;

        if (binding is null
            || string.IsNullOrWhiteSpace(binding.Namespace)
            || string.IsNullOrWhiteSpace(binding.WorkloadName))
        {
            return Snapshot(component, DeploymentValidationState.Partial, aksQueried: false,
                note: "No runtime binding configured.");
        }

        try
        {
            var pods = await aksClient.GetPodsAsync(binding.Namespace, ct: ct).ConfigureAwait(false);
            var workloadPod = pods.FirstOrDefault(p =>
                p.Name.StartsWith(binding.WorkloadName, StringComparison.OrdinalIgnoreCase)
                && p.Phase is "Running" or "Pending");

            if (workloadPod is null)
            {
                return Snapshot(component, DeploymentValidationState.Partial, aksQueried: true,
                    note: $"No pods found for workload '{binding.WorkloadName}' in namespace '{binding.Namespace}'.");
            }

            var containers = await aksClient.GetContainerDetailsAsync(binding.Namespace, workloadPod.Name, ct).ConfigureAwait(false);

            var container = binding.ContainerName is not null
                ? containers.FirstOrDefault(c => string.Equals(c.Name, binding.ContainerName, StringComparison.OrdinalIgnoreCase))
                : containers.Count > 0 ? containers[0] : null;

            if (container is null)
            {
                return Snapshot(component, DeploymentValidationState.Partial, aksQueried: true,
                    observedSource: $"aks/{binding.Namespace}/{workloadPod.Name}",
                    note: binding.ContainerName is not null
                        ? $"Container '{binding.ContainerName}' not found in pod."
                        : "No containers found in pod.");
            }

            var observedTag = container.ImageTag ?? ExtractTagFromImage(container.Image);
            var source = $"aks/{binding.Namespace}/{workloadPod.Name}";

            if (string.IsNullOrWhiteSpace(component.TargetTag))
            {
                return Snapshot(component, DeploymentValidationState.Partial, aksQueried: true,
                    observedTag: observedTag, observedSource: source,
                    note: "No target tag set on release component.");
            }

            var matched = string.Equals(component.TargetTag, observedTag, StringComparison.OrdinalIgnoreCase);
            var state = matched ? DeploymentValidationState.Passed : DeploymentValidationState.Drifted;

            return new DeploymentValidationSnapshot
            {
                ComponentName = component.ComponentName,
                State = state,
                TargetTag = component.TargetTag,
                ObservedTag = observedTag,
                ObservedSource = source,
                AksQueried = true,
                Note = null
            };
        }
        catch (OperationCanceledException) { throw; } // CS-2
        catch (Exception ex)
        {
            return Snapshot(component, DeploymentValidationState.Failed, aksQueried: true,
                note: $"AKS query failed: {ex.Message}");
        }
    }

    private static DeploymentValidationSnapshot Snapshot(
        ComponentScope component,
        DeploymentValidationState state,
        bool aksQueried,
        string? observedTag = null,
        string? observedSource = null,
        string? note = null) =>
        new()
        {
            ComponentName = component.ComponentName,
            State = state,
            TargetTag = component.TargetTag,
            ObservedTag = observedTag,
            ObservedSource = observedSource,
            AksQueried = aksQueried,
            Note = note
        };

    private static string? ExtractTagFromImage(string? image)
    {
        if (image is null) return null;
        var colonIdx = image.LastIndexOf(':');
        return colonIdx >= 0 ? image[(colonIdx + 1)..] : null;
    }
}
