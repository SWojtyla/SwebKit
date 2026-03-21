namespace SwebKit.Core.Models;

public enum NotificationSeverity { Info, Success, Warning, Error }

public record Notification(
    Guid Id,
    NotificationSeverity Severity,
    string Message,
    string? Detail,
    DateTimeOffset Timestamp
);
