using Microsoft.UI.Xaml;

namespace SwebKit.WinUI.ViewModels.Aks;

public sealed partial class AksPageViewModel
{
    public IReadOnlyList<AksKeyboardHintItemViewModel> KeyboardHints => BuildKeyboardHints();

    public Visibility KeyboardHintsVisibility => ResourceItems.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public async Task<bool> HandleKeyboardShortcutAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || IsLoading)
        {
            return false;
        }

        switch (key)
        {
            case "Escape":
                ClearSelectedResource();
                return true;

            case "Enter":
                return await HandleDefaultKeyboardActionAsync();

            case "l":
                if (SelectedResourceItem is not null && CanResourceSupportWorkloadLogs(SelectedResourceItem))
                {
                    await OpenSelectedResourceWorkloadLogsAsync();
                    return true;
                }

                if (SelectedPod is not null)
                {
                    await ReloadSelectedPodLogsAsync();
                    return true;
                }

                return false;

            case "n":
                if (CanAnalyzeSelectedResource)
                {
                    await AnalyzeSelectedResourceAsync();
                    return true;
                }

                return false;

            case "y":
                if (CanOpenSelectedResourceYaml)
                {
                    await OpenSelectedResourceYamlAsync();
                    return true;
                }

                return false;

            case "r":
                if (CanRestartSelectedResource)
                {
                    await RestartSelectedResourceAsync();
                    return true;
                }

                if (CanTriggerSelectedResource)
                {
                    await TriggerSelectedResourceAsync();
                    return true;
                }

                return false;

            case "s":
                if (CanOpenSelectedPodShell)
                {
                    await OpenSelectedPodShellAsync();
                    return true;
                }

                return false;

            case "p":
                if (CanStartSelectedPodPortForward)
                {
                    OpenSelectedPodPortForward();
                    return true;
                }

                return false;

            case "d":
                if (CanDeleteSelectedResource)
                {
                    await DeleteSelectedResourceAsync();
                    return true;
                }

                return false;

            case "i":
                if (CanAnalyzeSelectedResource)
                {
                    await AnalyzeSelectedResourceAsync();
                    return true;
                }

                return false;

            case "h":
                if (CanOpenSelectedResourceHelmHistory)
                {
                    await OpenSelectedResourceHelmHistoryAsync();
                    return true;
                }

                return false;

            case "v":
                if (CanOpenSelectedResourceHelmValues)
                {
                    await OpenSelectedResourceHelmValuesAsync();
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private async Task<bool> HandleDefaultKeyboardActionAsync()
    {
        if (SelectedResourceItem is null)
        {
            return false;
        }

        if (CanResourceSupportWorkloadLogs(SelectedResourceItem))
        {
            await OpenSelectedResourceWorkloadLogsAsync();
            return true;
        }

        if (SelectedPod is not null)
        {
            await ReloadSelectedPodLogsAsync();
            return true;
        }

        if (string.Equals(SelectedResourceItem.ApiKind, "Ingress", StringComparison.Ordinal) && CanOpenSelectedResourceUrl)
        {
            await OpenSelectedResourceUrlAsync();
            return true;
        }

        if (CanOpenSelectedResourceHelmHistory)
        {
            await OpenSelectedResourceHelmHistoryAsync();
            return true;
        }

        if (CanOpenSelectedResourceYaml)
        {
            await OpenSelectedResourceYamlAsync();
            return true;
        }

        return false;
    }

    private IReadOnlyList<AksKeyboardHintItemViewModel> BuildKeyboardHints()
    {
        var hints = new List<AksKeyboardHintItemViewModel>();
        var selectedResource = SelectedResourceItem;

        switch (SelectedResourceKind)
        {
            case "Deployments":
            case "StatefulSets":
                hints.Add(new AksKeyboardHintItemViewModel("Enter", "logs"));
                hints.Add(new AksKeyboardHintItemViewModel("L", "logs"));
                hints.Add(new AksKeyboardHintItemViewModel("N", "network"));
                hints.Add(new AksKeyboardHintItemViewModel("Y", "yaml"));
                hints.Add(new AksKeyboardHintItemViewModel("R", "restart"));
                break;

            case "Pods":
                var podRunning = string.Equals(SelectedPod?.Status, "Running", StringComparison.OrdinalIgnoreCase);
                var podTerminating = SelectedPod?.Status?.Contains("Terminating", StringComparison.OrdinalIgnoreCase) == true;
                hints.Add(new AksKeyboardHintItemViewModel("Enter", "logs"));
                hints.Add(new AksKeyboardHintItemViewModel("L", "logs"));
                hints.Add(new AksKeyboardHintItemViewModel("N", "network"));
                hints.Add(new AksKeyboardHintItemViewModel("Y", "yaml"));
                hints.Add(new AksKeyboardHintItemViewModel("S", "shell", !podRunning));
                hints.Add(new AksKeyboardHintItemViewModel("P", "port-fwd"));
                if (!podTerminating)
                {
                    hints.Add(new AksKeyboardHintItemViewModel("D", "kill"));
                }
                break;

            case "Ingresses":
                hints.Add(new AksKeyboardHintItemViewModel("Enter", "open URL"));
                hints.Add(new AksKeyboardHintItemViewModel("I", "inspect"));
                hints.Add(new AksKeyboardHintItemViewModel("Y", "yaml"));
                break;

            case "Helm":
                hints.Add(new AksKeyboardHintItemViewModel("Enter", "history"));
                hints.Add(new AksKeyboardHintItemViewModel("Y", "yaml"));
                hints.Add(new AksKeyboardHintItemViewModel("H", "history"));
                hints.Add(new AksKeyboardHintItemViewModel("V", "values"));
                break;

            case "CronJobs":
                hints.Add(new AksKeyboardHintItemViewModel("Enter", "yaml"));
                hints.Add(new AksKeyboardHintItemViewModel("Y", "yaml"));
                hints.Add(new AksKeyboardHintItemViewModel("R", "trigger", selectedResource?.CanTrigger != true));
                break;

            default:
                if (selectedResource is not null && CanOpenSelectedResourceYaml)
                {
                    hints.Add(new AksKeyboardHintItemViewModel("Enter", "default action"));
                    hints.Add(new AksKeyboardHintItemViewModel("Y", "yaml"));
                }
                break;
        }

        hints.Add(new AksKeyboardHintItemViewModel("/", "filter"));
        hints.Add(new AksKeyboardHintItemViewModel("Esc", "deselect"));
        return hints;
    }

    private void NotifyKeyboardShortcutStateChanged()
    {
        OnPropertyChanged(nameof(KeyboardHints));
        OnPropertyChanged(nameof(KeyboardHintsVisibility));
    }
}

public sealed record AksKeyboardHintItemViewModel(string Key, string Description, bool IsDimmed = false)
{
    public double Opacity => IsDimmed ? 0.55 : 1d;

    public string Label => $"{Key} {Description}";
}
