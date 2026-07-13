using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using System.Security;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace SwebKit.App.Platforms.Windows;

public class WindowsToastNotificationService : IWindowsNotificationService
{
    // Must match the AUMID registered at startup via RegisterAumidInRegistry +
    // SetCurrentProcessExplicitAppUserModelID in App.xaml.cs.
    private const string Aumid = "SwebKit.App";

    private readonly ILogger<WindowsToastNotificationService> _logger;
    private readonly object _capabilityGate = new();
    private ToastCapability _capability = ToastCapability.Unknown;

    public WindowsToastNotificationService(ILogger<WindowsToastNotificationService> logger)
    {
        _logger = logger;
    }

    public ToastCapability Capability
    {
        get { lock (_capabilityGate) return _capability; }
    }

    private void SetCapability(ToastCapability capability)
    {
        lock (_capabilityGate) _capability = capability;
    }

    public ToastCapability ProbeCapability()
    {
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier(Aumid);

            // NotificationSetting reflects Focus Assist/DND, per-app toggles, and group policy.
            // Per DEC-4 we only record this — we still attempt toasts regardless of the reading.
            var capability = notifier.Setting switch
            {
                NotificationSetting.Enabled => ToastCapability.Available(),
                NotificationSetting.DisabledForApplication =>
                    ToastCapability.Unavailable("notifications are turned off for SwebKit"),
                NotificationSetting.DisabledForUser =>
                    ToastCapability.Unavailable("notifications are turned off for this user"),
                NotificationSetting.DisabledByGroupPolicy =>
                    ToastCapability.Unavailable("notifications are disabled by group policy"),
                NotificationSetting.DisabledByManifest =>
                    ToastCapability.Unavailable("app is not registered for notifications"),
                _ => ToastCapability.Available(),
            };

            SetCapability(capability);
            if (capability.IsAvailable)
                _logger.LogDebug("Toast capability probe: available.");
            else
                _logger.LogWarning("Toast capability probe: unavailable — {Reason}.", capability.Reason);

            return capability;
        }
        catch (Exception ex)
        {
            var capability = ToastCapability.Unavailable($"toast notifier could not be created: {ex.Message}");
            SetCapability(capability);
            _logger.LogWarning(ex, "Toast capability probe failed — notifier could not be created.");
            return capability;
        }
    }

    public ToastDeliveryResult ShowPodAlert(PodHealthEvent evt)
    {
        var title = SecurityElement.Escape($"Pod {evt.EventType}: {evt.PodName}");
        var body = SecurityElement.Escape($"{evt.Namespace} \u2014 {evt.CurrentPhase}");
        var attribution = SecurityElement.Escape($"{evt.ClusterContext} \u00b7 {evt.DetectedAt:HH:mm:ss}");

        return TryShow(title, body, attribution, $"pod {evt.PodName}");
    }

    public ToastDeliveryResult ShowAlert(AlertFiredEvent evt)
    {
        var title = SecurityElement.Escape($"{evt.Severity}: {evt.RuleName}");
        var body = SecurityElement.Escape(evt.Message);
        var attribution = SecurityElement.Escape($"{evt.Source} \u00b7 {evt.FiredAt:HH:mm:ss} \u00b7 {evt.ProfileName}");

        return TryShow(title, body, attribution, $"alert rule {evt.RuleName}");
    }

    /// <summary>
    /// Attempts to show a toast. Never throws — returns a delivery result the caller acts on. On
    /// failure the capability state is updated so downstream diagnostics have a reason to surface.
    /// </summary>
    private ToastDeliveryResult TryShow(string title, string body, string attribution, string context)
    {
        try
        {
            var xml = $"""
                <toast>
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{title}</text>
                      <text>{body}</text>
                      <text hint-style="captionSubtle">{attribution}</text>
                    </binding>
                  </visual>
                </toast>
                """;

            var doc = new XmlDocument();
            // Harden against XXE: the toast payload is app-generated and escaped, but explicitly
            // prohibit DTDs so no external entities/DTDs are ever processed.
            doc.LoadXml(xml, new XmlLoadSettings { ProhibitDtd = true });

            ToastNotificationManager.CreateToastNotifier(Aumid).Show(new ToastNotification(doc));
            SetCapability(ToastCapability.Available());
            _logger.LogDebug("Windows toast shown for {Context}", context);
            return ToastDeliveryResult.Shown();
        }
        catch (Exception ex)
        {
            // Toast failures must never surface to the monitoring loop. Record the reason so the
            // caller can raise the in-app fallback + one-time diagnostic instead of losing the alert.
            SetCapability(ToastCapability.Unavailable(ex.Message));
            _logger.LogWarning(ex, "Windows toast notification failed for {Context}", context);
            return ToastDeliveryResult.Failed(ex.Message);
        }
    }
}

