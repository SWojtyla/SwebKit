using System.Globalization;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SwebKit.Core.Models;
using Windows.ApplicationModel.DataTransfer;
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

    [ObservableProperty]
    public partial bool IsSelectedResourceHelmHistoryPanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsSelectedResourceHelmHistoryLoading { get; set; }

    [ObservableProperty]
    public partial bool IsSelectedResourceHelmRollbackMode { get; set; }

    [ObservableProperty]
    public partial string SelectedResourceHelmHistoryTitle { get; set; } = "Helm history";

    [ObservableProperty]
    public partial string SelectedResourceHelmHistorySummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<AksHelmRevisionItemViewModel> SelectedResourceHelmHistoryItems { get; set; } = [];

    [ObservableProperty]
    public partial string? SelectedResourceHelmHistoryErrorMessage { get; set; }

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

    public Visibility SelectedResourceOpenUrlVisibility => CanOpenSelectedResourceUrl ? Visibility.Visible : Visibility.Collapsed;

    public bool CanOpenSelectedResourceUrl => !string.IsNullOrWhiteSpace(SelectedResourceItem?.PrimaryUrl);

    public Visibility SelectedResourceWorkloadLogsVisibility => CanSelectedResourceSupportWorkloadLogs
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanOpenSelectedResourceWorkloadLogs => CanSelectedResourceSupportWorkloadLogs
        && Client is not null
        && !IsSelectedWorkloadLogsLoading
        && !IsLoading;

    public string SelectedResourceWorkloadLogsLabel => SelectedResourceItem?.ApiKind switch
    {
        "StatefulSet" => "StatefulSet logs",
        _ => "All-pod logs"
    };

    public Visibility SelectedResourceCopyUrlVisibility => CanOpenSelectedResourceUrl ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SelectedResourceNamespaceQuotaVisibility => SelectedResourceItem is not null
        && !string.IsNullOrWhiteSpace(SelectedResourceItem.Namespace)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanLoadSelectedResourceNamespaceQuotas => SelectedResourceItem is not null
        && !string.IsNullOrWhiteSpace(SelectedResourceItem.Namespace)
        && Client is not null
        && !IsSelectedResourceDiagnosticsLoading;

    public Visibility SelectedResourcePodDisruptionBudgetVisibility => SelectedResourceNamespaceQuotaVisibility;

    public bool CanLoadSelectedResourcePodDisruptionBudgets => CanLoadSelectedResourceNamespaceQuotas;

    public Visibility SelectedResourceProbeFailuresVisibility => CanInspectSelectedResourceProbeFailures
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanLoadSelectedResourceProbeFailures => CanInspectSelectedResourceProbeFailures && !IsSelectedResourceDiagnosticsLoading;

    public Visibility SelectedResourcePlacementVisibility => CanInspectSelectedResourcePlacement
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanLoadSelectedResourcePlacement => CanInspectSelectedResourcePlacement && !IsSelectedResourceDiagnosticsLoading;

    public Visibility SelectedResourceHelmValuesVisibility => IsSelectedResourceHelmRelease
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanOpenSelectedResourceHelmValues => IsSelectedResourceHelmRelease
        && Client is not null
        && !IsSelectedResourceDiagnosticsLoading;

    public Visibility SelectedResourceHelmHistoryVisibility => IsSelectedResourceHelmRelease
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanOpenSelectedResourceHelmHistory => IsSelectedResourceHelmRelease
        && Client is not null
        && !IsSelectedResourceHelmHistoryLoading;

    public Visibility SelectedResourceHelmPreviewVisibility => IsSelectedResourceHelmRelease
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanPreviewSelectedResourceHelmUpgrade => IsSelectedResourceHelmRelease
        && Client is not null
        && !IsSelectedResourceDiagnosticsLoading;

    public Visibility SelectedResourceHelmRollbackVisibility => IsSelectedResourceHelmRelease
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanOpenSelectedResourceHelmRollback => IsSelectedResourceHelmRelease
        && Client is not null
        && !IsSelectedResourceHelmHistoryLoading
        && !IsSelectedResourceMutationRunning;

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

    public Visibility SelectedResourceDeleteVisibility => SelectedResourceItem?.CanDelete == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanDeleteSelectedResource => SelectedResourceItem?.CanDelete == true
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
            || SelectedResourceItem.CanDelete
            || SelectedResourceItem.CanScale
            || SelectedResourceItem.CanTrigger
            || SelectedResourceItem.CanRerun
            || IsSelectedResourceHelmRollbackMode
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

    public Visibility SelectedResourceHelmHistoryPanelVisibility =>
        IsSelectedResourceHelmHistoryPanelOpen || IsSelectedResourceHelmHistoryLoading || !string.IsNullOrWhiteSpace(SelectedResourceHelmHistoryErrorMessage)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility SelectedResourceHelmHistoryErrorVisibility => string.IsNullOrWhiteSpace(SelectedResourceHelmHistoryErrorMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility SelectedResourceHelmHistorySummaryVisibility => string.IsNullOrWhiteSpace(SelectedResourceHelmHistorySummary)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility SelectedResourceHelmHistoryContentVisibility => SelectedResourceHelmHistoryItems.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public bool ShowSelectedResourceHelmHistoryEmptyState => IsSelectedResourceHelmHistoryPanelOpen
        && !IsSelectedResourceHelmHistoryLoading
        && string.IsNullOrWhiteSpace(SelectedResourceHelmHistoryErrorMessage)
        && SelectedResourceHelmHistoryItems.Count == 0;

    public Visibility SelectedResourceHelmRollbackActionVisibility => IsSelectedResourceHelmRollbackMode
        ? Visibility.Visible
        : Visibility.Collapsed;

    private bool IsSelectedResourceHelmRelease => SelectedResourceItem is not null
        && string.Equals(SelectedResourceItem.Kind, "Helm", StringComparison.Ordinal);

    private bool CanSelectedResourceSupportWorkloadLogs => SelectedResourceItem is not null
        && CanResourceSupportWorkloadLogs(SelectedResourceItem);

    private bool CanInspectSelectedResourceProbeFailures => SelectedResourceItem is not null
        && Client is not null
        && (string.Equals(SelectedResourceItem.ApiKind, "Deployment", StringComparison.Ordinal)
            || string.Equals(SelectedResourceItem.ApiKind, "StatefulSet", StringComparison.Ordinal));

    private bool CanInspectSelectedResourcePlacement => CanInspectSelectedResourceProbeFailures;

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

    partial void OnIsSelectedResourceHelmHistoryPanelOpenChanged(bool value) => NotifySelectedResourceActionStateChanged();

    partial void OnIsSelectedResourceHelmHistoryLoadingChanged(bool value) => NotifySelectedResourceActionStateChanged();

    partial void OnIsSelectedResourceHelmRollbackModeChanged(bool value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceHelmHistoryTitleChanged(string value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceHelmHistorySummaryChanged(string value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceHelmHistoryItemsChanged(IReadOnlyList<AksHelmRevisionItemViewModel> value) => NotifySelectedResourceActionStateChanged();

    partial void OnSelectedResourceHelmHistoryErrorMessageChanged(string? value) => NotifySelectedResourceActionStateChanged();

    [RelayCommand]
    private Task OpenSelectedResourceUrlAsync()
    {
        var url = SelectedResourceItem?.PrimaryUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.CompletedTask;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
            _notifications.ShowSuccess("AKS URL opened", url);
        }
        catch (Exception ex)
        {
            _notifications.ShowError("AKS URL open failed", ex.Message, ex);
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task CopySelectedResourceUrlAsync()
    {
        var url = SelectedResourceItem?.PrimaryUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.CompletedTask;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(url);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            _notifications.ShowSuccess("AKS URL copied", url);
        }
        catch (Exception ex)
        {
            _notifications.ShowError("AKS URL copy failed", ex.Message, ex);
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenSelectedResourceYamlAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null)
        {
            return;
        }

        var actionToken = _selectedResourceActionCts.Token;

        SelectedResourceActionErrorMessage = null;
        SelectedResourceYamlErrorMessage = null;
        IsSelectedResourceYamlPanelOpen = true;
        IsSelectedResourceYamlLoading = true;
        IsSelectedResourceYamlEditorOpen = false;
        SelectedResourceYamlText = string.Empty;
        _loadedSelectedResourceYaml = string.Empty;

        try
        {
            var yaml = await Client.GetResourceYamlAsync(resource.Namespace, resource.ApiKind, resource.Name, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            _loadedSelectedResourceYaml = yaml;
            SelectedResourceYamlText = yaml;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceYamlErrorMessage = ex.Message;
                _notifications.ShowError("AKS YAML load failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceYamlLoading = false;
            }
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

        var actionToken = _selectedResourceActionCts.Token;

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
            await Client.ApplyResourceYamlAsync(resource.Namespace, resource.ApiKind, resource.Name, SelectedResourceYamlText, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            _loadedSelectedResourceYaml = SelectedResourceYamlText;
            IsSelectedResourceYamlEditorOpen = false;
            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("AKS YAML applied", $"{resource.ApiKind} {resource.Name}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceYamlErrorMessage = ex.Message;
                _notifications.ShowError("AKS YAML apply failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceYamlApplying = false;
            }
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

        var actionToken = BeginSelectedResourceDiagnosticsLoad();

        try
        {
            if (resource.CanAnalyzeIngress)
            {
                var ingressAnalysis = await Client.AnalyzeIngressAsync(resource.Namespace, resource.Name, actionToken);
                ThrowIfSelectedResourceActionCanceled(actionToken);
                ApplyIngressAnalysis(resource, ingressAnalysis);
            }
            else if (resource.CanAnalyzeNetworkPolicies)
            {
                var workloadKind = resource.NetworkPolicyKind ?? resource.ApiKind;
                var networkAnalysis = await Client.AnalyzeNetworkPoliciesAsync(resource.Namespace, workloadKind, resource.Name, actionToken);
                ThrowIfSelectedResourceActionCanceled(actionToken);
                ApplyNetworkPolicyAnalysis(resource, networkAnalysis);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceDiagnosticsErrorMessage = ex.Message;
                _notifications.ShowError("AKS analysis failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceDiagnosticsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task OpenSelectedResourceNamespaceQuotasAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || string.IsNullOrWhiteSpace(resource.Namespace))
        {
            return;
        }

        var actionToken = BeginSelectedResourceDiagnosticsLoad();

        try
        {
            var quotasTask = Client.GetResourceQuotasAsync(resource.Namespace, actionToken);
            var limitRangesTask = Client.GetLimitRangesAsync(resource.Namespace, actionToken);
            await Task.WhenAll(quotasTask, limitRangesTask);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            ApplyNamespaceQuotaDiagnostics(resource.Namespace, quotasTask.Result, limitRangesTask.Result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceDiagnosticsErrorMessage = ex.Message;
                _notifications.ShowError("AKS namespace quota load failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceDiagnosticsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task OpenSelectedResourcePodDisruptionBudgetsAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || string.IsNullOrWhiteSpace(resource.Namespace))
        {
            return;
        }

        var actionToken = BeginSelectedResourceDiagnosticsLoad();

        try
        {
            var budgets = await Client.GetPodDisruptionBudgetsAsync(resource.Namespace, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            ApplyPodDisruptionBudgetDiagnostics(resource.Namespace, budgets);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceDiagnosticsErrorMessage = ex.Message;
                _notifications.ShowError("AKS pod disruption budget load failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceDiagnosticsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task OpenSelectedResourceProbeFailuresAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !CanInspectSelectedResourceProbeFailures)
        {
            return;
        }

        var actionToken = BeginSelectedResourceDiagnosticsLoad();

        try
        {
            var summary = await Client.GetProbeFailureSummaryAsync(resource.Namespace, resource.ApiKind, resource.Name, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            ApplyProbeFailureDiagnostics(resource, summary);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceDiagnosticsErrorMessage = ex.Message;
                _notifications.ShowError("AKS probe failure load failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceDiagnosticsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task OpenSelectedResourcePlacementAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !CanInspectSelectedResourcePlacement)
        {
            return;
        }

        var actionToken = BeginSelectedResourceDiagnosticsLoad();

        try
        {
            var analysis = await Client.GetPlacementAnalysisAsync(resource.Namespace, resource.ApiKind, resource.Name, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            ApplyPlacementDiagnostics(resource, analysis);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceDiagnosticsErrorMessage = ex.Message;
                _notifications.ShowError("AKS placement analysis failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceDiagnosticsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task OpenSelectedResourceHelmValuesAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !IsSelectedResourceHelmRelease)
        {
            return;
        }

        var actionToken = BeginSelectedResourceDiagnosticsLoad();

        try
        {
            var values = await Client.GetHelmReleaseValuesAsync(resource.Namespace, resource.Name, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            ApplyHelmValuesDiagnostics(resource, values);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceDiagnosticsErrorMessage = ex.Message;
                _notifications.ShowError("AKS Helm values load failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceDiagnosticsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task PreviewSelectedResourceHelmUpgradeAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !IsSelectedResourceHelmRelease)
        {
            return;
        }

        var actionToken = BeginSelectedResourceDiagnosticsLoad();

        try
        {
            var preview = await Client.PreviewHelmUpgradeAsync(resource.Namespace, resource.Name, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            ApplyHelmPreviewDiagnostics(resource, preview, $"Upgrade preview · {resource.Name}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceDiagnosticsErrorMessage = ex.Message;
                _notifications.ShowError("AKS Helm preview failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceDiagnosticsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task OpenSelectedResourceHelmHistoryAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !IsSelectedResourceHelmRelease)
        {
            return;
        }

        await LoadSelectedResourceHelmHistoryAsync(resource, rollbackMode: false);
    }

    [RelayCommand]
    private async Task OpenSelectedResourceHelmRollbackAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !IsSelectedResourceHelmRelease)
        {
            return;
        }

        await LoadSelectedResourceHelmHistoryAsync(resource, rollbackMode: true);
    }

    [RelayCommand]
    private void CloseSelectedResourceHelmHistory()
    {
        IsSelectedResourceHelmHistoryPanelOpen = false;
        IsSelectedResourceHelmHistoryLoading = false;
        IsSelectedResourceHelmRollbackMode = false;
        SelectedResourceHelmHistoryTitle = "Helm history";
        SelectedResourceHelmHistorySummary = string.Empty;
        SelectedResourceHelmHistoryItems = [];
        SelectedResourceHelmHistoryErrorMessage = null;
    }

    [RelayCommand]
    private async Task PreviewSelectedResourceHelmRollbackRevisionAsync(AksHelmRevisionItemViewModel? revision)
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || revision is null || !IsSelectedResourceHelmRelease)
        {
            return;
        }

        var actionToken = BeginSelectedResourceDiagnosticsLoad();

        try
        {
            var preview = await Client.PreviewHelmRollbackAsync(resource.Namespace, resource.Name, revision.Revision, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            ApplyHelmPreviewDiagnostics(resource, preview, $"Rollback preview · {resource.Name} → rev {revision.Revision}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceDiagnosticsErrorMessage = ex.Message;
                _notifications.ShowError("AKS Helm rollback preview failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceDiagnosticsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task RollbackSelectedResourceHelmRevisionAsync(AksHelmRevisionItemViewModel? revision)
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || revision is null || !IsSelectedResourceHelmRelease || !revision.CanRollbackTarget)
        {
            return;
        }

        var actionToken = _selectedResourceActionCts.Token;

        if (!CanExecuteSelectedResourceMutation)
        {
            SelectedResourceActionErrorMessage = "Type CONFIRM before running a production AKS action.";
            return;
        }

        SelectedResourceActionErrorMessage = null;
        IsSelectedResourceMutationRunning = true;

        try
        {
            await Client.RollbackHelmReleaseAsync(resource.Namespace, resource.Name, revision.Revision, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("Helm rollback complete", $"{resource.Name} -> revision {revision.Revision}");
            await LoadResourceScopeAsync(actionToken);
            await LoadSelectedResourceHelmHistoryAsync(resource, rollbackMode: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceActionErrorMessage = ex.Message;
                _notifications.ShowError("Helm rollback failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceMutationRunning = false;
            }
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

        var actionToken = _selectedResourceActionCts.Token;

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
                await Client.RestartDeploymentAsync(resource.Namespace, resource.Name, actionToken);
            }
            else
            {
                await Client.RestartStatefulSetAsync(resource.Namespace, resource.Name, actionToken);
            }

            ThrowIfSelectedResourceActionCanceled(actionToken);
            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("AKS workload restart queued", $"{resource.ApiKind} {resource.Name}");
            await LoadResourceScopeAsync(actionToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceActionErrorMessage = ex.Message;
                _notifications.ShowError("AKS workload restart failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceMutationRunning = false;
            }
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedResourceAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !resource.CanDelete)
        {
            return;
        }

        var actionToken = _selectedResourceActionCts.Token;

        if (!CanExecuteSelectedResourceMutation)
        {
            SelectedResourceActionErrorMessage = "Type CONFIRM before running a production AKS action.";
            return;
        }

        SelectedResourceActionErrorMessage = null;
        IsSelectedResourceMutationRunning = true;

        try
        {
            await Client.DeletePodAsync(resource.Namespace, resource.Name, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("AKS pod deleted", resource.Name);
            await LoadResourceScopeAsync(actionToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceActionErrorMessage = ex.Message;
                _notifications.ShowError("AKS pod delete failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceMutationRunning = false;
            }
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

        var actionToken = _selectedResourceActionCts.Token;

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
                await Client.ScaleDeploymentAsync(resource.Namespace, resource.Name, replicas, actionToken);
            }
            else
            {
                await Client.ScaleStatefulSetAsync(resource.Namespace, resource.Name, replicas, actionToken);
            }

            ThrowIfSelectedResourceActionCanceled(actionToken);
            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("AKS workload scaled", $"{resource.ApiKind} {resource.Name} -> {replicas}");
            await LoadResourceScopeAsync(actionToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceActionErrorMessage = ex.Message;
                _notifications.ShowError("AKS scale action failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceMutationRunning = false;
            }
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

        var actionToken = _selectedResourceActionCts.Token;

        if (!CanExecuteSelectedResourceMutation)
        {
            SelectedResourceActionErrorMessage = "Type CONFIRM before running a production AKS action.";
            return;
        }

        SelectedResourceActionErrorMessage = null;
        IsSelectedResourceMutationRunning = true;

        try
        {
            var createdJobName = await Client.TriggerCronJobAsync(resource.Namespace, resource.Name, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("CronJob triggered", createdJobName);
            await LoadResourceScopeAsync(actionToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceActionErrorMessage = ex.Message;
                _notifications.ShowError("CronJob trigger failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceMutationRunning = false;
            }
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

        var actionToken = _selectedResourceActionCts.Token;

        if (!CanExecuteSelectedResourceMutation)
        {
            SelectedResourceActionErrorMessage = "Type CONFIRM before running a production AKS action.";
            return;
        }

        SelectedResourceActionErrorMessage = null;
        IsSelectedResourceMutationRunning = true;

        try
        {
            var createdJobName = await Client.RerunJobAsync(resource.Namespace, resource.Name, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            SelectedResourceConfirmText = string.Empty;
            _notifications.ShowSuccess("Job rerun started", createdJobName);
            await LoadResourceScopeAsync(actionToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceActionErrorMessage = ex.Message;
                _notifications.ShowError("Job rerun failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceMutationRunning = false;
            }
        }
    }

    private void ResetSelectedResourceBusyStateForDispose()
    {
        IsSelectedResourceYamlLoading = false;
        IsSelectedResourceYamlApplying = false;
        IsSelectedResourceDiagnosticsLoading = false;
        IsSelectedResourceHelmHistoryLoading = false;
        IsSelectedResourceMutationRunning = false;
    }

    private void InvalidateSelectedResourceActionToken()
    {
        if (_isDisposed)
        {
            return;
        }

        if (!_selectedResourceActionCts.IsCancellationRequested)
        {
            _selectedResourceActionCts.Cancel();
        }

        _selectedResourceActionCts.Dispose();
        _selectedResourceActionCts = new CancellationTokenSource();
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

        IsSelectedResourceHelmHistoryPanelOpen = false;
        IsSelectedResourceHelmHistoryLoading = false;
        IsSelectedResourceHelmRollbackMode = false;
        SelectedResourceHelmHistoryTitle = "Helm history";
        SelectedResourceHelmHistorySummary = string.Empty;
        SelectedResourceHelmHistoryItems = [];
        SelectedResourceHelmHistoryErrorMessage = null;

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

    private void ApplyNamespaceQuotaDiagnostics(
        string resourceNamespace,
        IReadOnlyList<ResourceQuotaInfo> quotas,
        IReadOnlyList<LimitRangeInfo> limitRanges)
    {
        SelectedResourceDiagnosticsTitle = $"Namespace quotas · {resourceNamespace}";
        SelectedResourceDiagnosticsSummary = quotas.Count == 0 && limitRanges.Count == 0
            ? "No resource quotas or limit ranges are defined for this namespace."
            : $"{quotas.Count} resource quota object(s) and {limitRanges.Count} limit range object(s) are currently defined.";
        SelectedResourceDiagnosticsFacts =
        [
            new AksResourceFactItemViewModel("Namespace", resourceNamespace),
            new AksResourceFactItemViewModel("Resource quotas", quotas.Count.ToString(CultureInfo.CurrentCulture)),
            new AksResourceFactItemViewModel("Limit ranges", limitRanges.Count.ToString(CultureInfo.CurrentCulture)),
        ];

        var highlights = new List<string>();
        foreach (var quota in quotas)
        {
            if (quota.HardLimits.Count == 0)
            {
                highlights.Add($"Quota · {quota.Name} has no surfaced hard limits.");
                continue;
            }

            foreach (var hardLimit in quota.HardLimits)
            {
                var used = quota.Used.FirstOrDefault(item => string.Equals(item.Resource, hardLimit.Resource, StringComparison.Ordinal));
                highlights.Add($"Quota · {quota.Name} · {hardLimit.Resource}: {used?.Used ?? "—"} / {hardLimit.Hard ?? "—"}");
            }
        }

        foreach (var limitRange in limitRanges)
        {
            foreach (var item in limitRange.Limits)
            {
                highlights.Add($"LimitRange · {limitRange.Name} ({item.Type})");
                foreach (var request in item.DefaultRequests.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    highlights.Add($"Default request · {request.Key}={request.Value}");
                }

                foreach (var limit in item.DefaultLimits.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    highlights.Add($"Default limit · {limit.Key}={limit.Value}");
                }
            }
        }

        SelectedResourceDiagnosticsHighlights = highlights;
    }

    private void ApplyPodDisruptionBudgetDiagnostics(string resourceNamespace, IReadOnlyList<PodDisruptionBudgetInfo> budgets)
    {
        SelectedResourceDiagnosticsTitle = $"Pod disruption budgets · {resourceNamespace}";
        SelectedResourceDiagnosticsSummary = budgets.Count == 0
            ? "No pod disruption budgets were found for this namespace."
            : $"{budgets.Count} disruption budget(s) currently shape planned workload disruption in this namespace.";
        SelectedResourceDiagnosticsFacts =
        [
            new AksResourceFactItemViewModel("Namespace", resourceNamespace),
            new AksResourceFactItemViewModel("Budgets", budgets.Count.ToString(CultureInfo.CurrentCulture)),
            new AksResourceFactItemViewModel(
                "Disruptions allowed",
                budgets.Count(budget => budget.DisruptionsAllowed && budget.AllowedDisruptions > 0).ToString(CultureInfo.CurrentCulture)),
        ];

        var highlights = new List<string>();
        foreach (var budget in budgets)
        {
            var threshold = !string.IsNullOrWhiteSpace(budget.MinAvailable)
                ? $"MinAvailable {budget.MinAvailable}"
                : !string.IsNullOrWhiteSpace(budget.MaxUnavailable)
                    ? $"MaxUnavailable {budget.MaxUnavailable}"
                    : "No threshold surfaced";
            highlights.Add($"Budget · {budget.Name} · {threshold} · Healthy {budget.CurrentHealthy}/{budget.ExpectedPods} · Allowed disruptions {budget.AllowedDisruptions}");
            highlights.AddRange(budget.SelectorLabels.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"Selector · {pair.Key}={pair.Value}"));
        }

        SelectedResourceDiagnosticsHighlights = highlights;
    }

    private void ApplyProbeFailureDiagnostics(AksResourceBrowseItemViewModel resource, ProbeFailureSummary summary)
    {
        SelectedResourceDiagnosticsTitle = $"Probe failures · {resource.Name}";
        SelectedResourceDiagnosticsSummary = summary.Findings.Count == 0
            ? $"Observed {summary.TotalPods} pod(s); {summary.PodsWithRestarts} pod(s) reported restarts in the current summary window."
            : string.Join(" ", summary.Findings);
        SelectedResourceDiagnosticsFacts =
        [
            new AksResourceFactItemViewModel("Namespace", summary.Namespace),
            new AksResourceFactItemViewModel("Workload", $"{summary.WorkloadKind} {summary.WorkloadName}"),
            new AksResourceFactItemViewModel("Pods", summary.TotalPods.ToString(CultureInfo.CurrentCulture)),
            new AksResourceFactItemViewModel("Pods with restarts", summary.PodsWithRestarts.ToString(CultureInfo.CurrentCulture)),
            new AksResourceFactItemViewModel("Recent probe events", summary.RecentProbeEvents.Count.ToString(CultureInfo.CurrentCulture)),
        ];

        var highlights = new List<string>();
        foreach (var pod in summary.Pods)
        {
            var termination = string.IsNullOrWhiteSpace(pod.LastTerminationReason)
                ? "No last termination surfaced"
                : pod.LastTerminationReason;
            highlights.Add($"Pod · {pod.PodName} · Restarts {pod.RestartCount} · Ready {(pod.Ready ? "yes" : "no")} · Liveness {(pod.LivenessProbeConfigured ? "configured" : "missing")} · Readiness {(pod.ReadinessProbeConfigured ? "configured" : "missing")} · Last termination {termination}");
            if (!string.IsNullOrWhiteSpace(pod.LastTerminationMessage))
            {
                highlights.Add($"Termination detail · {pod.LastTerminationMessage}");
            }
        }

        highlights.AddRange(summary.RecentProbeEvents.Select(probeEvent => $"Probe event · {probeEvent}"));
        highlights.AddRange(summary.Findings.Select(finding => $"Finding · {finding}"));
        highlights.Add($"Limitation · {summary.Limitation}");
        SelectedResourceDiagnosticsHighlights = highlights;
    }

    private void ApplyPlacementDiagnostics(AksResourceBrowseItemViewModel resource, PlacementAnalysis analysis)
    {
        SelectedResourceDiagnosticsTitle = $"Placement · {resource.Name}";
        SelectedResourceDiagnosticsSummary = analysis.Findings.Count == 0
            ? "Review declared node, affinity, toleration, and topology spread constraints together with recent scheduling failures."
            : string.Join(" ", analysis.Findings);
        SelectedResourceDiagnosticsFacts =
        [
            new AksResourceFactItemViewModel("Namespace", analysis.Namespace),
            new AksResourceFactItemViewModel("Workload", $"{analysis.WorkloadKind} {analysis.WorkloadName}"),
            new AksResourceFactItemViewModel("Node selector", analysis.HasNodeSelector ? "Yes" : "No"),
            new AksResourceFactItemViewModel("Node affinity", analysis.HasNodeAffinity ? "Yes" : "No"),
            new AksResourceFactItemViewModel("Pod affinity", analysis.HasPodAffinity ? "Yes" : "No"),
            new AksResourceFactItemViewModel("Pod anti-affinity", analysis.HasPodAntiAffinity ? "Yes" : "No"),
            new AksResourceFactItemViewModel("Tolerations", analysis.HasTolerations ? "Yes" : "No"),
            new AksResourceFactItemViewModel("Topology spread", analysis.HasTopologySpreadConstraints ? "Yes" : "No"),
        ];

        var highlights = new List<string>();
        highlights.AddRange(analysis.NodeSelector.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"Node selector · {pair.Key}={pair.Value}"));
        highlights.AddRange(analysis.Tolerations.Select(toleration => $"Toleration · {toleration}"));
        highlights.AddRange(analysis.TopologySpreadKeys.Select(key => $"Topology spread · {key}"));
        highlights.AddRange(analysis.RecentSchedulingFailureEvents.Select(schedulingEvent => $"Scheduling event · {schedulingEvent}"));
        highlights.AddRange(analysis.Findings.Select(finding => $"Finding · {finding}"));
        highlights.Add($"Limitation · {analysis.Limitation}");
        SelectedResourceDiagnosticsHighlights = highlights;
    }

    private void ApplyHelmValuesDiagnostics(AksResourceBrowseItemViewModel resource, string values)
    {
        var normalizedValues = values.ReplaceLineEndings("\n");
        SelectedResourceDiagnosticsTitle = $"Helm values · {resource.Name}";
        SelectedResourceDiagnosticsSummary = "Values snapshot for the selected Helm release.";
        SelectedResourceDiagnosticsFacts =
        [
            new AksResourceFactItemViewModel("Namespace", resource.Namespace),
            new AksResourceFactItemViewModel("Release", resource.Name),
            new AksResourceFactItemViewModel("Lines", normalizedValues.Split('\n').Length.ToString(CultureInfo.CurrentCulture)),
        ];
        SelectedResourceDiagnosticsHighlights = BuildHelmTextHighlights(normalizedValues, "Values");
    }

    private void ApplyHelmPreviewDiagnostics(AksResourceBrowseItemViewModel resource, HelmDiffPreview preview, string title)
    {
        SelectedResourceDiagnosticsTitle = title;
        SelectedResourceDiagnosticsSummary = string.IsNullOrWhiteSpace(preview.CapabilityNote)
            ? "Helm preview returned without a capability note. Review findings and diff output below."
            : preview.CapabilityNote;
        SelectedResourceDiagnosticsFacts =
        [
            new AksResourceFactItemViewModel("Namespace", resource.Namespace),
            new AksResourceFactItemViewModel("Release", resource.Name),
            new AksResourceFactItemViewModel("Capability", preview.Capability.ToString()),
            new AksResourceFactItemViewModel("Findings", preview.Findings.Count.ToString(CultureInfo.CurrentCulture)),
        ];

        var highlights = new List<string>();
        highlights.AddRange(preview.Findings.Select(finding => $"Finding · {finding}"));
        if (!string.IsNullOrWhiteSpace(preview.DiffText))
        {
            highlights.AddRange(BuildHelmTextHighlights(preview.DiffText.ReplaceLineEndings("\n"), "Diff"));
        }

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
        OnPropertyChanged(nameof(SelectedResourceOpenUrlVisibility));
        OnPropertyChanged(nameof(CanOpenSelectedResourceUrl));
        OnPropertyChanged(nameof(SelectedResourceCopyUrlVisibility));
        OnPropertyChanged(nameof(SelectedResourceWorkloadLogsVisibility));
        OnPropertyChanged(nameof(CanOpenSelectedResourceWorkloadLogs));
        OnPropertyChanged(nameof(SelectedResourceWorkloadLogsLabel));
        OnPropertyChanged(nameof(SelectedResourceNamespaceQuotaVisibility));
        OnPropertyChanged(nameof(CanLoadSelectedResourceNamespaceQuotas));
        OnPropertyChanged(nameof(SelectedResourcePodDisruptionBudgetVisibility));
        OnPropertyChanged(nameof(CanLoadSelectedResourcePodDisruptionBudgets));
        OnPropertyChanged(nameof(SelectedResourceProbeFailuresVisibility));
        OnPropertyChanged(nameof(CanLoadSelectedResourceProbeFailures));
        OnPropertyChanged(nameof(SelectedResourcePlacementVisibility));
        OnPropertyChanged(nameof(CanLoadSelectedResourcePlacement));
        OnPropertyChanged(nameof(SelectedResourceHelmValuesVisibility));
        OnPropertyChanged(nameof(CanOpenSelectedResourceHelmValues));
        OnPropertyChanged(nameof(SelectedResourceHelmHistoryVisibility));
        OnPropertyChanged(nameof(CanOpenSelectedResourceHelmHistory));
        OnPropertyChanged(nameof(SelectedResourceHelmPreviewVisibility));
        OnPropertyChanged(nameof(CanPreviewSelectedResourceHelmUpgrade));
        OnPropertyChanged(nameof(SelectedResourceHelmRollbackVisibility));
        OnPropertyChanged(nameof(CanOpenSelectedResourceHelmRollback));
        OnPropertyChanged(nameof(SelectedResourceDiagnosticsPanelVisibility));
        OnPropertyChanged(nameof(SelectedResourceDiagnosticsErrorVisibility));
        OnPropertyChanged(nameof(SelectedResourceDiagnosticsSummaryVisibility));
        OnPropertyChanged(nameof(SelectedResourceDiagnosticsHighlightsVisibility));
        OnPropertyChanged(nameof(SelectedResourceHelmHistoryPanelVisibility));
        OnPropertyChanged(nameof(SelectedResourceHelmHistoryErrorVisibility));
        OnPropertyChanged(nameof(SelectedResourceHelmHistorySummaryVisibility));
        OnPropertyChanged(nameof(SelectedResourceHelmHistoryContentVisibility));
        OnPropertyChanged(nameof(ShowSelectedResourceHelmHistoryEmptyState));
        OnPropertyChanged(nameof(SelectedResourceHelmRollbackActionVisibility));
        OnPropertyChanged(nameof(SelectedResourceRestartVisibility));
        OnPropertyChanged(nameof(CanRestartSelectedResource));
        OnPropertyChanged(nameof(SelectedResourceDeleteVisibility));
        OnPropertyChanged(nameof(CanDeleteSelectedResource));
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

    private void ThrowIfSelectedResourceActionCanceled(CancellationToken actionToken)
    {
        if (_isDisposed || actionToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(actionToken);
        }
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

    private CancellationToken BeginSelectedResourceDiagnosticsLoad()
    {
        SelectedResourceActionErrorMessage = null;
        SelectedResourceDiagnosticsErrorMessage = null;
        SelectedResourceDiagnosticsSummary = string.Empty;
        SelectedResourceDiagnosticsFacts = [];
        SelectedResourceDiagnosticsHighlights = [];
        IsSelectedResourceDiagnosticsPanelOpen = true;
        IsSelectedResourceDiagnosticsLoading = true;
        return _selectedResourceActionCts.Token;
    }

    private async Task LoadSelectedResourceHelmHistoryAsync(AksResourceBrowseItemViewModel resource, bool rollbackMode)
    {
        var actionToken = _selectedResourceActionCts.Token;

        SelectedResourceActionErrorMessage = null;
        SelectedResourceHelmHistoryErrorMessage = null;
        SelectedResourceHelmHistoryItems = [];
        SelectedResourceHelmHistoryTitle = rollbackMode ? $"Rollback · {resource.Name}" : $"History · {resource.Name}";
        SelectedResourceHelmHistorySummary = rollbackMode
            ? "Pick a superseded revision to preview or roll back."
            : "Review release revisions, status, and chart metadata without leaving the native AKS workspace.";
        IsSelectedResourceHelmRollbackMode = rollbackMode;
        IsSelectedResourceHelmHistoryPanelOpen = true;
        IsSelectedResourceHelmHistoryLoading = true;

        try
        {
            var history = await Client!.GetHelmReleaseHistoryAsync(resource.Namespace, resource.Name, actionToken);
            ThrowIfSelectedResourceActionCanceled(actionToken);
            SelectedResourceHelmHistoryItems = history
                .OrderByDescending(item => item.Revision)
                .Select(item => new AksHelmRevisionItemViewModel(item))
                .ToList();

            if (rollbackMode && !SelectedResourceHelmHistoryItems.Any(item => item.CanRollbackTarget))
            {
                SelectedResourceHelmHistoryErrorMessage = $"No previous revisions are available for \"{resource.Name}\".";
            }

            SelectedResourceHelmHistorySummary = rollbackMode
                ? $"{SelectedResourceHelmHistoryItems.Count(item => item.CanRollbackTarget)} rollback target revision(s) are currently available."
                : $"{SelectedResourceHelmHistoryItems.Count} revision(s) are currently recorded for this release.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                SelectedResourceHelmHistoryErrorMessage = ex.Message;
                _notifications.ShowError("AKS Helm history load failed", ex.Message, ex);
            }
        }
        finally
        {
            if (!_isDisposed)
            {
                IsSelectedResourceHelmHistoryLoading = false;
            }
        }
    }

    private static IReadOnlyList<string> BuildHelmTextHighlights(string text, string label)
        => text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => $"{label} · {line}")
            .ToList();
}

public sealed class AksHelmRevisionItemViewModel
{
    public AksHelmRevisionItemViewModel(HelmRevisionInfo revision)
    {
        Revision = revision.Revision;
        Status = string.IsNullOrWhiteSpace(revision.Status) ? "unknown" : revision.Status;
        Chart = revision.Chart ?? "—";
        AppVersion = revision.AppVersion ?? "—";
        UpdatedText = revision.Updated?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";
        Description = string.IsNullOrWhiteSpace(revision.Description) ? "—" : revision.Description!;
    }

    public int Revision { get; }

    public string Status { get; }

    public string Chart { get; }

    public string AppVersion { get; }

    public string UpdatedText { get; }

    public string Description { get; }

    public bool CanRollbackTarget => string.Equals(Status, "superseded", StringComparison.OrdinalIgnoreCase);
}