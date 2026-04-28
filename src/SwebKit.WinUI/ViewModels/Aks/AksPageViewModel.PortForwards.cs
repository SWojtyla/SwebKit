using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SwebKit.Core.Models;

namespace SwebKit.WinUI.ViewModels.Aks;

public sealed partial class AksPageViewModel
{
    public ObservableCollection<AksPortForwardSessionItemViewModel> PortForwardSessions { get; } = [];

    [ObservableProperty]
    public partial bool IsPortForwardFormOpen { get; set; }

    [ObservableProperty]
    public partial bool IsPortForwardStarting { get; set; }

    [ObservableProperty]
    public partial string PortForwardRemotePortText { get; set; } = "80";

    [ObservableProperty]
    public partial string PortForwardLocalPortText { get; set; } = "8080";

    [ObservableProperty]
    public partial string? PortForwardValidationMessage { get; set; }

    public bool HasPortForwardSessions => PortForwardSessions.Count > 0;

    public bool CanStartSelectedPodPortForward => SelectedPod is not null && Client is not null && !IsLoading && !IsPortForwardStarting;

    public bool CanCancelSelectedPodPortForward => !IsPortForwardStarting;

    public bool CanStopAllPortForwardSessions => PortForwardSessions.Any(session => session.CanStop);

    public string PortForwardSummary => PortForwardSessions.Count switch
    {
        0 => "No port-forward sessions are active in the native AKS workspace.",
        1 => "1 port-forward session is currently tracked.",
        _ => $"{PortForwardSessions.Count:N0} port-forward sessions are currently tracked.",
    };

    public string PortForwardSelectedPodLabel => SelectedPod is null
        ? "Select a pod to start a native port-forward session."
        : $"Selected pod: {SelectedPod.Namespace}/{SelectedPod.Name}";

