using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using System.Security;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace SwebKit.App.Platforms.Windows;

public class WindowsToastNotificationService : IWindowsNotificationService
{
    private readonly ILogger<WindowsToastNotificationService> _logger;

    public WindowsToastNotificationService(ILogger<WindowsToastNotificationService> logger)
    {
        _logger = logger;
    }

    public void ShowPodAlert(PodHealthEvent evt)
    {
        try
        {
            var title = SecurityElement.Escape($"Pod {evt.EventType}: {evt.PodName}");
            var body = SecurityElement.Escape($"{evt.Namespace} \u2014 {evt.CurrentPhase}");
            var attribution = SecurityElement.Escape($"{evt.ClusterContext} \u00b7 {evt.DetectedAt:HH:mm:ss}");

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
            doc.LoadXml(xml);

            var notification = new ToastNotification(doc);
            // Use explicit AppUserModelId registered at startup via RegisterAumidInRegistry + SetCurrentProcessExplicitAppUserModelID.
            ToastNotificationManager.CreateToastNotifier("SwebKit.App").Show(notification);
            _logger.LogDebug("Windows toast shown for pod {PodName}", evt.PodName);
        }
        catch (Exception ex)
        {
            // Toast failures must never surface to the monitoring loop.
            _logger.LogWarning(ex, "Windows toast notification failed for pod {PodName}", evt.PodName);
        }
    }

    public void ShowAlert(AlertFiredEvent evt)
    {
        try
        {
            var title = SecurityElement.Escape($"{evt.Severity}: {evt.RuleName}");
            var body = SecurityElement.Escape(evt.Message);
            var attribution = SecurityElement.Escape($"{evt.Source} \u00b7 {evt.FiredAt:HH:mm:ss} \u00b7 {evt.ProfileName}");

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
            doc.LoadXml(xml);

            ToastNotificationManager.CreateToastNotifier("SwebKit.App").Show(new ToastNotification(doc));
            _logger.LogDebug("Windows toast shown for alert rule {RuleName}", evt.RuleName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Windows toast notification failed for alert rule {RuleName}", evt.RuleName);
        }
    }
}
