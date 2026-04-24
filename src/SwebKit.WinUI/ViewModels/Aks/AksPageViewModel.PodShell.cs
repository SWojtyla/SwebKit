using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace SwebKit.WinUI.ViewModels.Aks;

public sealed partial class AksPageViewModel
{
    public bool CanOpenSelectedPodShell =>
        !IsLoading
        && Client is not null
        && SelectedPod is not null
        && string.Equals(SelectedPod.Status, "Running", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private async Task OpenSelectedPodShellAsync()
    {
        var selectedPod = SelectedPod;
        var client = Client;

        if (selectedPod is null || client is null)
        {
            return;
        }

        if (!string.Equals(selectedPod.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            _notifications.ShowWarning("Pod shell unavailable", $"{selectedPod.Namespace}/{selectedPod.Name} is not currently running.");
            return;
        }

        if (_appState.UseDemoData)
        {
            _notifications.ShowInfo("Demo mode shell", "Demo AKS mode does not launch an external pod shell.");
            return;
        }

        var container = selectedPod.Containers
            .FirstOrDefault(containerName => !string.Equals(containerName, "istio-proxy", StringComparison.Ordinal)
                && !string.Equals(containerName, "linkerd-proxy", StringComparison.Ordinal))
            ?? selectedPod.Containers.FirstOrDefault()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(container))
        {
            _notifications.ShowWarning("Pod shell unavailable", $"{selectedPod.Namespace}/{selectedPod.Name} does not expose a launchable container.");
            return;
        }

        try
        {
            await client.OpenShellAsync(selectedPod.Namespace, selectedPod.Name, container);
            _notifications.ShowSuccess("Pod shell launched", $"{selectedPod.Name} · {container}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AKS pod shell launch failed for {Namespace}/{PodName}.", selectedPod.Namespace, selectedPod.Name);
            _notifications.ShowError("Pod shell launch failed", ex.Message, ex);
        }
    }
}