    public Visibility PortForwardFormVisibility => IsPortForwardFormOpen ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PortForwardPanelVisibility => IsPortForwardFormOpen || HasPortForwardSessions
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PortForwardValidationVisibility => string.IsNullOrWhiteSpace(PortForwardValidationMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility PortForwardSessionsVisibility => HasPortForwardSessions ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowPortForwardSessionsEmptyState => !HasPortForwardSessions && !IsPortForwardFormOpen;

    partial void OnIsPortForwardStartingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartSelectedPodPortForward));
        OnPropertyChanged(nameof(CanCancelSelectedPodPortForward));
    }

    partial void OnIsPortForwardFormOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(PortForwardFormVisibility));
        OnPropertyChanged(nameof(PortForwardPanelVisibility));
        OnPropertyChanged(nameof(ShowPortForwardSessionsEmptyState));
        OnPropertyChanged(nameof(SelectedPodLogsPanelVisibility));
    }

    partial void OnPortForwardValidationMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(PortForwardValidationVisibility));
    }

    private void InitializePortForwardState()
    {
        PortForwardSessions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPortForwardSessions));
            OnPropertyChanged(nameof(CanStopAllPortForwardSessions));
            OnPropertyChanged(nameof(PortForwardPanelVisibility));
            OnPropertyChanged(nameof(PortForwardSummary));
            OnPropertyChanged(nameof(PortForwardSessionsVisibility));
            OnPropertyChanged(nameof(ShowPortForwardSessionsEmptyState));
            OnPropertyChanged(nameof(SelectedPodLogsPanelVisibility));
        };

        _portForwardSessions.SessionsChanged += OnPortForwardSessionsChanged;
        SyncPortForwardSessions();
    }

    private void DisposePortForwardState()
    {
        _portForwardSessions.SessionsChanged -= OnPortForwardSessionsChanged;
    }

    private void OnPortForwardSessionsChanged()
    {
        ExecuteOnUiThread(SyncPortForwardSessions);
    }

    [RelayCommand]
    private void OpenSelectedPodPortForward()
    {
        if (SelectedPod is null)
        {
            return;
        }

        const int defaultRemotePort = 80;
        PortForwardRemotePortText = defaultRemotePort.ToString(CultureInfo.InvariantCulture);
        PortForwardLocalPortText = SuggestLocalPort(defaultRemotePort).ToString(CultureInfo.InvariantCulture);
        PortForwardValidationMessage = null;
        IsPortForwardFormOpen = true;
    }

    [RelayCommand]
    private void CancelSelectedPodPortForward()
    {
        PortForwardValidationMessage = null;
        IsPortForwardFormOpen = false;
    }

    [RelayCommand]
    private async Task StartSelectedPodPortForwardAsync()
    {
        var selectedPod = SelectedPod;
        var client = Client;

        if (selectedPod is null || client is null)
        {
            return;
        }

        if (!TryParseRemotePort(PortForwardRemotePortText, out var remotePort, out var remoteMessage))
        {
            PortForwardValidationMessage = remoteMessage;
            return;
        }

        if (!TryParseLocalPort(PortForwardLocalPortText, out var localPort, out var localMessage))
        {
            PortForwardValidationMessage = localMessage;
            return;
        }

        if (!TryProbeLocalPort(localPort, out var suggestedPort))
        {
            PortForwardValidationMessage = suggestedPort.HasValue
                ? $"Local port {localPort} is already in use. Try {suggestedPort.Value} instead."
                : $"Local port {localPort} is already in use.";
            return;
        }

        PortForwardValidationMessage = null;
        IsPortForwardStarting = true;

        try
        {
            await _portForwardSessions.StartAsync(client, selectedPod.Namespace, selectedPod.Name, localPort, remotePort);
            _notifications.ShowInfo("Port-forward starting", $"{selectedPod.Name} is being forwarded on localhost:{localPort}");
            IsPortForwardFormOpen = false;
            SyncPortForwardSessions();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AKS port-forward start failed for {Namespace}/{PodName}.", selectedPod.Namespace, selectedPod.Name);
            PortForwardValidationMessage = ex.Message;
            _notifications.ShowError("Port-forward failed", ex.Message, ex);
        }
        finally
        {
            IsPortForwardStarting = false;
        }
    }

    [RelayCommand]
    private async Task StopPortForwardSessionAsync(AksPortForwardSessionItemViewModel? sessionItem)
    {
        if (sessionItem is null)
        {
            return;
        }

        try
        {
            await _portForwardSessions.StopAsync(sessionItem.Session);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AKS port-forward stop failed for {Namespace}/{PodName}.", sessionItem.Namespace, sessionItem.PodName);
            _notifications.ShowError("Failed to stop port-forward", ex.Message, ex);
        }
    }

    [RelayCommand]
    private async Task StopAllPortForwardSessionsAsync()
    {
        try
        {
            await _portForwardSessions.StopAllAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stopping all AKS port-forward sessions failed.");
            _notifications.ShowError("Failed to stop port-forward sessions", ex.Message, ex);
        }
    }

    private void SyncPortForwardSessions()
    {
        var sessions = _portForwardSessions.Sessions
            .OrderByDescending(session => session.StartedAt)
            .Select(session => new AksPortForwardSessionItemViewModel(session))
            .ToList();

        PortForwardSessions.Clear();
        foreach (var session in sessions)
        {
            PortForwardSessions.Add(session);
        }
    }

    private static bool TryParseRemotePort(string rawValue, out int port, out string validationMessage)
    {
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
        {
            validationMessage = "Remote ports must be whole numbers between 1 and 65535.";
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }

    private static bool TryParseLocalPort(string rawValue, out int port, out string validationMessage)
    {
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
        {
            validationMessage = "Local ports must be whole numbers between 1 and 65535.";
            return false;
        }

        if (port < 1024)
        {
            validationMessage = "Choose a local port above 1023 unless you explicitly need a privileged port.";
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }

    private static bool TryProbeLocalPort(int port, out int? suggestedPort)
    {
        if (IsLocalPortAvailable(port))
        {
            suggestedPort = null;
            return true;
        }

        suggestedPort = Enumerable.Range(port + 1, Math.Max(0, 65535 - port))
            .Take(20)
            .FirstOrDefault(candidate => IsLocalPortAvailable(candidate));

        if (suggestedPort == 0)
        {
            suggestedPort = null;
        }

        return false;
    }

    private static bool IsLocalPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int SuggestLocalPort(int remotePort)
        => remotePort switch
        {
            80 => 8080,
            443 => 8443,
            < 1024 => remotePort + 8000,
            _ => remotePort,
        };
}

public sealed class AksPortForwardSessionItemViewModel
{
    public AksPortForwardSessionItemViewModel(PortForwardSession session)
    {
        Session = session;
        PodName = session.ResourceName;
        Namespace = session.Namespace;
        PortsLabel = $"localhost:{session.LocalPort} -> :{session.RemotePort}";
        StatusLabel = session.Status.ToString();
        StartedText = session.StartedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
        LocalUrl = session.LocalUrl;
        ErrorText = session.LastError;
        HasError = !string.IsNullOrWhiteSpace(session.LastError);
        ErrorVisibility = HasError ? Visibility.Visible : Visibility.Collapsed;
        CanStop = session.Status is not PortForwardStatus.Stopping and not PortForwardStatus.Stopped;
    }

    public PortForwardSession Session { get; }

    public string PodName { get; }

    public string Namespace { get; }

    public string PortsLabel { get; }

    public string StatusLabel { get; }

    public string StartedText { get; }

    public string LocalUrl { get; }

    public string? ErrorText { get; }

    public bool HasError { get; }

    public Visibility ErrorVisibility { get; }

    public bool CanStop { get; }
}