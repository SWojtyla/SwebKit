using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SwebKit.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace SwebKit.WinUI.ViewModels.Aks;

public sealed partial class AksPageViewModel
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();

    private string _loadedSelectedResourceYaml = string.Empty;

    [ObservableProperty]
    public partial bool IsSelectedResourceYamlPanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsSelectedResourceYamlLoading { get; set; }

    [ObservableProperty]
    public partial bool IsSelectedResourceYamlApplying { get; set; }

    [ObservableProperty]
    public partial bool IsSelectedResourceYamlEditorOpen { get; set; }

    [ObservableProperty]
    public partial string SelectedResourceYamlText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SelectedResourceYamlErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsSelectedResourceDiagnosticsPanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsSelectedResourceDiagnosticsLoading { get; set; }

    [ObservableProperty]
    public partial string SelectedResourceDiagnosticsTitle { get; set; } = "Diagnostics";

    [ObservableProperty]
    public partial string SelectedResourceDiagnosticsSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<AksResourceFactItemViewModel> SelectedResourceDiagnosticsFacts { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> SelectedResourceDiagnosticsHighlights { get; set; } = [];

    [ObservableProperty]
    public partial string? SelectedResourceDiagnosticsErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsSelectedResourceMutationRunning { get; set; }

    [ObservableProperty]
    public partial string SelectedResourceScaleReplicaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SelectedResourceScaleErrorMessage { get; set; }

    [ObservableProperty]
    public partial string SelectedResourceConfirmText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SelectedResourceActionErrorMessage { get; set; }

    public Visibility SelectedResourceActionBarVisibility => SelectedResourceItem is null ? Visibility.Collapsed : Visibility.Visible;

    public string SelectedResourceYamlButtonText => IsSelectedResourceYamlPanelOpen ? "Reload YAML" : "Load YAML";

    public string SelectedResourceYamlTitle => SelectedResourceItem is null ? "Resource YAML" : $"YAML · {SelectedResourceItem.Name}";

    public string SelectedResourceYamlStatus => SelectedResourceItem is null
        ? string.Empty
        : $"{SelectedResourceItem.ApiKind} · {FormatResourceScope(SelectedResourceItem)}";

    public Visibility SelectedResourceYamlPanelVisibility =>
        IsSelectedResourceYamlPanelOpen || IsSelectedResourceYamlLoading || !string.IsNullOrWhiteSpace(SelectedResourceYamlErrorMessage)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility SelectedResourceYamlErrorVisibility => string.IsNullOrWhiteSpace(SelectedResourceYamlErrorMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public bool CanOpenSelectedResourceYaml => SelectedResourceItem is not null
        && Client is not null
        && !IsSelectedResourceYamlLoading
        && !IsSelectedResourceYamlApplying;

    public bool CanStartSelectedResourceYamlEdit => SelectedResourceItem?.CanEditYaml == true
        && IsSelectedResourceYamlPanelOpen
        && !IsSelectedResourceYamlLoading
        && !IsSelectedResourceYamlApplying
        && !IsSelectedResourceYamlEditorOpen
        && !string.IsNullOrWhiteSpace(_loadedSelectedResourceYaml);

    public Visibility SelectedResourceYamlEditVisibility => SelectedResourceItem?.CanEditYaml == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SelectedResourceYamlEditorActionsVisibility => IsSelectedResourceYamlEditorOpen
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool IsSelectedResourceYamlReadOnly => !IsSelectedResourceYamlEditorOpen || IsSelectedResourceYamlLoading || IsSelectedResourceYamlApplying;

    public bool HasSelectedResourceYamlChanges => !string.Equals(SelectedResourceYamlText, _loadedSelectedResourceYaml, StringComparison.Ordinal);

    public bool CanApplySelectedResourceYaml => IsSelectedResourceYamlEditorOpen
        && !IsSelectedResourceYamlLoading
        && !IsSelectedResourceYamlApplying
        && HasSelectedResourceYamlChanges
        && CanExecuteSelectedResourceMutation;

    public bool CanDiscardSelectedResourceYamlChanges => IsSelectedResourceYamlEditorOpen && !IsSelectedResourceYamlApplying;

    public bool CanAnalyzeSelectedResource => SelectedResourceItem is not null
        && Client is not null
        && !IsSelectedResourceDiagnosticsLoading
        && (SelectedResourceItem.CanAnalyzeIngress || SelectedResourceItem.CanAnalyzeNetworkPolicies);

    public string SelectedResourceAnalyzeLabel => SelectedResourceItem?.CanAnalyzeIngress == true ? "Analyze ingress" : "Analyze network";

    public Visibility SelectedResourceAnalyzeVisibility => CanAnalyzeSelectedResource ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SelectedResourceDiagnosticsPanelVisibility =>
        IsSelectedResourceDiagnosticsPanelOpen || IsSelectedResourceDiagnosticsLoading || !string.IsNullOrWhiteSpace(SelectedResourceDiagnosticsErrorMessage)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility SelectedResourceDiagnosticsErrorVisibility => string.IsNullOrWhiteSpace(SelectedResourceDiagnosticsErrorMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility SelectedResourceDiagnosticsSummaryVisibility => string.IsNullOrWhiteSpace(SelectedResourceDiagnosticsSummary)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility SelectedResourceDiagnosticsHighlightsVisibility => SelectedResourceDiagnosticsHighlights.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility SelectedResourceRestartVisibility => SelectedResourceItem?.CanRestart == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanRestartSelectedResource => SelectedResourceItem?.CanRestart == true
        && !IsSelectedResourceMutationRunning
        && CanExecuteSelectedResourceMutation;

    public Visibility SelectedResourceScaleVisibility => SelectedResourceItem?.CanScale == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanScaleSelectedResource => SelectedResourceItem?.CanScale == true
        && !IsSelectedResourceMutationRunning
        && CanExecuteSelectedResourceMutation
        && TryParseSelectedResourceScaleReplica(out _, out _);

    public string SelectedResourceScaleHint => SelectedResourceItem?.ScaleReplicaCount is int replicas
        ? $"Current replicas · {replicas}"
        : "Provide the desired replica count.";

    public Visibility SelectedResourceScaleErrorVisibility => string.IsNullOrWhiteSpace(SelectedResourceScaleErrorMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility SelectedResourceTriggerVisibility => SelectedResourceItem?.CanTrigger == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanTriggerSelectedResource => SelectedResourceItem?.CanTrigger == true
        && !IsSelectedResourceMutationRunning
        && CanExecuteSelectedResourceMutation;

    public Visibility SelectedResourceRerunVisibility => SelectedResourceItem?.CanRerun == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanRerunSelectedResource => SelectedResourceItem?.CanRerun == true
        && !IsSelectedResourceMutationRunning
        && CanExecuteSelectedResourceMutation;

    public bool SelectedResourceActionRequiresConfirm => _appState.Config.IsProduction
        && SelectedResourceItem is not null
        && (SelectedResourceItem.CanRestart
            || SelectedResourceItem.CanScale
            || SelectedResourceItem.CanTrigger
            || SelectedResourceItem.CanRerun
            || IsSelectedResourceYamlEditorOpen);

    public Visibility SelectedResourceConfirmVisibility => SelectedResourceActionRequiresConfirm
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool ShowSelectedResourceConfirmMessage => SelectedResourceActionRequiresConfirm;

    public string SelectedResourceConfirmationMessage => IsSelectedResourceYamlEditorOpen
        ? "Type CONFIRM before applying YAML changes to the production cluster."
        : "Type CONFIRM before running this production AKS action.";

    public bool CanExecuteSelectedResourceMutation => !SelectedResourceActionRequiresConfirm
        || string.Equals(SelectedResourceConfirmText, "CONFIRM", StringComparison.Ordinal);

    public Visibility SelectedResourceActionErrorVisibility => string.IsNullOrWhiteSpace(SelectedResourceActionErrorMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;

    partial void OnSelectedResourceYamlTextChanged(string value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceYamlErrorMessageChanged(string? value) => NotifySelectedResourceActionStateChanged();

    partial void OnIsSelectedResourceYamlPanelOpenChanged(bool value) => NotifySelectedResourceActionStateChanged();

    partial void OnIsSelectedResourceYamlLoadingChanged(bool value) => NotifySelectedResourceActionStateChanged();

    partial void OnIsSelectedResourceYamlApplyingChanged(bool value) => NotifySelectedResourceActionStateChanged();

    partial void OnIsSelectedResourceYamlEditorOpenChanged(bool value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceDiagnosticsErrorMessageChanged(string? value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceDiagnosticsSummaryChanged(string value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceDiagnosticsHighlightsChanged(IReadOnlyList<string> value) => NotifySelectedResourceActionStateChanged();

    partial void OnIsSelectedResourceDiagnosticsPanelOpenChanged(bool value) => NotifySelectedResourceActionStateChanged();

    partial void OnIsSelectedResourceDiagnosticsLoadingChanged(bool value) => NotifySelectedResourceActionStateChanged();

    partial void OnIsSelectedResourceMutationRunningChanged(bool value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceScaleReplicaTextChanged(string value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceScaleErrorMessageChanged(string? value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceConfirmTextChanged(string value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceActionErrorMessageChanged(string? value) => NotifySelectedResourceActionStateChanged();

    [RelayCommand]
    private async Task OpenSelectedResourceYamlAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null)
        {
            return;
        }

        SelectedResourceActionErrorMessage = null;
        SelectedResourceYamlErrorMessage = null;
        IsSelectedResourceYamlPanelOpen = true;
        IsSelectedResourceYamlLoading = true;
        IsSelectedResourceYamlEditorOpen = false;
        SelectedResourceYamlText = string.Empty;
        _loadedSelectedResourceYaml = string.Empty;

        try
        {
            var yaml = await Client.GetResourceYamlAsync(resource.Namespace, resource.ApiKind, resource.Name);
            _loadedSelectedResourceYaml = yaml;
            SelectedResourceYamlText = yaml;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SelectedResourceYamlErrorMessage = ex.Message;
            _notifications.ShowError("AKS YAML load failed", ex.Message, ex);
        }
        finally
        {
            IsSelectedResourceYamlLoading = false;
        }
    }

    [RelayCommand]
    private void CloseSelectedResourceYaml()
    {
        IsSelectedResourceYamlPanelOpen = false;
        IsSelectedResourceYamlLoading = false;
        IsSelectedResourceYamlApplying = false;
        IsSelectedResourceYamlEditorOpen = false;
        SelectedResourceYamlErrorMessage = null;
        SelectedResourceYamlText = string.Empty;
        _loadedSelectedResourceYaml = string.Empty;
    }

    [RelayCommand]
    private void EditSelectedResourceYaml()
    {
        if (!CanStartSelectedResourceYamlEdit)
        {
            return;
        }

        SelectedResourceActionErrorMessage = null;
        SelectedResourceYamlErrorMessage = null;
        IsSelectedResourceYamlEditorOpen = true;
        SelectedResourceYamlText = _loadedSelectedResourceYaml;
    }

    [RelayCommand]
    private void DiscardSelectedResourceYamlChanges()
    {
        if (!CanDiscardSelectedResourceYamlChanges)
        {
            return;
        }

        SelectedResourceActionErrorMessage = null;
        SelectedResourceYamlErrorMessage = null;
        SelectedResourceYamlText = _loadedSelectedResourceYaml;
        IsSelectedResourceYamlEditorOpen = false;
        SelectedResourceConfirmText = string.Empty;
    }

    [RelayCommand]
    private async Task ApplySelectedResourceYamlAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null)
        {
            return;
        }

        if (!CanExecuteSelectedResourceMutation)
        {
            SelectedResourceActionErrorMessage = "Type CONFIRM before applying production YAML changes.";
            return;
        }

        var yamlValidationError = ValidateYaml(SelectedResourceYamlText);
        if (yamlValidationError is not null)
        {
            SelectedResourceYamlErrorMessage = yamlValidationError;
            return;
        }

        SelectedResourceActionErrorMessage = null;
        SelectedResourceYamlErrorMessage = null;
        IsSelectedResourceYamlApplying = true;

        try
        {
            await Client.ApplyResourceYamlAsync(resource.Namespace, resource.ApiKind, resource.Name, SelectedResourceYamlText);
            _loadedSelectedResourceYaml = SelectedResourceYamlText;
            IsSelectedResourceYamlEditorOpen = false;
            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("AKS YAML applied", $"{resource.ApiKind} {resource.Name}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SelectedResourceYamlErrorMessage = ex.Message;
            _notifications.ShowError("AKS YAML apply failed", ex.Message, ex);
        }
        finally
        {
            IsSelectedResourceYamlApplying = false;
        }
    }

    [RelayCommand]
    private async Task AnalyzeSelectedResourceAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null)
        {
            return;
        }

        SelectedResourceDiagnosticsErrorMessage = null;
        SelectedResourceDiagnosticsSummary = string.Empty;
        SelectedResourceDiagnosticsFacts = [];
        SelectedResourceDiagnosticsHighlights = [];
        IsSelectedResourceDiagnosticsPanelOpen = true;
        IsSelectedResourceDiagnosticsLoading = true;

        try
        {
            if (resource.CanAnalyzeIngress)
            {
                var ingressAnalysis = await Client.AnalyzeIngressAsync(resource.Namespace, resource.Name);
                ApplyIngressAnalysis(resource, ingressAnalysis);
            }
            else if (resource.CanAnalyzeNetworkPolicies)
            {
                var workloadKind = resource.NetworkPolicyKind ?? resource.ApiKind;
                var networkAnalysis = await Client.AnalyzeNetworkPoliciesAsync(resource.Namespace, workloadKind, resource.Name);
                ApplyNetworkPolicyAnalysis(resource, networkAnalysis);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SelectedResourceDiagnosticsErrorMessage = ex.Message;
            _notifications.ShowError("AKS analysis failed", ex.Message, ex);
        }
        finally
        {
            IsSelectedResourceDiagnosticsLoading = false;
        }
    }

    [RelayCommand]
    private void CloseSelectedResourceDiagnostics()
    {
        IsSelectedResourceDiagnosticsPanelOpen = false;
        IsSelectedResourceDiagnosticsLoading = false;
        SelectedResourceDiagnosticsErrorMessage = null;
        SelectedResourceDiagnosticsTitle = "Diagnostics";
        SelectedResourceDiagnosticsSummary = string.Empty;
        SelectedResourceDiagnosticsFacts = [];
        SelectedResourceDiagnosticsHighlights = [];
    }

    [RelayCommand]
    private async Task RestartSelectedResourceAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !resource.CanRestart)
        {
            return;
        }

        if (!CanExecuteSelectedResourceMutation)
        {
            SelectedResourceActionErrorMessage = "Type CONFIRM before running a production AKS action.";
            return;
        }

        SelectedResourceActionErrorMessage = null;
        IsSelectedResourceMutationRunning = true;

        try
        {
            if (string.Equals(resource.ApiKind, "Deployment", StringComparison.Ordinal))
            {
                await Client.RestartDeploymentAsync(resource.Namespace, resource.Name);
            }
            else
            {
                await Client.RestartStatefulSetAsync(resource.Namespace, resource.Name);
            }

            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("AKS workload restart queued", $"{resource.ApiKind} {resource.Name}");
            await LoadResourceScopeAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SelectedResourceActionErrorMessage = ex.Message;
            _notifications.ShowError("AKS workload restart failed", ex.Message, ex);
        }
        finally
        {
            IsSelectedResourceMutationRunning = false;
        }
    }

    [RelayCommand]
    private async Task ScaleSelectedResourceAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !resource.CanScale)
        {
            return;
        }

        if (!TryParseSelectedResourceScaleReplica(out var replicas, out var error))
        {
            SelectedResourceScaleErrorMessage = error;
            return;
        }

        if (!CanExecuteSelectedResourceMutation)
        {
            SelectedResourceActionErrorMessage = "Type CONFIRM before running a production AKS action.";
            return;
        }

        SelectedResourceActionErrorMessage = null;
        SelectedResourceScaleErrorMessage = null;
        IsSelectedResourceMutationRunning = true;

        try
        {
            if (string.Equals(resource.ApiKind, "Deployment", StringComparison.Ordinal))
            {
                await Client.ScaleDeploymentAsync(resource.Namespace, resource.Name, replicas);
            }
            else
            {
                await Client.ScaleStatefulSetAsync(resource.Namespace, resource.Name, replicas);
            }

            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("AKS workload scaled", $"{resource.ApiKind} {resource.Name} -> {replicas}");
            await LoadResourceScopeAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SelectedResourceActionErrorMessage = ex.Message;
            _notifications.ShowError("AKS scale action failed", ex.Message, ex);
        }
        finally
        {
            IsSelectedResourceMutationRunning = false;
        }
    }

    [RelayCommand]
    private async Task TriggerSelectedResourceAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !resource.CanTrigger)
        {
            return;
        }

        if (!CanExecuteSelectedResourceMutation)
        {
            SelectedResourceActionErrorMessage = "Type CONFIRM before running a production AKS action.";
            return;
        }

        SelectedResourceActionErrorMessage = null;
        IsSelectedResourceMutationRunning = true;

        try
        {
            var createdJobName = await Client.TriggerCronJobAsync(resource.Namespace, resource.Name);
            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("CronJob triggered", createdJobName);
            await LoadResourceScopeAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SelectedResourceActionErrorMessage = ex.Message;
            _notifications.ShowError("CronJob trigger failed", ex.Message, ex);
        }
        finally
        {
            IsSelectedResourceMutationRunning = false;
        }
    }

    [RelayCommand]
    private async Task RerunSelectedResourceAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !resource.CanRerun)
        {
            return;
        }

        if (!CanExecuteSelectedResourceMutation)
        {
            SelectedResourceActionErrorMessage = "Type CONFIRM before running a production AKS action.";
            return;
        }

        SelectedResourceActionErrorMessage = null;
        IsSelectedResourceMutationRunning = true;

        try
        {
            var createdJobName = await Client.RerunJobAsync(resource.Namespace, resource.Name);
            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("Job rerun started", createdJobName);
            await LoadResourceScopeAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SelectedResourceActionErrorMessage = ex.Message;
            _notifications.ShowError("Job rerun failed", ex.Message, ex);
        }
        finally
        {
            IsSelectedResourceMutationRunning = false;
        }
    }

    private void ResetSelectedResourceActionState(AksResourceBrowseItemViewModel? resource)
    {
        _loadedSelectedResourceYaml = string.Empty;
        IsSelectedResourceYamlPanelOpen = false;
        IsSelectedResourceYamlLoading = false;
        IsSelectedResourceYamlApplying = false;
        IsSelectedResourceYamlEditorOpen = false;
        SelectedResourceYamlText = string.Empty;
        SelectedResourceYamlErrorMessage = null;

        IsSelectedResourceDiagnosticsPanelOpen = false;
        IsSelectedResourceDiagnosticsLoading = false;
        SelectedResourceDiagnosticsTitle = "Diagnostics";
        SelectedResourceDiagnosticsSummary = string.Empty;
        SelectedResourceDiagnosticsFacts = [];
        SelectedResourceDiagnosticsHighlights = [];
        SelectedResourceDiagnosticsErrorMessage = null;

        IsSelectedResourceMutationRunning = false;
        SelectedResourceScaleReplicaText = resource?.ScaleReplicaCount?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        SelectedResourceScaleErrorMessage = null;
        SelectedResourceConfirmText = string.Empty;
        SelectedResourceActionErrorMessage = null;

        NotifySelectedResourceActionStateChanged();
    }

    private void ApplyIngressAnalysis(AksResourceBrowseItemViewModel resource, IngressAnalysis analysis)
    {
        SelectedResourceDiagnosticsTitle = $"Ingress analysis · {resource.Name}";
        SelectedResourceDiagnosticsSummary = string.IsNullOrWhiteSpace(analysis.Summary)
            ? "The ingress analysis returned no summary text. Review addresses, backends, and findings below."
            : analysis.Summary;
        SelectedResourceDiagnosticsFacts =
        [
            new AksResourceFactItemViewModel("Namespace", analysis.Namespace),
            new AksResourceFactItemViewModel("Ingress class", string.IsNullOrWhiteSpace(analysis.IngressClass) ? "—" : analysis.IngressClass),
            new AksResourceFactItemViewModel("Addresses", analysis.Addresses.Count.ToString(CultureInfo.CurrentCulture)),
            new AksResourceFactItemViewModel("Backends", analysis.Backends.Count.ToString(CultureInfo.CurrentCulture)),
            new AksResourceFactItemViewModel("Findings", analysis.Findings.Count.ToString(CultureInfo.CurrentCulture)),
        ];

        var highlights = new List<string>();
        highlights.AddRange(analysis.Findings.Select(finding => $"Finding · {finding}"));
        highlights.AddRange(analysis.Addresses.Select(address => $"Address · {address}"));
        foreach (var backend in analysis.Backends)
        {
            var serviceTarget = string.IsNullOrWhiteSpace(backend.ServiceName)
                ? "no backend service"
                : string.IsNullOrWhiteSpace(backend.ServiceNamespace)
                    ? backend.ServiceName
                    : $"{backend.ServiceNamespace}/{backend.ServiceName}";
            highlights.Add($"Backend · {backend.Host}{backend.Path} -> {serviceTarget} ({backend.RequestedPort})");
            highlights.AddRange(backend.Findings.Select(finding => $"Backend finding · {finding}"));
        }

        highlights.Add($"Limitation · {analysis.Limitation}");
        SelectedResourceDiagnosticsHighlights = highlights;
    }

    private void ApplyNetworkPolicyAnalysis(AksResourceBrowseItemViewModel resource, NetworkPolicyAnalysis analysis)
    {
        SelectedResourceDiagnosticsTitle = $"Network analysis · {resource.Name}";
        SelectedResourceDiagnosticsSummary = string.IsNullOrWhiteSpace(analysis.Summary)
            ? "The network policy analysis returned no summary text. Review selector coverage, services, and policies below."
            : analysis.Summary;
        SelectedResourceDiagnosticsFacts =
        [
            new AksResourceFactItemViewModel("Namespace", analysis.Namespace),
            new AksResourceFactItemViewModel("Matching pods", analysis.MatchingPodCount.ToString(CultureInfo.CurrentCulture)),
            new AksResourceFactItemViewModel("Services", analysis.Services.Count.ToString(CultureInfo.CurrentCulture)),
            new AksResourceFactItemViewModel("Policies", analysis.Policies.Count.ToString(CultureInfo.CurrentCulture)),
            new AksResourceFactItemViewModel("Ingress isolated", analysis.IngressIsolated ? "Yes" : "No"),
            new AksResourceFactItemViewModel("Egress isolated", analysis.EgressIsolated ? "Yes" : "No"),
        ];

        var highlights = new List<string>();
        highlights.AddRange(analysis.Findings.Select(finding => $"Finding · {finding}"));
        highlights.AddRange(analysis.SelectorLabels.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"Selector · {pair.Key}={pair.Value}"));
        highlights.AddRange(analysis.Services.Select(service => $"Service · {service}"));
        highlights.AddRange(analysis.ExposedByIngresses.Select(ingress => $"Ingress exposure · {ingress}"));
        highlights.AddRange(analysis.ExposedByHttpRoutes.Select(route => $"HTTPRoute exposure · {route}"));
        highlights.AddRange(analysis.MatchingPods.Select(pod => $"Matching pod · {pod}"));
        foreach (var policy in analysis.Policies)
        {
            var policyTypes = policy.PolicyTypes.Count == 0 ? "unspecified policy types" : string.Join(", ", policy.PolicyTypes);
            highlights.Add($"Policy · {policy.Name} ({policyTypes})");
            highlights.AddRange(policy.IngressRules.Select(rule => $"Ingress rule · {rule}"));
            highlights.AddRange(policy.EgressRules.Select(rule => $"Egress rule · {rule}"));
        }

        highlights.Add($"Limitation · {analysis.Limitation}");
        SelectedResourceDiagnosticsHighlights = highlights;
    }

    private bool TryParseSelectedResourceScaleReplica(out int replicas, out string? error)
    {
        replicas = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(SelectedResourceScaleReplicaText))
        {
            error = "Enter the desired replica count before scaling the workload.";
            return false;
        }

        if (!int.TryParse(SelectedResourceScaleReplicaText, NumberStyles.Integer, CultureInfo.CurrentCulture, out replicas) || replicas < 0)
        {
            error = "Replica count must be a whole number greater than or equal to zero.";
            return false;
        }

        return true;
    }

    private void NotifySelectedResourceActionStateChanged()
    {
        OnPropertyChanged(nameof(SelectedResourceActionBarVisibility));
        OnPropertyChanged(nameof(SelectedResourceYamlButtonText));
        OnPropertyChanged(nameof(SelectedResourceYamlTitle));
        OnPropertyChanged(nameof(SelectedResourceYamlStatus));
        OnPropertyChanged(nameof(SelectedResourceYamlPanelVisibility));
        OnPropertyChanged(nameof(SelectedResourceYamlErrorVisibility));
        OnPropertyChanged(nameof(CanOpenSelectedResourceYaml));
        OnPropertyChanged(nameof(CanStartSelectedResourceYamlEdit));
        OnPropertyChanged(nameof(SelectedResourceYamlEditVisibility));
        OnPropertyChanged(nameof(SelectedResourceYamlEditorActionsVisibility));
        OnPropertyChanged(nameof(IsSelectedResourceYamlReadOnly));
        OnPropertyChanged(nameof(HasSelectedResourceYamlChanges));
        OnPropertyChanged(nameof(CanApplySelectedResourceYaml));
        OnPropertyChanged(nameof(CanDiscardSelectedResourceYamlChanges));
        OnPropertyChanged(nameof(CanAnalyzeSelectedResource));
        OnPropertyChanged(nameof(SelectedResourceAnalyzeLabel));
        OnPropertyChanged(nameof(SelectedResourceAnalyzeVisibility));
        OnPropertyChanged(nameof(SelectedResourceDiagnosticsPanelVisibility));
        OnPropertyChanged(nameof(SelectedResourceDiagnosticsErrorVisibility));
        OnPropertyChanged(nameof(SelectedResourceDiagnosticsSummaryVisibility));
        OnPropertyChanged(nameof(SelectedResourceDiagnosticsHighlightsVisibility));
        OnPropertyChanged(nameof(SelectedResourceRestartVisibility));
        OnPropertyChanged(nameof(CanRestartSelectedResource));
        OnPropertyChanged(nameof(SelectedResourceScaleVisibility));
        OnPropertyChanged(nameof(CanScaleSelectedResource));
        OnPropertyChanged(nameof(SelectedResourceScaleHint));
        OnPropertyChanged(nameof(SelectedResourceScaleErrorVisibility));
        OnPropertyChanged(nameof(SelectedResourceTriggerVisibility));
        OnPropertyChanged(nameof(CanTriggerSelectedResource));
        OnPropertyChanged(nameof(SelectedResourceRerunVisibility));
        OnPropertyChanged(nameof(CanRerunSelectedResource));
        OnPropertyChanged(nameof(SelectedResourceActionRequiresConfirm));
        OnPropertyChanged(nameof(SelectedResourceConfirmVisibility));
        OnPropertyChanged(nameof(ShowSelectedResourceConfirmMessage));
        OnPropertyChanged(nameof(SelectedResourceConfirmationMessage));
        OnPropertyChanged(nameof(CanExecuteSelectedResourceMutation));
        OnPropertyChanged(nameof(SelectedResourceActionErrorVisibility));
    }

    private static string? ValidateYaml(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return "YAML cannot be empty.";
        }

        try
        {
            _ = YamlDeserializer.Deserialize<object>(yaml);
            return null;
        }
        catch (YamlException ex)
        {
            return $"YAML validation failed: {ex.Message}";
        }
    }

    private static string FormatResourceScope(AksResourceBrowseItemViewModel resource)
        => string.IsNullOrWhiteSpace(resource.Namespace) ? "cluster scope" : $"namespace {resource.Namespace}";
}